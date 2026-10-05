namespace FSharp.Control.R3.Tests

open System
open System.Collections.Concurrent
open System.Linq
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests.TestHelpers

/// <summary>
/// Integration tests of the cold Async functions of <see cref="T:FSharp.Control.R3.Async.Observable"/> and
/// <see cref="T:FSharp.Control.R3.Async.Extensions.Observable"/> against R3.
/// <para>
/// The processing modes of
/// <see cref="M:FSharp.Control.R3.Async.Observable.mapAsync``2(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{``0,Microsoft.FSharp.Control.FSharpAsync{``1}},R3.Observable{``0})"/>
/// are covered in MapAsyncTests.fs.
/// </para>
/// </summary>
[<TestClass; AsyncTestCategory>]
type AsyncObservableTests (testContext : TestContext) =

    // Typed as exn so that Assert.AreSame accepts it next to the InvalidOperationException that Assert.ThrowsAsync returns
    let boom : exn = InvalidOperationException "boom"

    // A synchronous source that completes without elements
    let empty () : Observable<int> = Sources.values [||]

    // Starts the computation on the calling thread with the test token, so the test timeout cancels a computation that hangs
    let run (computation : Async<'T>) = AsyncTest.start testContext.CancellationToken computation

    // Elements that leave the result of the named terminal function undecided: firstAsync and existsAsync decide at the
    // first element, all (x > 0) decides early only at an element that is not positive, the others wait for the completion
    let undecidedElements (functionName : string) =
        match functionName with
        | "existsAsync"
        | "firstAsync" -> [||]
        | _ -> [| 1; 2 |]

    /// Names of the terminal functions, as the data rows of the tests that cover every one of them.
    static member TerminalFunctions : obj array seq = dataRows Terminals.names

    // length

    [<TestMethod>]
    member _.``length subscribes when the Async starts and counts only the elements pushed after that`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let lengthOfSubject = subject |> probe.Watch |> Observable.length

        // Building the computation does not subscribe, and a subject drops the elements it receives without subscribers
        subject.OnNext 1
        Assert.AreEqual (0, probe.Subscribed, "Building the computation must not subscribe to the source")

        let lengthTask = run lengthOfSubject
        Assert.AreEqual (1, probe.Subscribed, "Starting the computation must subscribe to the source")
        subject.OnNext 2
        subject.OnNext 3
        Assert.IsFalse (lengthTask.IsCompleted, "length must wait for the source to complete")
        subject.OnCompleted (Result.Success)

        let! count = lengthTask
        Assert.AreEqual (2, count, "length must count only the elements pushed after the computation started")
    }

    [<TestMethod>]
    member _.``length of a subject that completed before the Async started is zero`` () : Task = task {
        use subject = new Subject<int> ()
        subject.OnNext 1
        subject.OnCompleted (Result.Success)

        // Subscribing to a completed subject delivers only its completion, during the subscription
        let! count = run (Observable.length subject)

        Assert.AreEqual (0, count, "length must not count the elements that a subject emitted before the computation started")
    }

    [<TestMethod>]
    member _.``length subscribes to the source once on every run of the same Async`` () : Task = task {
        let probe = SubscriptionProbe ()
        let lengthOfSource =
            Sources.values [| 1; 2; 3 |]
            |> probe.Watch
            |> Observable.length

        let! first = run lengthOfSource
        let! second = run lengthOfSource

        Assert.AreEqual (3, first, "The first run must count every element")
        Assert.AreEqual (3, second, "The second run must count every element again")
        Assert.AreEqual (2, probe.Subscribed, "Every run of the computation must subscribe to the source once")
    }

    // aggregate

    [<TestMethod>]
    member _.``aggregate folds the elements in order starting from the seed`` () : Task = task {
        let source = Sources.values [| 1; 2; 3; 4 |]

        let! sum = run (source |> Observable.aggregate 0 (+))
        let! digits =
            run (
                source
                |> Observable.aggregate "" (fun digits x -> $"%s{digits}%d{x}")
            )

        Assert.AreEqual (10, sum, "aggregate must add every element to the seed")
        Assert.AreEqual ("1234", digits, "aggregate must pass the elements to the accumulator in their order")
    }

    [<TestMethod>]
    member _.``aggregate returns the seed for an empty source`` () : Task = task {
        let! result = run (empty () |> Observable.aggregate 42 (+))
        Assert.AreEqual (42, result, "aggregate must return the seed when the source has no element")
    }

    // all

    [<TestMethod>]
    member _.``all returns true for an empty source`` () : Task = task {
        // R3 completes AllAsync with true when the source completes without an element that fails the predicate
        let! result = run (empty () |> Observable.all (fun _ -> false))
        Assert.IsTrue (result, "all must be true for a source without elements, whatever the predicate")
    }

    [<TestMethod>]
    member _.``all returns true when every element satisfies the predicate`` () : Task = task {
        let! result =
            run (
                Sources.values [| 1; 2; 3 |]
                |> Observable.all (fun x -> x > 0)
            )
        Assert.IsTrue (result, "all must be true when every element satisfies the predicate")
    }

    [<TestMethod>]
    member _.``all returns false at the first element that fails the predicate and unsubscribes`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let allTask =
            subject
            |> probe.Watch
            |> Observable.all (fun x -> x > 0)
            |> run

        subject.OnNext 1
        Assert.IsFalse (allTask.IsCompleted, "all must wait while every element satisfies the predicate")
        // R3 decides at the first element that fails the predicate and disposes its subscription without waiting for completion
        subject.OnNext (-1)
        Assert.AreEqual (1, probe.Disposed, "all must unsubscribe from the source as soon as the result is decided")

        let! result = allTask
        Assert.IsFalse (result, "all must be false once an element fails the predicate")
    }

    // existsAsync

    [<TestMethod>]
    member _.``existsAsync returns false for an empty source`` () : Task = task {
        let! result = run (Observable.existsAsync (empty ()))
        Assert.IsFalse (result, "existsAsync must be false for a source without elements")
    }

    [<TestMethod>]
    member _.``existsAsync returns true at the first element and unsubscribes`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let existsTask = subject |> probe.Watch |> Observable.existsAsync |> run

        Assert.IsFalse (existsTask.IsCompleted, "existsAsync must wait for an element or for the completion")
        // R3 decides AnyAsync at the first element and disposes its subscription without waiting for completion
        subject.OnNext 5
        Assert.AreEqual (1, probe.Disposed, "existsAsync must unsubscribe from the source as soon as an element arrives")

        let! result = existsTask
        Assert.IsTrue (result, "existsAsync must be true once an element arrives")
    }

    // firstAsync

    [<TestMethod>]
    member _.``firstAsync returns the first element and unsubscribes`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let firstTask = subject |> probe.Watch |> Observable.firstAsync |> run

        // R3 decides FirstAsync at the first element and disposes its subscription, so later elements are not observed
        subject.OnNext 7
        Assert.AreEqual (1, probe.Disposed, "firstAsync must unsubscribe from the source at the first element")
        subject.OnNext 8

        let! first = firstTask
        Assert.AreEqual (7, first, "firstAsync must return the first element")
    }

    [<TestMethod>]
    member _.``firstAsync of an empty source raises InvalidOperationException rather than an AggregateException`` () : Task = task {
        // R3 faults FirstAsync with InvalidOperationException for a source without elements; the Async flavour raises that
        // exception itself, where Async.AwaitTask would raise the AggregateException of the faulted task
        do!
            Assert.ThrowsExactlyAsync<InvalidOperationException>(
                (fun () -> run (Observable.firstAsync (empty ())) :> Task),
                "firstAsync must raise exactly InvalidOperationException for a source without elements"
            )
            :> Task
    }

    [<TestMethod>]
    member _.``firstAsync of an empty source raises an exception that a typed handler in an async workflow catches`` () : Task = task {
        let firstOrFallback = async {
            try
                let! first = Observable.firstAsync (empty ())
                return $"first element %d{first}"
            with :? InvalidOperationException ->
                return "handled"
        }

        let! outcome = run firstOrFallback

        // A handler for InvalidOperationException only matches when the computation raises that exception unwrapped
        Assert.AreEqual ("handled", outcome, "The typed handler must catch the exception that firstAsync raises")
    }

    // iter

    [<TestMethod>]
    member _.``iter invokes the action for every element in order`` () : Task = task {
        let seen = ResizeArray<int>()

        do! run (Sources.values [| 1; 2; 3 |] |> Observable.iter seen.Add)

        CollectionAssert.AreEqual ([| 1; 2; 3 |], seen.ToArray (), "iter must invoke the action for every element in order")
    }

    [<TestMethod>]
    member _.``iter completes without invoking the action for an empty source`` () : Task = task {
        let seen = ResizeArray<int>()

        do! run (empty () |> Observable.iter seen.Add)

        Assert.IsEmpty (seen, "iter must not invoke the action when the source has no element")
    }

    [<TestMethod>]
    member _.``iter raises the exception of the action as the same instance and stops processing`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let invoked = ResizeArray<int>()

        let action x =
            invoked.Add x
            if x = 2 then
                raise boom

        let iterTask = subject |> probe.Watch |> Observable.iter action |> run
        subject.OnNext 1
        // R3 turns the exception of the action into a fault of ForEachAsync, which disposes its subscription at once
        subject.OnNext 2
        Assert.AreEqual (1, probe.Disposed, "iter must unsubscribe from the source when the action fails")
        subject.OnNext 3

        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> iterTask :> Task), "iter must raise the exception of the action")
        Assert.AreSame (boom, error, "iter must raise the exception of the action itself, not a wrapper")
        CollectionAssert.AreEqual ([| 1; 2 |], invoked.ToArray (), "The action must not be invoked after it failed")
    }

    // toArray and toList

    [<TestMethod>]
    member _.``toArray collects every element in order`` () : Task = task {
        let! elements = run (Observable.toArray (Sources.values [| 1; 2; 3 |]))
        CollectionAssert.AreEqual ([| 1; 2; 3 |], elements, "toArray must collect every element in order")
    }

    [<TestMethod>]
    member _.``toList collects every element in order`` () : Task = task {
        let! elements = run (Observable.toList (Sources.values [| 1; 2; 3 |]))
        Assert.AreEqual<int list>([ 1; 2; 3 ], elements, "toList must collect every element in order")
    }

    [<TestMethod>]
    member _.``toArray and toList return empty collections for an empty source`` () : Task = task {
        let! collectedArray = run (Observable.toArray (empty ()))
        let! collectedList = run (Observable.toList (empty ()))

        Assert.IsEmpty (collectedArray, "toArray must return an empty array for a source without elements")
        Assert.IsEmpty (collectedList, "toList must return an empty list for a source without elements")
    }

    // ofAsync

    [<TestMethod>]
    member _.``ofAsync emits the result of an immediate computation once and completes synchronously`` () =
        // FromAsync awaits the task of the computation, which an immediate computation has already completed,
        // so the result and the completion arrive during the subscription
        use recorder = Observable.ofAsync (async { return 7 }) |> Recorder.Attach

        CollectionAssert.AreEqual ([| 7 |], recorder.Values, "ofAsync must emit the result of the computation once")
        Assert.IsEmpty (recorder.Errors, "ofAsync must not report errors for a successful computation")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "ofAsync must complete successfully during the subscription")

    [<TestMethod>]
    member _.``ofAsync starts the computation once per subscription`` () =
        let runs = ref 0

        let numberOfRun =
            Observable.ofAsync (
                async {
                    runs.Value <- runs.Value + 1
                    return runs.Value
                }
            )

        Assert.AreEqual (0, runs.Value, "Creating the sequence must not start the computation")
        use first = Recorder.Attach numberOfRun
        use second = Recorder.Attach numberOfRun

        Assert.AreEqual (2, runs.Value, "Every subscription must start the computation once")
        CollectionAssert.AreEqual ([| 1 |], first.Values, "The first subscription must receive the result of the first run")
        CollectionAssert.AreEqual ([| 2 |], second.Values, "The second subscription must receive the result of its own run")

    [<TestMethod>]
    member _.``ofAsync completes with the failure of the computation as the same exception`` () =
        // FromAsync turns a fault of the factory task into OnCompleted(Failure), and awaiting the task of the computation
        // rethrows the exception that the computation raised
        use recorder =
            Observable.ofAsync (async { return (raise boom : int) })
            |> Recorder.Attach

        Assert.IsEmpty (recorder.Values, "A failed computation must not emit a value")
        Assert.IsEmpty (recorder.Errors, "The failure must not be reported through OnErrorResume")
        assertFailedWith boom recorder "ofAsync must complete with the exception of the computation itself"

    [<TestMethod>]
    member _.``disposing an ofAsync subscription cancels the computation`` () =
        let token = ref CancellationToken.None
        let resume = ref (fun (_ : int) -> ())

        let computation = async {
            let! cancellationToken = Async.CancellationToken
            token.Value <- cancellationToken
            // Suspends until the test resumes it, like a computation that does not observe its token
            return! Async.FromContinuations (fun (onSuccess, _, _) -> resume.Value <- onSuccess)
        }

        use recorder = Observable.ofAsync computation |> Recorder.Attach
        Assert.IsTrue (token.Value.CanBeCanceled, "Subscribing must start the computation with the token of the subscription")
        Assert.IsFalse (token.Value.IsCancellationRequested, "The token must not be cancelled while the subscription is alive")
        Assert.IsEmpty (recorder.Values, "Nothing may be emitted while the computation is running")
        Assert.IsTrue (recorder.Completion.IsNone, "The sequence must not complete while the computation is running")

        // The subscription of FromAsync is a CancellationDisposable whose token the computation runs with
        recorder.Dispose ()
        Assert.IsTrue (token.Value.IsCancellationRequested, "Disposing the subscription must cancel the token of the computation")

        // R3's Observer ignores every notification once it is disposed, so a late result can never reach a subscriber;
        // resuming only runs the abandoned continuation and nothing is asserted about it
        let onSuccess = resume.Value
        onSuccess 42

    // iterAsync

    [<TestMethod>]
    member _.``iterAsync applies the action to every element in order and completes with the source`` () : Task = task {
        let seen = ConcurrentQueue<int>()

        do!
            Sources.values [| 1; 2; 3 |]
            |> Observable.iterAsync ProcessingOptions.Default (fun x -> async { seen.Enqueue x })
            |> run

        // The default options run one action at a time in the order of the elements
        CollectionAssert.AreEqual ([| 1; 2; 3 |], seen.ToArray (), "iterAsync must apply the action to every element in order")
    }

    [<TestMethod>]
    member _.``iterAsync completes without invoking the action for an empty source`` () : Task = task {
        let seen = ConcurrentQueue<int>()

        do!
            empty ()
            |> Observable.iterAsync ProcessingOptions.Default (fun x -> async { seen.Enqueue x })
            |> run

        Assert.IsEmpty (seen, "iterAsync must not invoke the action when the source has no element")
    }

    [<TestMethod>]
    member _.``iterAsync raises a terminal failure of the source as the same exception`` () : Task = task {
        // A failure of the source makes SelectAwait cancel its running actions and publish the failure at once,
        // which faults the ForEachAsync of iterAsync with that exception
        let iterTask =
            Sources.failingAfter [| 1; 2 |] boom
            |> Observable.iterAsync ProcessingOptions.Default (fun _ -> async { return () })
            |> run

        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> iterTask :> Task), "iterAsync must raise the failure of the source")
        Assert.AreSame (boom, error, "iterAsync must raise the exception of the source itself, not a wrapper")
    }

    [<TestMethod>]
    member _.``iterAsync raises the exception of the action as the same instance and stops processing`` () : Task = task {
        use subject = new Subject<int> ()
        let invoked = ConcurrentQueue<int>()

        let action x = async {
            invoked.Enqueue x
            if x = 2 then
                raise boom
        }

        let iterTask =
            subject
            |> Observable.iterAsync ProcessingOptions.Default action
            |> run
        subject.OnNext 1
        subject.OnNext 2
        subject.OnNext 3

        // SelectAwait reports the exception of the action through OnErrorResume, which faults the ForEachAsync of iterAsync
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> iterTask :> Task), "iterAsync must raise the exception of the action")
        Assert.AreSame (boom, error, "iterAsync must raise the exception of the action itself, not a wrapper")
        // The fault disposes the mapped sequence, whose sequential worker checks its cancelled token before the next element
        CollectionAssert.AreEqual ([| 1; 2 |], invoked.ToArray (), "The action must not be invoked after it failed")
    }

    [<TestMethod>]
    member _.``iterAsync stops invoking the action after it failed even when a synchronous source emits during the subscription`` () : Task = task {
        let invoked = ConcurrentQueue<int>()

        let action x = async {
            invoked.Enqueue x
            if x = 2 then
                raise boom
        }

        // The source emits every element while iterAsync subscribes, before R3 could dispose the mapped sequence,
        // so it is the library that skips the elements after the failure
        let iterTask =
            Sources.values [| 1; 2; 3; 4; 5 |]
            |> Observable.iterAsync ProcessingOptions.Default action
            |> run

        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> iterTask :> Task), "iterAsync must raise the exception of the action")
        Assert.AreSame (boom, error, "iterAsync must raise the exception of the action itself, not a wrapper")
        CollectionAssert.AreEqual ([| 1; 2 |], invoked.ToArray (), "The action must not be invoked after it failed")
    }

    [<TestMethod>]
    member _.``iterAsync raises a failure of the action that happens after the source completed`` () : Task = task {
        let gate = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)

        let action _ = async {
            do! Async.AwaitTask gate.Task
            raise boom
        }

        let iterTask =
            Sources.values [| 1 |]
            |> Observable.iterAsync ProcessingOptions.Default action
            |> run

        // The source has completed while the action still runs
        Assert.IsFalse (iterTask.IsCompleted, "iterAsync must wait for the running action")
        // R3 drops an error that the mapped sequence reports after its source completed and would complete the iteration
        // successfully; the library raises the failure it recorded when the action failed
        gate.SetResult ()

        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> iterTask :> Task), "iterAsync must raise the late failure of the action")
        Assert.AreSame (boom, error, "iterAsync must raise the exception of the action itself")
    }

    [<TestMethod>]
    member _.``iterAsync completes successfully when an action superseded by AwaitSwitch raises a cancellation`` () : Task = task {
        use subject = new Subject<int> ()
        // Without RunContinuationsAsynchronously, releasing the first gate resumes its action inline, so the action has raised
        // its cancellation by the time SetResult returns. Its token is already cancelled, so FSharp.Core cancels the superseded
        // computation without running the try/with handler of iterAsync, and R3 drops the cancelled invocation
        let firstGate = TaskCompletionSource ()
        let lastGate = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)
        let options = { ProcessingOptions.Default with AwaitOperationConfiguration = AwaitSwitch }

        let action x = async {
            let! cancellationToken = Async.CancellationToken
            if x = 1 then
                do! Async.AwaitTask firstGate.Task
                // A well-behaved action that notices its cancellation after a wait that does not observe the token
                cancellationToken.ThrowIfCancellationRequested ()
            else
                do! Async.AwaitTask lastGate.Task
        }

        let iterTask = subject |> Observable.iterAsync options action |> run
        subject.OnNext 1
        // AwaitSwitch cancels the token of the running first action when the next element arrives
        subject.OnNext 2
        firstGate.SetResult ()
        subject.OnCompleted (Result.Success)
        lastGate.SetResult ()

        // R3 swallows the cancellation of a superseded action, and the library must not count it as a failure either
        do! iterTask
        Assert.IsTrue (iterTask.IsCompletedSuccessfully, "iterAsync must complete successfully when only a superseded action was cancelled")
    }

    [<TestMethod>]
    member _.``iterAsync stops at an OperationCanceledException that the action raises by itself instead of hanging`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let invoked = ConcurrentQueue<int>()
        // Like the TaskCanceledException of an HttpClient timeout: raised while the computation of the action is not cancelled
        let timeout : exn = TaskCanceledException "timeout"

        let action x = async {
            invoked.Enqueue x
            if x = 2 then
                raise timeout
        }

        let iterTask =
            subject
            |> probe.Watch
            |> Observable.iterAsync ProcessingOptions.Default action
            |> run
        subject.OnNext 1
        subject.OnNext 2

        // R3 swallows an OperationCanceledException of the selector, which stops its sequential worker for good without completing;
        // the library records it as the failure of the iteration instead
        let! error =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> iterTask :> Task), "iterAsync must raise the exception of the action")
        Assert.AreSame (timeout, error, "iterAsync must raise the exception of the action itself")
        subject.OnNext 3
        CollectionAssert.AreEqual ([| 1; 2 |], invoked.ToArray (), "The action must not be invoked after it failed")
        // R3 completes the task of the iteration before it disposes the subscription, so the disposal is awaited, not assumed
        do! probe.WaitForDisposedAsync (testContext.CancellationToken, 1)
        Assert.AreEqual (0, probe.Active, "The failure of the action must unsubscribe from the source")
    }

    [<TestMethod>]
    member _.``iterAsync started with an already cancelled token is cancelled without invoking the action`` () : Task = task {
        let invoked = ConcurrentQueue<int>()
        let probe = SubscriptionProbe ()
        use cancellation = new CancellationTokenSource ()
        cancellation.Cancel ()

        // FSharp.Core stops a cancelled computation at its first bind, the let! that binds the token of the iteration,
        // before iterAsync subscribes. The guard of iterAsync would skip every action of a subscribed iteration anyway,
        // so only the probe shows that the source is left alone
        let iterTask =
            Sources.values [| 1; 2; 3 |]
            |> probe.Watch
            |> Observable.iterAsync ProcessingOptions.Default (fun x -> async { invoked.Enqueue x })
            |> AsyncTest.start cancellation.Token

        let! _ =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> iterTask :> Task), "A cancelled iterAsync must raise a cancellation")
        Assert.IsTrue (iterTask.IsCanceled, "The task of the cancelled iterAsync must be cancelled, not faulted")
        Assert.IsEmpty (invoked, "The action must not be invoked with an already cancelled token")
        Assert.AreEqual (0, probe.Subscribed, "A computation started with an already cancelled token must not subscribe to the source")
    }

    [<TestMethod>]
    member _.``cancelling iterAsync cancels the token of the running action and unsubscribes from the source`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        // The gate of the action is never released: the cancellation has to stop the computation on its own
        let action = GatedSelector<int, unit> ignore
        use cancellation = CancellationTokenSource.CreateLinkedTokenSource testContext.CancellationToken

        // Parallel starts an action inside OnNext, so an element that still reached the iteration after the cancellation
        // would be in Started at once; the sequential worker would stay parked on the unreleased action 1 instead
        let iterTask =
            subject
            |> probe.Watch
            |> Observable.iterAsync ProcessingOptions.Parallel action.InvokeAsync
            |> AsyncTest.start cancellation.Token

        subject.OnNext 1
        do! action.WaitForStartedAsync (testContext.CancellationToken, 1)
        Assert.IsFalse (action.Tokens[0].IsCancellationRequested, "The running action must not be cancelled before the computation is")

        // R3 disposes the subscription of ForEachAsync inside Cancel, and disposing the mapped sequence cancels the token
        // that SelectAwait passed to the running action as the token of its computation
        cancellation.Cancel ()
        Assert.AreEqual (1, probe.Disposed, "Cancelling must unsubscribe from the source before Cancel returns")
        Assert.IsTrue (action.Tokens[0].IsCancellationRequested, "Cancelling must cancel the token of the running action")
        subject.OnNext 2
        CollectionAssert.AreEqual ([| 1 |], action.Started, "No action may start after the cancellation")

        let! _ =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> iterTask :> Task), "Awaiting a cancelled iterAsync must raise a cancellation")
        Assert.IsTrue (iterTask.IsCanceled, "The task of a cancelled iterAsync must be cancelled, not faulted")
    }

    [<TestMethod>]
    member _.``iterAsync throws ArgumentOutOfRangeException when called with a concurrency limit of zero`` () =
        let options = {
            ProcessingOptions.Default with
                AwaitOperationConfiguration = AwaitOperationConfiguration.AwaitParallel 0
        }

        // R3 would only reject the limit when the mapped sequence is subscribed; the library validates it when called
        assertArgumentRejected
            (Observable.iterAsync options (fun _ -> async { return () }))
            "options"
            0
            "iterAsync must validate the options when it is called, before the computation starts"

    // mapAsync

    [<TestMethod>]
    member _.``mapAsync with the default options projects every element in order`` () : Task = task {
        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.mapAsync ProcessingOptions.Default (fun x -> async { return x * 10 })
            |> Recorder.Attach

        // The sequential worker of SelectAwait completes the mapped sequence once it has drained its queue,
        // which is not guaranteed to happen during the subscription
        let! completion = recorder.WaitForCompletionAsync testContext.CancellationToken

        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully with the source")
        CollectionAssert.AreEqual ([| 10; 20; 30 |], recorder.Values, "mapAsync must emit the projections in the order of the elements")
        Assert.IsEmpty (recorder.Errors, "No error may be reported")
    }

    // toLookup

    [<TestMethod>]
    member _.``toLookup groups the elements by key`` () : Task = task {
        let! lookup = run (Observable.toLookup (Fruits.source (), Fruits.initial))

        // The default comparer of string keys is case-sensitive, so every initial is a key of its own
        Assert.HasCount (5, lookup, "Every distinct initial must be a key")
        CollectionAssert.AreEqual ([| "apple" |], lookup["a"] |> Seq.toArray, "The group of a key must hold its elements")
        CollectionAssert.AreEqual ([| "Avocado" |], lookup["A"] |> Seq.toArray, "Keys that differ in case must be separate groups")
    }

    [<TestMethod>]
    member _.``toLookup with a positional keyComparer groups the keys with the comparer`` () : Task = task {
        // Regression: a positional third argument used to bind to an unused cancellationToken parameter, which dropped the comparer
        let! positional =
            run (Observable.toLookup (Fruits.source (), Fruits.initial, StringComparer.OrdinalIgnoreCase))
        let! named =
            run (Observable.toLookup (Fruits.source (), Fruits.initial, keyComparer = StringComparer.OrdinalIgnoreCase))

        Assert.HasCount (3, positional, "A positional keyComparer must merge the initials that differ only in case")
        CollectionAssert.AreEqual ([| "apple"; "Avocado" |], positional["a"] |> Seq.toArray, "A merged group must hold its elements in arrival order")
        CollectionAssert.AreEqual (
            [| "banana"; "Blueberry" |],
            positional["B"] |> Seq.toArray,
            "The comparer must also find a key that differs in case"
        )
        Assert.HasCount (3, named, "A named keyComparer must group the keys the same way")
        CollectionAssert.AreEqual ([| "apple"; "Avocado" |], named["A"] |> Seq.toArray, "A named keyComparer must merge the same groups")
    }

    [<TestMethod>]
    member _.``toLookup with a positional elementSelector projects the grouped elements`` () : Task = task {
        // Regression: a positional elementSelector used to bind to an unused cancellationToken parameter, so the result was a
        // lookup of the unprojected strings; the annotated element types are part of the assertion
        let! (positional : ILookup<string, int>) =
            run (Observable.toLookup (Fruits.source (), Fruits.initial, Fruits.nameLength))
        let! (named : ILookup<string, int>) =
            run (Observable.toLookup (Fruits.source (), Fruits.initial, elementSelector = Fruits.nameLength))

        Assert.HasCount (5, positional, "Without a comparer every distinct initial must be a key")
        CollectionAssert.AreEqual ([| 6 |], positional["b"] |> Seq.toArray, "A positional elementSelector must project the grouped elements")
        CollectionAssert.AreEqual ([| 9 |], positional["B"] |> Seq.toArray, "Every group must hold the projected elements")
        Assert.HasCount (5, named, "A named elementSelector must group the keys the same way")
        CollectionAssert.AreEqual ([| 6 |], named["b"] |> Seq.toArray, "A named elementSelector must project the grouped elements")
    }

    [<TestMethod>]
    member _.``toLookup with an elementSelector and a keyComparer projects the elements and groups the keys with the comparer`` () : Task = task {
        let! positional =
            run (Observable.toLookup (Fruits.source (), Fruits.initial, Fruits.nameLength, StringComparer.OrdinalIgnoreCase))
        let! named =
            run (
                Observable.toLookup (
                    Fruits.source (),
                    Fruits.initial,
                    elementSelector = Fruits.nameLength,
                    keyComparer = StringComparer.OrdinalIgnoreCase
                )
            )

        Assert.HasCount (3, positional, "The keyComparer must merge the initials that differ only in case")
        CollectionAssert.AreEqual ([| 6; 9 |], positional["B"] |> Seq.toArray, "The merged group must hold the projected elements in arrival order")
        Assert.HasCount (3, named, "Named arguments must group the keys the same way")
        CollectionAssert.AreEqual ([| 5; 7 |], named["a"] |> Seq.toArray, "Named arguments must project and merge the same groups")
    }

    [<TestMethod>]
    member _.``toLookup returns an empty lookup for an empty source`` () : Task = task {
        let! lookup = run (Observable.toLookup (empty (), fun x -> x % 2))

        Assert.IsEmpty (lookup, "A source without elements must give a lookup without keys")
        Assert.IsFalse (lookup.Contains 0, "The empty lookup must not contain any key")
        // The lookup of R3 returns an empty sequence for a missing key
        Assert.IsEmpty (lookup[0], "A missing key must map to an empty group")
    }

    // Every terminal function

    [<TestMethod; DynamicData(nameof AsyncObservableTests.TerminalFunctions)>]
    member _.``a terminal function raises a terminal failure of the source as the same exception`` (functionName : string) : Task = task {
        let source = Sources.failingAfter (undecidedElements functionName) boom

        // R3 faults every terminal task with the exception of OnCompleted(Failure) itself, and the Async flavour raises the
        // single inner exception of the faulted task rather than the AggregateException that Async.AwaitTask raises
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> run (Terminals.runAsync functionName source) :> Task),
                $"%s{functionName} must raise the failure of the source"
            )

        Assert.AreSame (boom, error, $"%s{functionName} must raise the exception of the source itself, not a wrapper")
    }

    [<TestMethod; DynamicData(nameof AsyncObservableTests.TerminalFunctions)>]
    member _.``a terminal function raises an OnErrorResume error as the same exception and unsubscribes`` (functionName : string) : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let terminalTask =
            subject
            |> probe.Watch
            |> Terminals.runAsync functionName
            |> run

        for element in undecidedElements functionName do
            subject.OnNext element

        Assert.IsFalse (terminalTask.IsCompleted, $"%s{functionName} must wait while the source neither fails nor completes")
        // R3 terminal operators treat OnErrorResume as fatal: they fault with that exception and dispose their subscription
        subject.OnErrorResume boom
        Assert.AreEqual (1, probe.Disposed, $"%s{functionName} must unsubscribe from the source when the source reports an error")

        let! error =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> terminalTask :> Task),
                $"%s{functionName} must raise the error that the source reported"
            )

        Assert.AreSame (boom, error, $"%s{functionName} must raise the reported exception itself, not a wrapper")
    }

    [<TestMethod; DynamicData(nameof AsyncObservableTests.TerminalFunctions)>]
    member _.``cancelling the Async token cancels a terminal function and unsubscribes from the source`` (functionName : string) : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        use cancellation = CancellationTokenSource.CreateLinkedTokenSource testContext.CancellationToken
        let terminalTask =
            subject
            |> probe.Watch
            |> Terminals.runAsync functionName
            |> AsyncTest.start cancellation.Token

        for element in undecidedElements functionName do
            subject.OnNext element

        Assert.AreEqual (1, probe.Active, $"%s{functionName} must stay subscribed while its result is undecided")
        // R3 registers on the token of the computation and disposes its subscription inside Cancel
        cancellation.Cancel ()
        Assert.AreEqual (1, probe.Disposed, $"%s{functionName} must unsubscribe from the source as soon as the token is cancelled")

        let! _ =
            Assert.ThrowsAsync<OperationCanceledException>(
                (fun () -> terminalTask :> Task),
                $"Awaiting a cancelled %s{functionName} must raise a cancellation"
            )

        // The cancelled R3 task cancels the computation instead of failing it with a TaskCanceledException
        Assert.IsTrue (terminalTask.IsCanceled, $"The task of a cancelled %s{functionName} must be cancelled, not faulted")
    }

    [<TestMethod; DynamicData(nameof AsyncObservableTests.TerminalFunctions)>]
    member _.``a terminal function started with a cancelled token is cancelled without keeping a subscription`` (functionName : string) : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        use cancellation = CancellationTokenSource.CreateLinkedTokenSource testContext.CancellationToken
        cancellation.Cancel ()

        let terminalTask =
            subject
            |> probe.Watch
            |> Terminals.runAsync functionName
            |> AsyncTest.start cancellation.Token

        // FSharp.Core stops a cancelled computation at its first bind, before the R3 operator subscribes, and R3 would
        // dispose a subscription made with a cancelled token at once, so no subscription survives either way
        Assert.AreEqual (0, probe.Active, $"%s{functionName} must not keep a subscription when started with a cancelled token")

        let! _ =
            Assert.ThrowsAsync<OperationCanceledException>(
                (fun () -> terminalTask :> Task),
                $"Awaiting %s{functionName} started with a cancelled token must raise a cancellation"
            )

        Assert.IsTrue (terminalTask.IsCanceled, $"The task of %s{functionName} started with a cancelled token must be cancelled")
    }

    [<TestMethod; DynamicData(nameof AsyncObservableTests.TerminalFunctions)>]
    member _.``a terminal function resumes on the synchronization context that was current when it started to wait`` (functionName : string) : Task =
        task {
            use subject = new Subject<int> ()
            let context = RecordingSynchronizationContext ()
            let resumed = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)

            // The computation subscribes and starts to wait while the context is current, like a UI handler
            context.Run (fun () ->
                Async.StartImmediate (
                    async {
                        do! Terminals.runAsync functionName subject
                        resumed.SetResult ()
                    },
                    testContext.CancellationToken
                )
            )

            // The source completes on the test thread, which has no context: like Async.AwaitTask, the terminal function must post
            // the rest of the computation to the captured context instead of running it on the thread that completed the source
            subject.OnNext 1
            subject.OnCompleted (Result.Success)
            do! resumed.Task.WaitAsync testContext.CancellationToken

            Assert.IsTrue (context.Posts >= 1, $"%s{functionName} must resume through the synchronization context it started to wait on")
        }
