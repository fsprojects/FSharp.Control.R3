namespace FSharp.Control.R3.Tests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Time.Testing
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests.TestHelpers

/// Assertions and building blocks shared by the chunk tests.
[<AutoOpen>]
module private ChunkTestHelpers =

    /// <summary>
    /// Compares the chunks one by one, because <see cref="T:Microsoft.VisualStudio.TestTools.UnitTesting.CollectionAssert"/>
    /// compares nested arrays by reference.
    /// </summary>
    let assertChunks (expected : 'T array array) (actual : 'T array array) (message : string) =
        Assert.HasCount (expected.Length, actual, message)

        expected
        |> Array.iteri (fun index chunk -> CollectionAssert.AreEqual (chunk, actual[index], $"%s{message} (chunk %d{index})"))

    /// Asserts the whole outcome of a chunked sequence that completed successfully without reporting an error.
    let assertCompletedWith (expected : 'T array array) (recorder : Recorder<'T array>) (message : string) =
        assertChunks expected recorder.Values message
        Assert.IsEmpty (recorder.Errors, "The chunked sequence must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "The chunked sequence must complete successfully")

    /// Asserts that every element opened a window that failed with the error and still emitted that element as a chunk.
    let assertEveryWindowFailed (error : exn) (elements : int array) (recorder : Recorder<int array>) =
        assertChunks (elements |> Array.map Array.singleton) recorder.Values "Every failed window must still emit its chunk"
        Assert.HasCount (elements.Length, recorder.Errors, "Every window failure must be reported once")

        for reported in recorder.Errors do
            Assert.AreSame (error, reported, "The original exception must be reported, without a wrapper")

        Assert.IsTrue (recorder.IsCompletedSuccessfully, "A window failure must not terminate the chunked sequence")

    /// <summary>
    /// A <see cref="T:FSharp.Control.R3.ChunkConfiguration`1.ChunkAsyncWindow"/> window that runs until the test releases
    /// the gate of the element that opened it.
    /// </summary>
    let gatedWindow (selector : GatedSelector<int, unit>) =
        Func<int, CancellationToken, ValueTask>(fun value cancellationToken -> ValueTask (selector.InvokeTask cancellationToken value :> Task))

    /// <summary>
    /// Every chunking function, configured so that a chunk closes only when it holds two elements or when the source
    /// terminates: the test never advances the fake time, the window never finishes and the boundaries never emit.
    /// </summary>
    let everyChunker (time : TimeProvider) (boundaries : Observable<int>) : struct (string * (Observable<int> -> Observable<int array>)) array =
        // A window that never finishes leaves its elements to the flush on completion
        let endlessWindow =
            Func<int, CancellationToken, ValueTask>(fun _ _ -> ValueTask ((TaskCompletionSource ()).Task))

        [|
            struct ("chunkBySize", Observable.chunkBySize 2)
            struct ("chunkBy ChunkCount", Observable.chunkBy (ChunkCount 2))
            struct ("chunkBy ChunkTimeSpan", Observable.chunkBy (ChunkTimeSpan (TimeSpan.FromSeconds 1., time)))
            struct ("chunkBy ChunkTimeSpanCount", Observable.chunkBy (ChunkTimeSpanCount (TimeSpan.FromSeconds 1., 2, time)))
            struct ("chunkBy ChunkMilliseconds", Observable.chunkBy (ChunkMilliseconds (1000, time)))
            struct ("chunkBy ChunkMillisecondsCount", Observable.chunkBy (ChunkMillisecondsCount (1000, 2, time)))
            struct ("chunkBy ChunkAsyncWindow", Observable.chunkBy (ChunkAsyncWindow (endlessWindow, true)))
            struct ("chunkBy ChunkWindowBoundaries", Observable.chunkBy (ChunkWindowBoundaries boundaries))
            struct ("chunkByBoundaries", Observable.chunkByBoundaries boundaries)
        |]

[<TestClass; ChunkTestCategory>]
type ChunkTests (testContext : TestContext) =

    // Behaviour that every chunking function shares

    [<TestMethod>]
    member _.``every chunking function emits no chunk for an empty source and completes successfully`` () =
        let time = FakeTimeProvider ()
        use boundaries = new Subject<int> ()

        for struct (name, chunk) in everyChunker time boundaries do
            // Empty completes during Subscribe, and no R3 chunk operator flushes an empty buffer
            use recorder =
                (Observable.empty () : Observable<int>)
                |> chunk
                |> Recorder.Attach
            Assert.IsEmpty (recorder.Values, $"%s{name} must not emit a chunk for an empty source")
            Assert.IsEmpty (recorder.Errors, $"%s{name} must not report an error for an empty source")
            Assert.IsTrue (recorder.IsCompletedSuccessfully, $"%s{name} must complete when the empty source completes")

    [<TestMethod>]
    member _.``every chunking function forwards OnErrorResume of the source and keeps buffering`` () =
        let boom : exn = InvalidOperationException "boom"
        let time = FakeTimeProvider ()
        use boundaries = new Subject<int> ()

        for struct (name, chunk) in everyChunker time boundaries do
            // resumingError emits 1, reports the error, emits 2 and completes, all during Subscribe
            use recorder = Sources.resumingError boom |> chunk |> Recorder.Attach
            assertChunks [| [| 1; 2 |] |] recorder.Values $"%s{name} must keep the elements around the error in one chunk"
            let error = Assert.ContainsSingle (recorder.Errors, $"%s{name} must forward the error once")
            Assert.AreSame (boom, error, $"%s{name} must forward the original error")
            Assert.IsTrue (recorder.IsCompletedSuccessfully, $"%s{name} must not terminate on a resumed error")

    [<TestMethod>]
    member _.``every chunking function flushes the buffered elements before forwarding a source failure`` () =
        let boom : exn = InvalidOperationException "boom"
        let time = FakeTimeProvider ()
        use boundaries = new Subject<int> ()

        for struct (name, chunk) in everyChunker time boundaries do
            // Every R3 chunk operator flushes its buffer on completion, whatever the result
            use recorder =
                Sources.failingAfter [| 1 |] boom
                |> chunk
                |> Recorder.Attach
            assertChunks [| [| 1 |] |] recorder.Values $"%s{name} must flush the buffered element before the failure"
            Assert.IsEmpty (recorder.Errors, $"%s{name} must not report the terminal failure as a resumed error")
            assertFailedWith boom recorder $"%s{name} must forward the failure of the source"

    // chunkBySize

    [<TestMethod>]
    member _.``chunkBySize emits each chunk as soon as it is full and flushes the remainder on completion`` () =
        use subject = new Subject<int> ()
        use recorder = subject |> Observable.chunkBySize 2 |> Recorder.Attach

        // Subject and Chunk(count) deliver synchronously on the calling thread
        subject.OnNext 1
        Assert.IsEmpty (recorder.Values, "A chunk must not be emitted before it is full")
        subject.OnNext 2
        assertChunks [| [| 1; 2 |] |] recorder.Values "A chunk must be emitted as soon as it is full"
        subject.OnNext 3
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |]; [| 3 |] |] recorder "Completion must flush the partial chunk"

    [<TestMethod>]
    member _.``chunkBySize emits no empty chunk when the length of the source is a multiple of the size`` () =
        use recorder =
            Sources.values [| 1..4 |]
            |> Observable.chunkBySize 2
            |> Recorder.Attach

        assertCompletedWith [| [| 1; 2 |]; [| 3; 4 |] |] recorder "Completion must not add an empty chunk after full chunks"

    [<TestMethod>]
    member _.``chunkBySize flushes the partial chunk before forwarding a failure`` () =
        let boom : exn = InvalidOperationException "boom"
        use recorder =
            Sources.failingAfter [| 1; 2; 3 |] boom
            |> Observable.chunkBySize 2
            |> Recorder.Attach

        assertChunks [| [| 1; 2 |]; [| 3 |] |] recorder.Values "R3 must flush the partial chunk on a failed completion too"
        Assert.IsEmpty (recorder.Errors, "The terminal failure must not be reported as a resumed error")
        assertFailedWith boom recorder "The failure of the source must be forwarded"

    [<TestMethod; DataRow 0; DataRow -1>]
    member _.``chunkBySize rejects a non-positive size when called, before subscribing`` (size : int) =
        // The library validates the size itself: R3 would also reject it, but with its message as the parameter name and no value
        assertArgumentRejected (Observable.chunkBySize size) "chunkSize" size "A non-positive chunk size must be rejected when chunkBySize is called"

    // chunkBy ChunkCount

    [<TestMethod>]
    member _.``chunkBy ChunkCount splits the source into chunks of the count and flushes the remainder`` () =
        use recorder =
            Sources.values [| 1..5 |]
            |> Observable.chunkBy (ChunkCount 2)
            |> Recorder.Attach

        assertCompletedWith [| [| 1; 2 |]; [| 3; 4 |]; [| 5 |] |] recorder "ChunkCount must chunk like chunkBySize"

    [<TestMethod; DataRow 0; DataRow -1>]
    member _.``chunkBy ChunkCount rejects a non-positive count when called, before subscribing`` (count : int) =
        assertArgumentRejected
            (Observable.chunkBy (ChunkCount count))
            "configuration"
            count
            "A non-positive count must be rejected when chunkBy is called"

    // chunkBy ChunkTimeSpan

    [<TestMethod>]
    member _.``chunkBy ChunkTimeSpan emits the chunk when the window opened by its first element elapses`` () =
        let time = FakeTimeProvider ()
        use subject = new Subject<int> ()
        use recorder =
            subject
            |> Observable.chunkBy (ChunkTimeSpan (TimeSpan.FromSeconds 3., time))
            |> Recorder.Attach

        // R3 starts the window timer at the first element of a window, so idle time opens no window
        time.Advance (TimeSpan.FromSeconds 10.)
        subject.OnNext 1
        time.Advance (TimeSpan.FromSeconds 2.)
        subject.OnNext 2
        Assert.IsEmpty (recorder.Values, "No chunk may be emitted before the window elapses")
        // FakeTimeProvider fires the due timer synchronously inside Advance
        time.Advance (TimeSpan.FromSeconds 1.)
        assertChunks [| [| 1; 2 |] |] recorder.Values "The window must emit its elements 3 seconds after its first element"
        subject.OnNext 3
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |]; [| 3 |] |] recorder "Completion must flush the window opened by the last element"

    [<TestMethod>]
    member _.``chunkBy ChunkTimeSpan never emits an empty chunk while nothing is buffered`` () =
        let time = FakeTimeProvider ()
        use subject = new Subject<int> ()
        use recorder =
            subject
            |> Observable.chunkBy (ChunkTimeSpan (TimeSpan.FromSeconds 3., time))
            |> Recorder.Attach

        time.Advance (TimeSpan.FromHours 1.)
        Assert.IsEmpty (recorder.Values, "No window may elapse before the first element")
        subject.OnNext 1
        time.Advance (TimeSpan.FromSeconds 3.)
        // The window timer fires once, and the next one starts only with the next element
        time.Advance (TimeSpan.FromHours 1.)
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1 |] |] recorder "Only the window of the single element may emit a chunk"

    // chunkBy ChunkTimeSpanCount

    [<TestMethod>]
    member _.``chunkBy ChunkTimeSpanCount emits on reaching the count and the next element opens a new window`` () =
        let time = FakeTimeProvider ()
        use subject = new Subject<int> ()

        use recorder =
            subject
            |> Observable.chunkBy (ChunkTimeSpanCount (TimeSpan.FromSeconds 3., 2, time))
            |> Recorder.Attach

        subject.OnNext 1
        time.Advance (TimeSpan.FromSeconds 2.)
        subject.OnNext 2
        assertChunks [| [| 1; 2 |] |] recorder.Values "A full chunk must be emitted as soon as the count is reached"
        // Reaching the count stops the window timer, which would otherwise emit an empty chunk at its due time
        time.Advance (TimeSpan.FromSeconds 2.)
        assertChunks [| [| 1; 2 |] |] recorder.Values "The stopped window of the full chunk must not emit"
        subject.OnNext 3
        time.Advance (TimeSpan.FromSeconds 2.)
        assertChunks [| [| 1; 2 |] |] recorder.Values "The next window must start with its own first element"
        time.Advance (TimeSpan.FromSeconds 1.)
        assertChunks [| [| 1; 2 |]; [| 3 |] |] recorder.Values "A window that elapses before the count is reached must emit the partial chunk"
        subject.OnNext 4
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |]; [| 3 |]; [| 4 |] |] recorder "Completion must flush the partial chunk"

    [<TestMethod; DataRow 0; DataRow -1>]
    member _.``chunkBy ChunkTimeSpanCount rejects a non-positive count when called, before subscribing`` (count : int) =
        let time = FakeTimeProvider ()

        // R3 does not validate this count: 0 would fail every element and a negative count would fail only at subscription
        assertArgumentRejected
            (Observable.chunkBy (ChunkTimeSpanCount (TimeSpan.FromSeconds 1., count, time)))
            "configuration"
            count
            "A non-positive count must be rejected when chunkBy is called"

    // chunkBy ChunkMilliseconds

    [<TestMethod>]
    member _.``chunkBy ChunkMilliseconds emits the chunk exactly when the window of that many milliseconds elapses`` () =
        let time = FakeTimeProvider ()
        use subject = new Subject<int> ()
        use recorder =
            subject
            |> Observable.chunkBy (ChunkMilliseconds (1500, time))
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        time.Advance (TimeSpan.FromMilliseconds 1499.)
        Assert.IsEmpty (recorder.Values, "The window must still be open 1 ms before its due time")
        // FakeTimeProvider fires a timer exactly at its due time, so this proves a window of 1500 milliseconds
        time.Advance (TimeSpan.FromMilliseconds 1.)
        assertChunks [| [| 1; 2 |] |] recorder.Values "The window must emit its elements 1500 ms after its first element"
        subject.OnNext 3
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |]; [| 3 |] |] recorder "Completion must flush the window opened by the last element"

    // chunkBy ChunkMillisecondsCount

    [<TestMethod>]
    member _.``chunkBy ChunkMillisecondsCount emits on reaching the count or exactly when the window elapses`` () =
        let time = FakeTimeProvider ()
        use subject = new Subject<int> ()
        use recorder =
            subject
            |> Observable.chunkBy (ChunkMillisecondsCount (1500, 2, time))
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        assertChunks [| [| 1; 2 |] |] recorder.Values "A full chunk must be emitted as soon as the count is reached"
        subject.OnNext 3
        time.Advance (TimeSpan.FromMilliseconds 1499.)
        assertChunks [| [| 1; 2 |] |] recorder.Values "The window of the partial chunk must still be open 1 ms before its due time"
        time.Advance (TimeSpan.FromMilliseconds 1.)
        assertChunks [| [| 1; 2 |]; [| 3 |] |] recorder.Values "The window must emit the partial chunk exactly when it elapses"
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |]; [| 3 |] |] recorder "Completion must not emit an empty chunk"

    [<TestMethod; DataRow 0; DataRow -1>]
    member _.``chunkBy ChunkMillisecondsCount rejects a non-positive count when called, before subscribing`` (count : int) =
        let time = FakeTimeProvider ()

        assertArgumentRejected
            (Observable.chunkBy (ChunkMillisecondsCount (1000, count, time)))
            "configuration"
            count
            "A non-positive count must be rejected when chunkBy is called"

    // chunkBy ChunkAsyncWindow

    [<TestMethod>]
    member _.``chunkBy ChunkAsyncWindow opens a window at the first element and emits the chunk when the window finishes`` () : Task = task {
        use subject = new Subject<int> ()
        let window = GatedSelector<int, unit> ignore
        use recorder =
            subject
            |> Observable.chunkBy (ChunkAsyncWindow (gatedWindow window, true))
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        subject.OnNext 3
        // R3 opens a window only for an element that arrives while no window runs, so 2 and 3 join the window of 1
        CollectionAssert.AreEqual ([| 1 |], window.Started, "Only the first element may open a window")
        Assert.IsEmpty (recorder.Values, "No chunk may be emitted while the window runs")

        window.Release 1
        do! recorder.WaitForValuesAsync (testContext.CancellationToken, 1)
        // R3 emits the chunk and closes the window under one lock, so an element pushed now always opens a new window
        subject.OnNext 4
        CollectionAssert.AreEqual ([| 1; 4 |], window.Started, "The first element after a window closed must open a new window")

        window.Release 4
        do! recorder.WaitForValuesAsync (testContext.CancellationToken, 2)
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2; 3 |]; [| 4 |] |] recorder "Every window must emit the elements that arrived while it ran"
    }

    [<TestMethod>]
    member _.``chunkBy ChunkAsyncWindow flushes the open window on completion and cancels the window token`` () =
        use subject = new Subject<int> ()
        let window = GatedSelector<int, unit> ignore
        use recorder =
            subject
            |> Observable.chunkBy (ChunkAsyncWindow (gatedWindow window, true))
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        let token = Assert.ContainsSingle (window.Tokens, "Only the first element may open a window")
        Assert.IsFalse (token.IsCancellationRequested, "The window token must stay active while the source runs")
        // The window is never released: R3 cancels the window token, flushes the buffer and completes, all inside OnCompleted
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |] |] recorder "Completion must flush the elements of the open window"
        Assert.IsTrue (token.IsCancellationRequested, "Completion must cancel the token of the open window")

    [<TestMethod>]
    member _.``chunkBy ChunkAsyncWindow with a synchronous window emits one chunk per element`` () =
        let opened = ResizeArray<int>()

        let synchronousWindow =
            Func<int, CancellationToken, ValueTask>(fun value _ ->
                opened.Add value
                ValueTask.CompletedTask
            )

        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.chunkBy (ChunkAsyncWindow (synchronousWindow, true))
            |> Recorder.Attach

        // A window that completes synchronously closes inside OnNext, so every element opens and closes its own window
        CollectionAssert.AreEqual ([| 1; 2; 3 |], opened.ToArray (), "Every element must open a window")
        assertCompletedWith [| [| 1 |]; [| 2 |]; [| 3 |] |] recorder "A synchronous window must emit every element alone"

    [<TestMethod>]
    member _.``chunkBy ChunkAsyncWindow resumes a window failure and still emits the chunk`` () =
        let boom : exn = InvalidOperationException "boom"
        let failingWindow =
            Func<int, CancellationToken, ValueTask>(fun _ _ -> ValueTask (Task.FromException boom))

        use recorder =
            Sources.values [| 1; 2 |]
            |> Observable.chunkBy (ChunkAsyncWindow (failingWindow, true))
            |> Recorder.Attach

        // R3 reports a window exception through OnErrorResume and still emits the chunk when the window ends
        assertEveryWindowFailed boom [| 1; 2 |] recorder

    [<TestMethod>]
    member _.``disposing a chunkBy ChunkAsyncWindow subscription cancels the window token and unsubscribes from the source`` () =
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let window = GatedSelector<int, unit> ignore

        use recorder =
            subject
            |> probe.Watch
            |> Observable.chunkBy (ChunkAsyncWindow (gatedWindow window, true))
            |> Recorder.Attach

        subject.OnNext 1
        let token = Assert.ContainsSingle (window.Tokens, "The first element must open a window")
        Assert.IsFalse (token.IsCancellationRequested, "The window token must stay active until the subscription is disposed")
        recorder.Dispose ()

        Assert.IsTrue (token.IsCancellationRequested, "Disposing must cancel the token of the running window")
        // The subject never completes by itself, so this disposal proves the unsubscription
        Assert.AreEqual (1, probe.Disposed, "Disposing must unsubscribe from the source")
        // Nothing is asserted on the recorder: disposing only cancels the window token, R3 keeps the buffer and would still
        // emit it when the window ends, and only the disposed recorder, which ignores every notification, discards it

    [<TestMethod; DataRow true; DataRow false>]
    member _.``chunkBy ChunkAsyncWindow resumes the window on the captured synchronization context only when ConfigureAwait is true``
        (configureAwait : bool)
        : Task
        = task {
        use subject = new Subject<int> ()
        // The gate resumes its awaiter asynchronously, so only ConfigureAwait decides where the window continuation runs
        let gate = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)
        let window = Func<int, CancellationToken, ValueTask>(fun _ _ -> ValueTask gate.Task)
        use recorder =
            subject
            |> Observable.chunkBy (ChunkAsyncWindow (window, configureAwait))
            |> Recorder.Attach
        let context = RecordingSynchronizationContext ()

        // R3 awaits the window inside OnNext, so the window captures the context that is current when the element arrives
        context.Run (fun () -> subject.OnNext 1)
        gate.SetResult ()
        do! recorder.WaitForValuesAsync (testContext.CancellationToken, 1)

        assertChunks [| [| 1 |] |] recorder.Values "The window must emit its element when it finishes"
        let expectedPosts = if configureAwait then 1 else 0
        Assert.AreEqual (expectedPosts, context.Posts, "Only a window awaited with ConfigureAwait may resume on the captured context")
    }

    // chunkBy ChunkWindowBoundaries

    [<TestMethod>]
    member _.``chunkBy ChunkWindowBoundaries emits the buffer on every boundary, including empty chunks`` () =
        use subject = new Subject<int> ()
        use boundaries = new Subject<int> ()
        use recorder =
            subject
            |> Observable.chunkBy (ChunkWindowBoundaries boundaries)
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        Assert.IsEmpty (recorder.Values, "Elements must be buffered until a boundary arrives")
        boundaries.OnNext 0
        // R3 emits an empty chunk for a boundary that finds nothing buffered
        boundaries.OnNext 0
        subject.OnNext 3
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |]; [||]; [| 3 |] |] recorder "Every boundary and the completion must emit the buffer"

    [<TestMethod>]
    member _.``chunkBy ChunkWindowBoundaries forwards a boundary OnErrorResume and keeps chunking`` () =
        let boom : exn = InvalidOperationException "boom"
        use subject = new Subject<int> ()
        use boundaries = new Subject<int> ()
        use recorder =
            subject
            |> Observable.chunkBy (ChunkWindowBoundaries boundaries)
            |> Recorder.Attach

        subject.OnNext 1
        // R3 reports an error of the boundaries through the chunked sequence instead of terminating it
        boundaries.OnErrorResume boom
        subject.OnNext 2
        boundaries.OnNext 0

        let error =
            Assert.ContainsSingle (recorder.Errors, "The error of the boundaries must be forwarded once")
        Assert.AreSame (boom, error, "The original error of the boundaries must be forwarded")
        assertChunks [| [| 1; 2 |] |] recorder.Values "The error of the boundaries must not close the chunk"
        Assert.IsTrue (recorder.Completion.IsNone, "The error of the boundaries must not complete the chunked sequence")

    [<TestMethod>]
    member _.``chunkBy ChunkWindowBoundaries flushes and completes successfully when the boundaries fail`` () =
        let boom : exn = InvalidOperationException "boom"
        use subject = new Subject<int> ()
        use boundaries = new Subject<int> ()
        let probe = SubscriptionProbe ()

        use recorder =
            subject
            |> probe.Watch
            |> Observable.chunkBy (ChunkWindowBoundaries boundaries)
            |> Recorder.Attach

        subject.OnNext 1
        // R3 completes the chunked sequence with Success whatever the result of the boundaries, so their failure is lost
        boundaries.OnCompleted (Result.Failure boom)

        assertChunks [| [| 1 |] |] recorder.Values "The completion of the boundaries must flush the buffer"
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "A failure of the boundaries must complete the chunked sequence successfully")
        Assert.IsEmpty (recorder.Errors, "A failure of the boundaries must not be reported as an error either")
        // The subject never completes by itself, so this disposal proves the unsubscription
        Assert.AreEqual (1, probe.Disposed, "The completed chunked sequence must unsubscribe from the source")

    [<TestMethod>]
    member _.``disposing a chunkBy ChunkWindowBoundaries subscription unsubscribes from the source and the boundaries`` () =
        use subject = new Subject<int> ()
        use boundaries = new Subject<int> ()
        let sourceProbe = SubscriptionProbe ()
        let boundariesProbe = SubscriptionProbe ()

        use recorder =
            subject
            |> sourceProbe.Watch
            |> Observable.chunkBy (ChunkWindowBoundaries (boundariesProbe.Watch boundaries))
            |> Recorder.Attach

        Assert.AreEqual (1, sourceProbe.Active, "Subscribing must subscribe to the source")
        Assert.AreEqual (1, boundariesProbe.Active, "Subscribing must subscribe to the boundaries")
        subject.OnNext 1
        recorder.Dispose ()

        // Neither subject completes by itself, so these disposals prove the unsubscriptions
        Assert.AreEqual (0, sourceProbe.Active, "Disposing must unsubscribe from the source")
        Assert.AreEqual (0, boundariesProbe.Active, "Disposing must unsubscribe from the boundaries")

    // chunkByBoundaries

    [<TestMethod>]
    member _.``chunkByBoundaries chunks on every tick of boundaries of another element type`` () =
        use subject = new Subject<int> ()
        use ticks = new Subject<unit> ()
        use recorder =
            subject
            |> Observable.chunkByBoundaries ticks
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        ticks.OnNext ()
        ticks.OnNext ()
        subject.OnNext 3
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |]; [||]; [| 3 |] |] recorder "Every tick and the completion must emit the buffer"

    [<TestMethod>]
    member _.``chunkByBoundaries chunks on the ticks of Observable.Interval driven by fake time`` () =
        let time = FakeTimeProvider ()
        use subject = new Subject<int> ()

        use recorder =
            subject
            |> Observable.chunkByBoundaries (Observable.Interval (TimeSpan.FromSeconds 1., time))
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        // The periodic timer of Interval fires synchronously inside Advance
        time.Advance (TimeSpan.FromSeconds 1.)
        assertChunks [| [| 1; 2 |] |] recorder.Values "The first tick must emit the elements buffered before it"
        time.Advance (TimeSpan.FromSeconds 1.)
        subject.OnNext 3
        time.Advance (TimeSpan.FromSeconds 1.)
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |]; [||]; [| 3 |] |] recorder "Every tick must emit a chunk, an empty one when nothing is buffered"

    [<TestMethod>]
    member _.``chunkByBoundaries forwards an error of string boundaries and completes successfully when they fail`` () =
        let boom : exn = InvalidOperationException "boom"
        use subject = new Subject<int> ()
        use boundaries = new Subject<string> ()
        let probe = SubscriptionProbe ()
        use recorder =
            subject
            |> probe.Watch
            |> Observable.chunkByBoundaries boundaries
            |> Recorder.Attach

        subject.OnNext 1
        boundaries.OnErrorResume boom
        boundaries.OnNext "close"
        subject.OnNext 2
        // R3 completes the chunked sequence with Success whatever the result of the boundaries
        boundaries.OnCompleted (Result.Failure (TimeoutException "the boundaries failed"))

        assertChunks [| [| 1 |]; [| 2 |] |] recorder.Values "The tick and the completion of the boundaries must emit the buffer"
        let error =
            Assert.ContainsSingle (recorder.Errors, "Only the resumed error of the boundaries may be forwarded")
        Assert.AreSame (boom, error, "The original error of the boundaries must be forwarded")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "A failure of the boundaries must complete the chunked sequence successfully")
        // The subject never completes by itself, so this disposal proves the unsubscription
        Assert.AreEqual (1, probe.Disposed, "The completed chunked sequence must unsubscribe from the source")

    // ChunkConfiguration helpers

    [<TestMethod>]
    member _.``ChunkConfiguration TimeSpan and TimeSpanCount keep their arguments and capture the default time provider`` () =
        let windowTime = TimeSpan.FromSeconds 3.
        // Only read: the default time provider is process-wide and this test runs in parallel with the others. It is the
        // system provider here, so only the test that replaces it, run apart from the parallel ones, tells the two apart
        let defaultTimeProvider = ObservableSystem.DefaultTimeProvider

        match (ChunkConfiguration.TimeSpan windowTime : ChunkConfiguration<int>) with
        | ChunkTimeSpan (actualWindowTime, timeProvider) ->
            Assert.AreEqual (windowTime, actualWindowTime, "TimeSpan must keep the window time")
            Assert.AreSame (defaultTimeProvider, timeProvider, "TimeSpan must capture the default time provider")
        | other -> Assert.Fail $"TimeSpan must build ChunkTimeSpan, not %A{other}"

        match (ChunkConfiguration.TimeSpanCount windowTime 4 : ChunkConfiguration<int>) with
        | ChunkTimeSpanCount (actualWindowTime, windowLength, timeProvider) ->
            Assert.AreEqual (windowTime, actualWindowTime, "TimeSpanCount must keep the window time")
            Assert.AreEqual (4, windowLength, "TimeSpanCount must keep the window length")
            Assert.AreSame (defaultTimeProvider, timeProvider, "TimeSpanCount must capture the default time provider")
        | other -> Assert.Fail $"TimeSpanCount must build ChunkTimeSpanCount, not %A{other}"

    [<TestMethod>]
    member _.``ChunkConfiguration Milliseconds and MillisecondsCount keep their arguments and capture the default time provider`` () =
        // Only read: the default time provider is process-wide and this test runs in parallel with the others. It is the
        // system provider here, so only the test that replaces it, run apart from the parallel ones, tells the two apart
        let defaultTimeProvider = ObservableSystem.DefaultTimeProvider

        match (ChunkConfiguration.Milliseconds 1500 : ChunkConfiguration<int>) with
        | ChunkMilliseconds (windowTime, timeProvider) ->
            Assert.AreEqual (1500, windowTime, "Milliseconds must keep the window time")
            Assert.AreSame (defaultTimeProvider, timeProvider, "Milliseconds must capture the default time provider")
        | other -> Assert.Fail $"Milliseconds must build ChunkMilliseconds, not %A{other}"

        match (ChunkConfiguration.MillisecondsCount 1500 4 : ChunkConfiguration<int>) with
        | ChunkMillisecondsCount (windowTime, windowLength, timeProvider) ->
            Assert.AreEqual (1500, windowTime, "MillisecondsCount must keep the window time")
            Assert.AreEqual (4, windowLength, "MillisecondsCount must keep the window length")
            Assert.AreSame (defaultTimeProvider, timeProvider, "MillisecondsCount must capture the default time provider")
        | other -> Assert.Fail $"MillisecondsCount must build ChunkMillisecondsCount, not %A{other}"

    [<TestMethod; DoNotParallelize>]
    member _.``ChunkConfiguration time helpers capture a replaced default time provider when called and chunk by its timers`` () =
        let time = FakeTimeProvider ()
        let windowMilliseconds = 3000
        let windowTime = TimeSpan.FromMilliseconds (float windowMilliseconds)
        // The system provider is the initial default, so only a replaced default tells helpers that read the default apart
        // from helpers that use TimeProvider.System directly. The default is process-wide: DoNotParallelize makes MSTest run
        // this test after the parallel ones, so none of them can see the fake, and the finally restores the previous default
        // for the tests that run after this one, even when a helper throws
        let previous = ObservableSystem.DefaultTimeProvider
        ObservableSystem.DefaultTimeProvider <- time

        let configurations : struct (string * ChunkConfiguration<int>) array =
            try
                [|
                    struct ("TimeSpan", ChunkConfiguration.TimeSpan windowTime)
                    struct ("TimeSpanCount", ChunkConfiguration.TimeSpanCount windowTime 2)
                    struct ("Milliseconds", ChunkConfiguration.Milliseconds windowMilliseconds)
                    struct ("MillisecondsCount", ChunkConfiguration.MillisecondsCount windowMilliseconds 2)
                |]
            finally
                ObservableSystem.DefaultTimeProvider <- previous

        for struct (name, configuration) in configurations do
            match configuration with
            | ChunkTimeSpan (_, timeProvider)
            | ChunkTimeSpanCount (_, _, timeProvider)
            | ChunkMilliseconds (_, timeProvider)
            | ChunkMillisecondsCount (_, _, timeProvider) ->
                Assert.AreSame<TimeProvider>(time, timeProvider, $"%s{name} must capture the default time provider of the moment it is called")
            | other -> Assert.Fail $"%s{name} must build a time based configuration, not %A{other}"

            // The previous default is back and a single element stays below the count of 2, so only a timer of the captured
            // fake can emit this chunk; FakeTimeProvider fires the due timer synchronously inside Advance
            use subject = new Subject<int> ()
            use recorder =
                subject
                |> Observable.chunkBy configuration
                |> Recorder.Attach
            subject.OnNext 1
            time.Advance windowTime
            assertChunks [| [| 1 |] |] recorder.Values $"%s{name} must emit the chunk when the fake time reaches the end of the window"

    [<TestMethod>]
    member _.``ChunkConfiguration time helpers chunk a synchronous source by count and on completion before any system timer fires`` () =
        // The helpers capture the default time provider, the system one here. The source is synchronous, so completion
        // flushes long before a one-hour timer could fire, and R3 disposes the timer when the chunked sequence completes:
        // never use a hot source here
        let hour = TimeSpan.FromHours 1.
        let millisecondsPerHour = 3_600_000

        let cases = [|
            struct ("TimeSpanCount", ChunkConfiguration.TimeSpanCount hour 2, [| [| 1; 2 |]; [| 3; 4 |]; [| 5 |] |])
            struct ("TimeSpan", ChunkConfiguration.TimeSpan hour, [| [| 1; 2; 3; 4; 5 |] |])
            struct ("MillisecondsCount", ChunkConfiguration.MillisecondsCount millisecondsPerHour 2, [| [| 1; 2 |]; [| 3; 4 |]; [| 5 |] |])
            struct ("Milliseconds", ChunkConfiguration.Milliseconds millisecondsPerHour, [| [| 1; 2; 3; 4; 5 |] |])
        |]

        for struct (name, configuration, expected) in cases do
            use recorder =
                Sources.values [| 1..5 |]
                |> Observable.chunkBy configuration
                |> Recorder.Attach
            assertChunks expected recorder.Values $"%s{name} must chunk by count and flush the rest on completion"
            Assert.IsEmpty (recorder.Errors, $"%s{name} must not report an error")
            Assert.IsTrue (recorder.IsCompletedSuccessfully, $"%s{name} must complete with the source")

    [<TestMethod>]
    member _.``ChunkConfiguration AsyncWindow builds ChunkAsyncWindow with ConfigureAwait enabled`` () =
        match ChunkConfiguration.AsyncWindow (fun (_ : int) -> async.Zero ()) with
        | ChunkAsyncWindow (_, configureAwait) ->
            Assert.IsTrue (configureAwait, "AsyncWindow must capture the synchronization context, as R3 does by default")
        | other -> Assert.Fail $"AsyncWindow must build ChunkAsyncWindow, not %A{other}"

    [<TestMethod>]
    member _.``ChunkConfiguration AsyncWindow passes the window token to the Async and closes the window when the Async finishes`` () : Task = task {
        use subject = new Subject<int> ()
        // InvokeAsync records the token of the Async computation and waits for the gate of its element
        let window = GatedSelector<int, unit> ignore
        use recorder =
            subject
            |> Observable.chunkBy (ChunkConfiguration.AsyncWindow window.InvokeAsync)
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        CollectionAssert.AreEqual ([| 1 |], window.Started, "Only the first element may start the Async window")
        let token = Assert.ContainsSingle (window.Tokens, "The Async window must start once")
        Assert.IsFalse (token.IsCancellationRequested, "The window token must stay active while the source runs")

        window.Release 1
        do! recorder.WaitForValuesAsync (testContext.CancellationToken, 1)
        subject.OnNext 3
        CollectionAssert.AreEqual ([| 1; 3 |], window.Started, "The first element after the Async finished must start a new window")

        window.Release 3
        do! recorder.WaitForValuesAsync (testContext.CancellationToken, 2)
        subject.OnCompleted Result.Success

        assertCompletedWith [| [| 1; 2 |]; [| 3 |] |] recorder "Every Async window must emit the elements that arrived while it ran"
        // R3 cancels its window token when the chunked sequence completes, so this proves the Async ran with that token
        Assert.IsTrue (token.IsCancellationRequested, "The Async must have run with the window token of R3")
    }

    [<TestMethod>]
    member _.``ChunkConfiguration AsyncWindow resumes an Async window failure and still emits the chunk`` () =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            Sources.values [| 1; 2 |]
            |> Observable.chunkBy (ChunkConfiguration.AsyncWindow (fun (_ : int) -> async { return raise boom }))
            |> Recorder.Attach

        // StartImmediateAsTask runs the Async synchronously and faults its task with the original exception,
        // which R3 reports through OnErrorResume before it emits the chunk of the window
        assertEveryWindowFailed boom [| 1; 2 |] recorder
