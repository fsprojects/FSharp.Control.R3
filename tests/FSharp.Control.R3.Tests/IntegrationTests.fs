namespace FSharp.Control.R3.Tests

open System
open System.Linq
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Time.Testing
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open Swensen.Unquote
open FSharp.Control.R3
open FSharp.Control.R3.Observable.Builders
open FSharp.Control.R3.Tests.TestHelpers

// The scenarios combine both flavours, whose modules must never be opened in the same file, so they are reached through abbreviations.
// The abbreviations that TestHelpers declares are local to that file, so this file declares its own
module TaskObservable = FSharp.Control.R3.Task.Observable
module AsyncObservable = FSharp.Control.R3.Async.Observable

// The type abbreviations live in a module named after this file rather than directly in the namespace: a namespace-level type
// is shared by every file of the assembly, so another test file that declared the same names would fail to compile with FS0249.
// The module is opened right below rather than AutoOpen, which would also open it in every later file of the namespace
module internal IntegrationTestsSupport =

    /// The overloaded functions of the Task flavour.
    type TaskConversions = FSharp.Control.R3.Task.Extensions.Observable

    /// The overloaded functions of the Async flavour.
    type AsyncConversions = FSharp.Control.R3.Async.Extensions.Observable

    /// The asynchronous factory of the flavour that emits the result of the gated selector for the value.
    let factoryWith (flavour : string) (selector : GatedSelector<'T, 'R>) (value : 'T) : Observable<'R> =
        match flavour with
        | "Task" -> TaskConversions.ofTask (fun cancellationToken -> ValueTask<'R>(selector.InvokeTask cancellationToken value))
        | "Async" -> AsyncObservable.ofAsync (selector.InvokeAsync value)
        | _ -> Flavour.unknown flavour

    /// <summary>
    /// Starts <see cref="M:FSharp.Control.R3.Task.Observable.toList``1(System.Threading.CancellationToken,R3.Observable{``0})"/>
    /// or <see cref="M:FSharp.Control.R3.Async.Observable.toList``1(R3.Observable{``0})"/>, as the flavour says;
    /// the Async flavour runs with the token as the token of its computation.
    /// </summary>
    let toListWith (cancellationToken : CancellationToken) (flavour : string) (source : Observable<'T>) : Task<'T list> =
        match flavour with
        | "Task" -> TaskObservable.toList cancellationToken source
        | "Async" ->
            AsyncObservable.toList source
            |> AsyncTest.start cancellationToken
        | _ -> Flavour.unknown flavour

    /// The groups of the lookup as pairs of key and elements, ordered by key, so that two lookups compare structurally.
    let groupsOf (lookup : ILookup<'Key, 'Element>) =
        lookup
        |> Seq.map (fun group -> struct (group.Key, Seq.toArray group))
        |> Seq.sortBy (fun struct (key, _) -> key)
        |> Seq.toArray

open IntegrationTestsSupport

[<TestClass; IntegrationTestCategory>]
type IntegrationTests (testContext : TestContext) =

    // Keeps the elements that satisfy the predicate, drops the repeated ones, pairs every element with its index and chunks the pairs by two
    let distinctIndexedPairs (predicate : int -> bool) (source : Observable<int>) =
        source
        |> Observable.filter predicate
        |> Observable.distinct
        |> Observable.mapi (fun index value -> struct (index, value))
        |> Observable.chunkBySize 2

    // Merges both sources, projects their elements and falls back to -1 when a source fails with an InvalidOperationException
    let catchProtected (project : int -> int) (handled : ResizeArray<exn>) (left : Observable<int>) (right : Observable<int>) =
        Observable.merge (left, right)
        |> Observable.map project
        |> Observable.catch (fun (error : InvalidOperationException) ->
            handled.Add error
            Observable.singleton (-1)
        )

    // The sum of a chunk, or None for the empty chunk that a boundary emits when nothing is buffered
    let sumOfNonEmpty (chunk : int array) =
        if Array.isEmpty chunk then
            ValueNone
        else
            ValueSome (Array.sum chunk)

    [<TestMethod>]
    member _.``filter, distinct, mapi and chunkBySize compose over a hot subject`` () =
        use subject = new Subject<int> ()
        // A subject is hot: what it receives before the pipeline subscribes is lost
        subject.OnNext 9

        use recorder =
            subject
            |> distinctIndexedPairs (fun x -> x > 0)
            |> Recorder.Attach

        subject.OnNext 3
        subject.OnNext (-1)
        subject.OnNext 3
        subject.OnNext 5
        // Every operator delivers synchronously, so the first chunk is emitted as soon as its second element arrives
        let firstChunk = [| [| struct (0, 3); struct (1, 5) |] |]
        test <@ recorder.Values = firstChunk @>
        subject.OnNext 7
        Assert.IsTrue (recorder.Completion.IsNone, "The pipeline must stay open while the subject is open")

        subject.OnCompleted Result.Success

        // Completion flushes the partial chunk, and the index counts only the elements that passed filter and distinct
        let chunks = [| [| struct (0, 3); struct (1, 5) |]; [| struct (2, 7) |] |]
        test <@ recorder.Values = chunks @>
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "The pipeline must complete successfully with the subject")

    [<TestMethod>]
    member _.``a failure of the hot subject flushes the partial chunk of the pipeline before failing it`` () =
        use subject = new Subject<int> ()
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            subject
            |> distinctIndexedPairs (fun x -> x > 0)
            |> Recorder.Attach

        subject.OnNext 3
        subject.OnNext 5
        subject.OnNext 7
        subject.OnCompleted (Result.Failure boom)

        // R3 chunking flushes the partial chunk on a failed completion too, before it forwards the failure
        let chunks = [| [| struct (0, 3); struct (1, 5) |]; [| struct (2, 7) |] |]
        test <@ recorder.Values = chunks @>
        assertFailedWith boom recorder "The pipeline must fail with the exception of the subject"

    [<TestMethod>]
    member _.``an error resumed by the filter passes through distinct, mapi and chunkBySize without taking an index`` () =
        use subject = new Subject<int> ()
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            subject
            |> distinctIndexedPairs (fun x -> if x = 4 then raise boom else x > 0)
            |> Recorder.Attach

        subject.OnNext 3
        // R3 reports the exception of the predicate through OnErrorResume, which every following operator forwards unchanged
        subject.OnNext 4
        subject.OnNext 5
        subject.OnNext 7
        subject.OnCompleted Result.Success

        let chunks = [| [| struct (0, 3); struct (1, 5) |]; [| struct (2, 7) |] |]
        test <@ recorder.Values = chunks @>
        let resumed = Assert.ContainsSingle (recorder.Errors, "Exactly one error must be resumed")
        Assert.AreSame (boom, resumed, "The resumed error must be the exception of the predicate")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "A resumed error must not stop the pipeline")

    [<TestMethod>]
    member _.``disposing a multi-operator pipeline unsubscribes it from the hot subject`` () =
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()

        use recorder =
            subject
            |> probe.Watch
            |> distinctIndexedPairs (fun x -> x > 0)
            |> Recorder.Attach

        subject.OnNext 3
        Assert.AreEqual (1, probe.Active, "The pipeline must hold one subscription to the subject")

        recorder.Dispose ()

        // Every operator disposes its upstream subscription synchronously, down to the subject, which never completes by itself
        Assert.AreEqual (1, probe.Subscribed, "The pipeline must have subscribed to the subject once")
        Assert.AreEqual (1, probe.Disposed, "Disposing the pipeline must unsubscribe it from the subject")

    [<TestMethod>]
    member _.``merged sources feed a catch-protected pipeline that falls back and unsubscribes from the open source`` () =
        use left = new Subject<int> ()
        use right = new Subject<int> ()
        let rightProbe = SubscriptionProbe ()
        let handled = ResizeArray<exn>()
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            catchProtected (fun x -> x * 10) handled left (rightProbe.Watch right)
            |> Recorder.Attach

        left.OnNext 1
        right.OnNext 2
        left.OnCompleted (Result.Failure boom)

        // Merge fails as soon as one source fails, and catch subscribes to the fallback, which emits synchronously
        CollectionAssert.AreEqual ([| 10; 20; -1 |], recorder.Values, "The values of both sources must be followed by the fallback value")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "The fallback must complete the pipeline successfully")
        let handledError = Assert.ContainsSingle (handled, "The handler must be called once")
        Assert.AreSame (boom, handledError, "The handler must receive the exception of the failed source")
        // Merge only forwards the failure; the downstream observers dispose the merged subscription when they complete.
        // The right subject is still open, so this disposal proves the unsubscription
        Assert.AreEqual (1, rightProbe.Disposed, "The failure must unsubscribe the pipeline from the source that is still open")

    [<TestMethod>]
    member _.``a failure the catch handler does not handle fails the merged pipeline and unsubscribes from the open source`` () =
        use left = new Subject<int> ()
        use right = new Subject<int> ()
        let rightProbe = SubscriptionProbe ()
        let handled = ResizeArray<exn>()
        let unexpected : exn = ArgumentException "unexpected"

        use recorder =
            catchProtected (fun x -> x * 10) handled left (rightProbe.Watch right)
            |> Recorder.Attach

        left.OnNext 1
        left.OnCompleted (Result.Failure unexpected)

        // R3 Catch handles only failures of the exception type of the handler and forwards the others unchanged
        CollectionAssert.AreEqual ([| 10 |], recorder.Values, "Only the value emitted before the failure must arrive")
        assertFailedWith unexpected recorder "The pipeline must fail with the unhandled exception"
        Assert.IsEmpty (handled, "The handler must not be called for an exception of another type")
        Assert.AreEqual (1, rightProbe.Disposed, "The failure must unsubscribe the pipeline from the source that is still open")

    [<TestMethod>]
    member _.``errors resumed by a merged source or by the projection pass through catch without falling back`` () =
        use left = new Subject<int> ()
        use right = new Subject<int> ()
        let handled = ResizeArray<exn>()
        // Both errors have the exception type of the handler, to show that only a terminal failure reaches it
        let sourceError : exn = InvalidOperationException "source"
        let projectionError : exn = InvalidOperationException "projection"

        use recorder =
            catchProtected (fun x -> if x = 2 then raise projectionError else x * 10) handled left right
            |> Recorder.Attach

        left.OnNext 1
        left.OnErrorResume sourceError
        // R3 reports the exception of the projection through OnErrorResume as well
        right.OnNext 2
        right.OnNext 3
        left.OnCompleted Result.Success
        Assert.IsTrue (recorder.Completion.IsNone, "Merge must wait for the other source to complete")
        right.OnCompleted Result.Success

        // Catch forwards OnErrorResume unchanged and the sequence goes on after each resumed error
        CollectionAssert.AreEqual ([| 10; 30 |], recorder.Values, "The values around the resumed errors must arrive")
        CollectionAssert.AreEqual ([| sourceError; projectionError |], recorder.Errors, "Both errors must be resumed in order")
        Assert.IsEmpty (handled, "A resumed error must not reach the handler")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "The pipeline must complete successfully when both sources complete")

    [<TestMethod>]
    member _.``Async and Task terminal functions agree on the same pipeline`` () : Task = task {
        let ct = testContext.CancellationToken
        // The numbers from 1 to 20 without the multiples of 3, doubled; the synchronous source makes every run deterministic
        let pipeline =
            Sources.values [| 1..20 |]
            |> Observable.filter (fun x -> x % 3 <> 0)
            |> Observable.map (fun x -> x * 2)

        let elements = [| 2; 4; 8; 10; 14; 16; 20; 22; 26; 28; 32; 34; 38; 40 |]

        let! taskLength = TaskObservable.length ct pipeline
        let! asyncLength = AsyncObservable.length pipeline
        Assert.AreEqual (elements.Length, taskLength, "The Task length must count every element")
        Assert.AreEqual (taskLength, asyncLength, "The Async length must agree with the Task length")

        let! taskSum = TaskObservable.aggregate ct 0 (+) pipeline
        let! asyncSum = AsyncObservable.aggregate 0 (+) pipeline
        Assert.AreEqual (Array.sum elements, taskSum, "The Task aggregate must fold every element")
        Assert.AreEqual (taskSum, asyncSum, "The Async aggregate must agree with the Task aggregate")

        let! taskAllEven = TaskObservable.all ct (fun x -> x % 2 = 0) pipeline
        let! asyncAllEven = AsyncObservable.all (fun x -> x % 2 = 0) pipeline
        Assert.IsTrue (taskAllEven, "The Task all must hold for a predicate that every element satisfies")
        Assert.AreEqual (taskAllEven, asyncAllEven, "The Async all must agree with the Task all")

        let! taskAllBelow30 = TaskObservable.all ct (fun x -> x < 30) pipeline
        let! asyncAllBelow30 = AsyncObservable.all (fun x -> x < 30) pipeline
        Assert.IsFalse (taskAllBelow30, "The Task all must fail for a predicate that some element does not satisfy")
        Assert.AreEqual (taskAllBelow30, asyncAllBelow30, "The Async all must agree with the Task all on a failing predicate")

        let! taskExists = TaskObservable.existsAsync ct pipeline
        let! asyncExists = AsyncObservable.existsAsync pipeline
        Assert.IsTrue (taskExists, "The Task existsAsync must find an element")
        Assert.AreEqual (taskExists, asyncExists, "The Async existsAsync must agree with the Task existsAsync")

        let! taskFirst = TaskObservable.firstAsync ct pipeline
        let! asyncFirst = AsyncObservable.firstAsync pipeline
        Assert.AreEqual (elements[0], taskFirst, "The Task firstAsync must return the first element")
        Assert.AreEqual (taskFirst, asyncFirst, "The Async firstAsync must agree with the Task firstAsync")

        let taskSeen = ResizeArray<int>()
        let asyncSeen = ResizeArray<int>()
        do! TaskObservable.iter ct taskSeen.Add pipeline
        do! AsyncObservable.iter asyncSeen.Add pipeline
        CollectionAssert.AreEqual (elements, taskSeen.ToArray (), "The Task iter must visit every element in order")
        CollectionAssert.AreEqual (taskSeen.ToArray (), asyncSeen.ToArray (), "The Async iter must visit the same elements")

        let! taskArray = TaskObservable.toArray ct pipeline
        let! asyncArray = AsyncObservable.toArray pipeline
        CollectionAssert.AreEqual (elements, taskArray, "The Task toArray must collect every element in order")
        CollectionAssert.AreEqual (taskArray, asyncArray, "The Async toArray must agree with the Task toArray")

        let! taskList = TaskObservable.toList ct pipeline
        let! asyncList = AsyncObservable.toList pipeline
        Assert.AreEqual<int list>(List.ofArray elements, taskList, "The Task toList must collect every element in order")
        Assert.AreEqual<int list>(taskList, asyncList, "The Async toList must agree with the Task toList")

        // Grouped by the remainder of the division by 8; the elements of a group keep their order of arrival
        let! taskLookup = TaskConversions.toLookup (pipeline, (fun x -> x % 8), ct)
        let! asyncLookup = AsyncConversions.toLookup (pipeline, (fun x -> x % 8))

        let expectedGroups = [|
            struct (0, [| 8; 16; 32; 40 |])
            struct (2, [| 2; 10; 26; 34 |])
            struct (4, [| 4; 20; 28 |])
            struct (6, [| 14; 22; 38 |])
        |]

        let taskGroups = groupsOf taskLookup
        let asyncGroups = groupsOf asyncLookup
        test <@ taskGroups = expectedGroups @>
        test <@ asyncGroups = taskGroups @>
    }

    [<TestMethod>]
    member _.``Async and Task terminal functions agree on a pipeline that filters out every element`` () : Task = task {
        let ct = testContext.CancellationToken

        let pipeline =
            Sources.values [| 1..20 |]
            |> Observable.filter (fun x -> x > 20)
            |> Observable.map (fun x -> x * 2)

        let! taskLength = TaskObservable.length ct pipeline
        let! asyncLength = AsyncObservable.length pipeline
        Assert.AreEqual (0, taskLength, "The Task length of an empty pipeline must be 0")
        Assert.AreEqual (0, asyncLength, "The Async length of an empty pipeline must be 0")

        // R3 returns the seed when there is nothing to fold
        let! taskSum = TaskObservable.aggregate ct 7 (+) pipeline
        let! asyncSum = AsyncObservable.aggregate 7 (+) pipeline
        Assert.AreEqual (7, taskSum, "The Task aggregate of an empty pipeline must return the seed")
        Assert.AreEqual (7, asyncSum, "The Async aggregate of an empty pipeline must return the seed")

        let! taskAll = TaskObservable.all ct (fun _ -> false) pipeline
        let! asyncAll = AsyncObservable.all (fun _ -> false) pipeline
        Assert.IsTrue (taskAll, "The Task all of an empty pipeline must hold vacuously")
        Assert.IsTrue (asyncAll, "The Async all of an empty pipeline must hold vacuously")

        let! taskExists = TaskObservable.existsAsync ct pipeline
        let! asyncExists = AsyncObservable.existsAsync pipeline
        Assert.IsFalse (taskExists, "The Task existsAsync of an empty pipeline must be false")
        Assert.IsFalse (asyncExists, "The Async existsAsync of an empty pipeline must be false")

        // R3 FirstAsync fails with "Sequence contains no elements.", which both flavours surface unwrapped
        let! taskError =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> TaskObservable.firstAsync ct pipeline :> Task),
                "The Task firstAsync of an empty pipeline must fail"
            )

        let! asyncError =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> AsyncTest.start ct (AsyncObservable.firstAsync pipeline) :> Task),
                "The Async firstAsync of an empty pipeline must fail"
            )

        Assert.AreEqual (taskError.Message, asyncError.Message, "Both flavours must fail with the same message")

        let taskSeen = ResizeArray<int>()
        let asyncSeen = ResizeArray<int>()
        do! TaskObservable.iter ct taskSeen.Add pipeline
        do! AsyncObservable.iter asyncSeen.Add pipeline
        Assert.IsEmpty (taskSeen, "The Task iter must not call the action for an empty pipeline")
        Assert.IsEmpty (asyncSeen, "The Async iter must not call the action for an empty pipeline")

        let! taskArray = TaskObservable.toArray ct pipeline
        let! asyncArray = AsyncObservable.toArray pipeline
        Assert.IsEmpty (taskArray, "The Task toArray of an empty pipeline must be empty")
        Assert.IsEmpty (asyncArray, "The Async toArray of an empty pipeline must be empty")

        let! taskList = TaskObservable.toList ct pipeline
        let! asyncList = AsyncObservable.toList pipeline
        Assert.IsEmpty (taskList, "The Task toList of an empty pipeline must be empty")
        Assert.IsEmpty (asyncList, "The Async toList of an empty pipeline must be empty")

        let! taskLookup = TaskConversions.toLookup (pipeline, (fun x -> x % 8), ct)
        let! asyncLookup = AsyncConversions.toLookup (pipeline, (fun x -> x % 8))
        Assert.IsEmpty (taskLookup, "The Task toLookup of an empty pipeline must have no group")
        Assert.IsEmpty (asyncLookup, "The Async toLookup of an empty pipeline must have no group")
    }

    [<TestMethod; DataRow "terminal failure"; DataRow "resumed error">]
    member _.``Async and Task terminal functions fail with the same exception when the pipeline fails`` (failure : string) : Task = task {
        let ct = testContext.CancellationToken
        let boom : exn = InvalidOperationException "boom"

        let source =
            match failure with
            | "terminal failure" -> Sources.failingAfter [| 1; 2; 3 |] boom
            // R3 reports the exception of the projection through OnErrorResume, which a terminal function treats as a failure
            | "resumed error" ->
                Sources.values [| 1; 2; 3; 4 |]
                |> Observable.map (fun x -> if x = 4 then raise boom else x)
            | other -> invalidArg (nameof failure) $"Unknown failure %s{other}"

        let pipeline = source |> Observable.map (fun x -> x * 10)

        // The functions that decide on the first element return and unsubscribe before the failure arrives
        let! taskExists = TaskObservable.existsAsync ct pipeline
        let! asyncExists = AsyncObservable.existsAsync pipeline
        let! taskFirst = TaskObservable.firstAsync ct pipeline
        let! asyncFirst = AsyncObservable.firstAsync pipeline
        Assert.IsTrue (taskExists, "The Task existsAsync must decide on the first element")
        Assert.IsTrue (asyncExists, "The Async existsAsync must decide on the first element")
        Assert.AreEqual (10, taskFirst, "The Task firstAsync must return the first element")
        Assert.AreEqual (10, asyncFirst, "The Async firstAsync must return the first element")

        // The others wait for the end of the sequence and fail with the very exception of the pipeline in both flavours;
        // all waits as well, because every element of the pipeline satisfies its predicate
        let waitingForTheEnd =
            Terminals.names
            |> Array.except [| "existsAsync"; "firstAsync" |]

        for name in waitingForTheEnd do
            for flavour in [| "Task"; "Async" |] do
                let! error =
                    Assert.ThrowsAsync<InvalidOperationException>(
                        (fun () -> Terminals.runWith ct flavour name pipeline),
                        $"%s{flavour} %s{name} must fail"
                    )

                Assert.AreSame (boom, error, $"%s{flavour} %s{name} must fail with the exception of the pipeline")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``a mapAsync selector failure faults the terminal function and tears down the pipeline`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let boom : exn = InvalidOperationException "boom"
        let selector = GatedSelector<int, int>(fun x -> if x = 2 then raise boom else x * 10)
        // Released up front, so both invocations finish as soon as they start
        selector.Release 1
        selector.Release 2

        let elements =
            subject
            |> probe.Watch
            |> Flavour.mapWith flavour ProcessingOptions.Default selector
            |> Terminals.runWith ct flavour "toArray"

        subject.OnNext 1
        subject.OnNext 2

        // mapAsync reports the selector failure through OnErrorResume, which the terminal function turns into its own failure
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> elements), "The terminal function must fail with the selector failure")

        Assert.AreSame (boom, error, "Both flavours must surface the very exception of the selector")
        // R3 completes the task of the terminal operator before it disposes the subscription, so the test waits for the disposal
        do! probe.WaitForDisposedAsync (ct, 1)
        Assert.AreEqual (1, probe.Subscribed, "The pipeline must have subscribed to the subject once")
        Assert.AreEqual (1, probe.Disposed, "The failure must unsubscribe the pipeline from the subject")
        CollectionAssert.AreEqual ([| 1; 2 |], selector.Started, "The selector must have been invoked for both elements")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``cancelling a terminal function cancels the running mapAsync work and unsubscribes from the source`` (flavour : string) : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        use cancellation = CancellationTokenSource.CreateLinkedTokenSource testContext.CancellationToken

        // Parallel starts the selector inside OnNext, so an element that still reached the pipeline after the cancellation
        // would be in Started at once; the sequential worker would stay parked on the unreleased selector of element 1 instead
        let length =
            subject
            |> probe.Watch
            |> Flavour.mapWith flavour ProcessingOptions.Parallel selector
            |> Terminals.runWith cancellation.Token flavour "length"

        subject.OnNext 1
        do! selector.WaitForStartedAsync (testContext.CancellationToken, 1)
        Assert.IsFalse (length.IsCompleted, "The terminal function must wait while the selector runs")

        // R3 registers on the token before it subscribes; Cancel runs the registration inline, which disposes the subscription
        // chain, cancelling the selector token of mapAsync on its way, before it cancels the task
        cancellation.Cancel ()

        Assert.AreEqual (1, probe.Disposed, "Cancelling must unsubscribe the pipeline from the subject before Cancel returns")
        Assert.IsTrue (selector.Tokens[0].IsCancellationRequested, "Cancelling must cancel the token of the running selector")
        subject.OnNext 2
        CollectionAssert.AreEqual ([| 1 |], selector.Started, "No element may reach the selector after the cancellation")

        let! _ =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> length), "Awaiting a cancelled terminal function must throw")

        Assert.IsTrue (length.IsCanceled, "The task of the terminal function must be cancelled rather than faulted")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``time-chunked elements are aggregated asynchronously in window order`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        let time = FakeTimeProvider ()
        use subject = new Subject<int> ()
        // GatedSelector keys its gates with structural equality, so an equal array releases the aggregation of a chunk
        let sum = GatedSelector<int array, int> Array.sum

        use recorder =
            subject
            |> Observable.chunkBy (ChunkTimeSpan (TimeSpan.FromSeconds 2., time))
            |> Flavour.mapWith flavour ProcessingOptions.Default sum
            |> Recorder.Attach

        subject.OnNext 1
        time.Advance (TimeSpan.FromSeconds 1.)
        subject.OnNext 2
        // R3 starts the window at its first element, and that window has not elapsed yet
        Assert.IsEmpty (sum.Started, "No chunk may be aggregated before its window elapses")

        // FakeTimeProvider fires the window timer synchronously inside Advance
        time.Advance (TimeSpan.FromSeconds 1.)
        sum.Release [| 1; 2 |]
        do! recorder.WaitForValuesAsync (ct, 1)

        subject.OnNext 4
        // Completion flushes the open window, and the sequential mapAsync completes only after its pending aggregation
        subject.OnCompleted Result.Success
        sum.Release [| 4 |]
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 3; 4 |], recorder.Values, "The sums must arrive in window order")
        Assert.IsTrue (completion.IsSuccess, "The aggregation must complete successfully after the last window")
        test <@ sum.Started = [| [| 1; 2 |]; [| 4 |] |] @>
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``a source failure ends the time-chunked aggregation at once and cancels the pending aggregation`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        let time = FakeTimeProvider ()
        use subject = new Subject<int> ()
        let sum = GatedSelector<int array, int> Array.sum
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            subject
            |> Observable.chunkBy (ChunkTimeSpan (TimeSpan.FromSeconds 2., time))
            |> Flavour.mapWith flavour ProcessingOptions.Default sum
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        time.Advance (TimeSpan.FromSeconds 2.)
        do! sum.WaitForStartedAsync (ct, 1)
        subject.OnNext 3
        subject.OnCompleted (Result.Failure boom)

        // On a failure mapAsync cancels its pending work and publishes the failure synchronously, without waiting for that work
        assertFailedWith boom recorder "The aggregation must fail with the exception of the source at once"
        Assert.IsEmpty (recorder.Values, "The pending aggregation must not emit a sum")
        Assert.IsTrue (sum.Tokens[0].IsCancellationRequested, "The failure must cancel the token of the pending aggregation")
        // The sequential worker still waits for the first aggregation, so the chunk flushed by the failure never starts
        test <@ sum.Started = [| [| 1; 2 |] |] @>
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``asynchronous factories compose through bind synchronously`` (flavour : string) =
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        // Released up front, so every factory completes synchronously
        for value in 1..3 do
            selector.Release value

        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.bind (factoryWith flavour selector)
            |> Recorder.Attach

        // FromAsync emits a completed factory result inline, and SelectMany completes once the source and all inner sequences have completed
        CollectionAssert.AreEqual ([| 10; 20; 30 |], recorder.Values, "Every factory result must arrive in order without waiting")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "The merged sequence must complete synchronously")
        CollectionAssert.AreEqual ([| 1; 2; 3 |], selector.Started, "Every element must start one factory")

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``a failing asynchronous factory inside bind fails the merged sequence at once`` (flavour : string) =
        let boom : exn = InvalidOperationException "boom"
        let selector = GatedSelector<int, int>(fun x -> if x = 2 then raise boom else x * 10)

        for value in 1..3 do
            selector.Release value

        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.bind (factoryWith flavour selector)
            |> Recorder.Attach

        // FromAsync completes its sequence with the failure of the factory, and SelectMany fails as soon as an inner sequence fails
        CollectionAssert.AreEqual ([| 10 |], recorder.Values, "Only the result produced before the failure must arrive")
        assertFailedWith boom recorder "The merged sequence must fail with the exception of the factory"
        // The failed merge ignores the rest of the source, so no factory starts for the last element
        CollectionAssert.AreEqual ([| 1; 2 |], selector.Started, "No factory may start after the failure")

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``disposing a bind pipeline cancels the tokens of its pending asynchronous factories`` (flavour : string) =
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)

        use recorder =
            subject
            |> probe.Watch
            |> Observable.bind (factoryWith flavour selector)
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        // Both factories start synchronously and wait at their gates, which are never released
        CollectionAssert.AreEqual ([| 1; 2 |], selector.Started, "Every element must start one factory")
        Assert.IsFalse (selector.Tokens |> Array.exists _.IsCancellationRequested, "No factory token may be cancelled while subscribed")
        Assert.IsEmpty (recorder.Values, "No factory result may arrive while the factories wait at their gates")

        recorder.Dispose ()

        // SelectMany disposes its inner subscriptions, and FromAsync cancels the token of its factory when it is disposed
        Assert.IsTrue (selector.Tokens |> Array.forall _.IsCancellationRequested, "Disposing must cancel the token of every pending factory")
        Assert.AreEqual (1, probe.Disposed, "Disposing must unsubscribe the pipeline from the subject")

    [<TestMethod>]
    member _.``an rxquery feeds the Task and Async terminal functions with the same ordered elements`` () : Task = task {
        let ct = testContext.CancellationToken

        let query = rxquery {
            for x in Sources.values [| 1..8 |] do
                where (x % 2 = 0)
                select (x * 10)
        }

        let elements = TaskObservable.toArray ct query
        // Yield emits synchronously, like every operator of the query, so the Task terminal function completes before it returns
        Assert.IsTrue (elements.IsCompletedSuccessfully, "A query over a synchronous source must complete synchronously")
        let! taskElements = elements
        let! asyncElements = AsyncObservable.toArray query
        CollectionAssert.AreEqual ([| 20; 40; 60; 80 |], taskElements, "The query must keep the order of the source")
        CollectionAssert.AreEqual (taskElements, asyncElements, "The Async toArray must receive the same elements")

        let! taskLength = TaskObservable.length ct query
        let! asyncLength = AsyncObservable.length query
        Assert.AreEqual (4, taskLength, "The Task length must count the elements of the query")
        Assert.AreEqual (taskLength, asyncLength, "The Async length must agree with the Task length")
    }

    [<TestMethod>]
    member _.``an rxquery whose projection throws faults the Task and Async terminal functions with the same exception`` () : Task = task {
        let ct = testContext.CancellationToken
        let boom : exn = InvalidOperationException "boom"

        let query = rxquery {
            for x in Sources.values [| 1..4 |] do
                select (if x = 3 then raise boom else x)
        }

        // Select reports the exception of the projection through OnErrorResume, which a terminal function turns into its failure
        let! taskError =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> TaskObservable.toArray ct query :> Task),
                "The Task toArray must fail with the exception of the projection"
            )

        let! asyncError =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> AsyncTest.start ct (AsyncObservable.toArray query) :> Task),
                "The Async toArray must fail with the exception of the projection"
            )

        Assert.AreSame (boom, taskError, "The Task flavour must surface the very exception of the projection")
        Assert.AreSame (boom, asyncError, "The Async flavour must surface the very exception of the projection")
    }

    [<TestMethod>]
    member _.``chunkByBoundaries driven by a unit subject feeds choose with the sums of the non-empty chunks`` () =
        use source = new Subject<int> ()
        use trigger = new Subject<unit> ()
        let triggerProbe = SubscriptionProbe ()
        let boundaryError : exn = InvalidOperationException "boundary"

        use recorder =
            source
            |> Observable.chunkByBoundaries (triggerProbe.Watch trigger)
            |> Observable.choose sumOfNonEmpty
            |> Recorder.Attach

        source.OnNext 1
        source.OnNext 2
        trigger.OnNext ()
        // A boundary with nothing buffered emits an empty chunk, which choose drops
        trigger.OnNext ()
        // R3 forwards an error resumed by the boundaries downstream and goes on chunking
        trigger.OnErrorResume boundaryError
        source.OnNext 4
        source.OnNext 5
        CollectionAssert.AreEqual ([| 3 |], recorder.Values, "Only the boundary that closed buffered elements must produce a sum")

        source.OnCompleted Result.Success

        // Completion of the source flushes the buffer and unsubscribes from the boundaries, which never complete by themselves
        CollectionAssert.AreEqual ([| 3; 9 |], recorder.Values, "Completion must flush the buffered elements")
        let resumed = Assert.ContainsSingle (recorder.Errors, "Exactly one error must be resumed")
        Assert.AreSame (boundaryError, resumed, "The resumed error must be the error of the boundaries")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "The chosen sequence must complete successfully with the source")
        Assert.AreEqual (1, triggerProbe.Disposed, "Completion of the source must unsubscribe from the boundaries")

    [<TestMethod>]
    member _.``a failure of the boundary trigger flushes the buffer and completes the chosen sequence successfully`` () =
        use source = new Subject<int> ()
        use trigger = new Subject<unit> ()
        let sourceProbe = SubscriptionProbe ()
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            sourceProbe.Watch source
            |> Observable.chunkByBoundaries trigger
            |> Observable.choose sumOfNonEmpty
            |> Recorder.Attach

        source.OnNext 1
        source.OnNext 2
        trigger.OnCompleted (Result.Failure boom)

        // R3 ends the chunked sequence on any completion of the boundaries, a failed one included: it flushes and succeeds
        CollectionAssert.AreEqual ([| 3 |], recorder.Values, "The completion of the boundaries must flush the buffered elements")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "A failure of the boundaries must complete the chosen sequence successfully")
        Assert.IsEmpty (recorder.Errors, "The failure of the boundaries must not be resumed")
        Assert.AreEqual (1, sourceProbe.Disposed, "The completion of the boundaries must unsubscribe from the source")

    [<TestMethod>]
    member _.``a failure of the source flushes the buffer before failing the chosen sequence`` () =
        use source = new Subject<int> ()
        use trigger = new Subject<unit> ()
        let triggerProbe = SubscriptionProbe ()
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            source
            |> Observable.chunkByBoundaries (triggerProbe.Watch trigger)
            |> Observable.choose sumOfNonEmpty
            |> Recorder.Attach

        source.OnNext 1
        source.OnNext 2
        source.OnCompleted (Result.Failure boom)

        // R3 flushes the buffer on any completion of the source before it forwards the result
        CollectionAssert.AreEqual ([| 3 |], recorder.Values, "The failure must flush the buffered elements first")
        assertFailedWith boom recorder "The chosen sequence must fail with the exception of the source"
        Assert.AreEqual (1, triggerProbe.Disposed, "The failure of the source must unsubscribe from the boundaries")

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``ofSeq feeds a pipeline of operators, chunking, mapAsync and a terminal function`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        let enumerations = ref 0

        let numbers = seq {
            enumerations.Value <- enumerations.Value + 1
            yield! [ 1..10 ]
        }

        let sum = GatedSelector<int array, int> Array.sum
        // Released up front, so every aggregation finishes as soon as it starts
        for chunk in [ [| 10; 30 |]; [| 50; 70 |]; [| 90 |] ] do
            sum.Release chunk

        let! sums =
            numbers
            |> Observable.ofSeq
            |> Observable.filter (fun x -> x % 2 = 1)
            |> Observable.map (fun x -> x * 10)
            |> Observable.chunkBySize 2
            |> Flavour.mapWith flavour ProcessingOptions.Default sum
            |> toListWith ct flavour

        // The sequential mapAsync keeps the order of the chunks, the last of which is flushed by the completion of ofSeq
        Assert.AreEqual<int list>([ 40; 120; 90 ], sums, "The sums of the chunks must arrive in order")
        test <@ sum.Started = [| [| 10; 30 |]; [| 50; 70 |]; [| 90 |] |] @>
        Assert.AreEqual (1, enumerations.Value, "ofSeq must enumerate the sequence once per subscription")
    }

    [<TestMethod>]
    member _.``cancelling the token of ofSeq during the enumeration completes the whole pipeline successfully`` () : Task = task {
        use cancellation = new CancellationTokenSource ()

        let pipeline =
            Observable.ofSeq ([ 1..10 ], cancellation.Token)
            |> Observable.map (fun x ->
                // R3 checks the token before every element, so the enumeration ends right after this one
                if x = 3 then
                    cancellation.Cancel ()
                x
            )
            |> Observable.chunkBySize 2

        let! chunks = TaskObservable.toArray testContext.CancellationToken pipeline

        // A cancelled enumeration completes the sequence successfully, so the partial chunk is flushed as well
        test <@ chunks = [| [| 1; 2 |]; [| 3 |] |] @>
    }
