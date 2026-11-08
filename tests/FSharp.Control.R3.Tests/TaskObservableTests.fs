namespace FSharp.Control.R3.Tests

open System
open System.Collections.Concurrent
open System.Linq
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Task
open FSharp.Control.R3.Tests.TestHelpers

/// <summary>
/// Integration tests of <see cref="T:FSharp.Control.R3.Task.Observable"/> and <see cref="T:FSharp.Control.R3.Task.Extensions.Observable"/>:
/// functions that subscribe as soon as they are called and take the cancellation token first.
/// <para>
/// The selector modes of
/// <see cref="M:FSharp.Control.R3.Task.Observable.mapAsync``2(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,Microsoft.FSharp.Core.FSharpFunc{``0,System.Threading.Tasks.Task{``1}}},R3.Observable{``0})"/>
/// are covered by <see cref="T:FSharp.Control.R3.Tests.MapAsyncTests"/>.
/// </para>
/// </summary>
[<TestClass; TaskTestCategory>]
type TaskObservableTests (testContext : TestContext) =

    // MSTest creates the test class for every test, so no two tests share this exception
    let boom : exn = InvalidOperationException "boom"

    /// Checks that the recorded sequence completed with boom itself, without emitting anything before.
    let assertFailedWithBoom (case : string) (recorder : Recorder<'T>) =
        Assert.IsEmpty (recorder.Values, $"Nothing may be emitted for %s{case}")
        Assert.IsEmpty (recorder.Errors, $"The failure of %s{case} must not be reported as a resumable error")
        assertFailedWith boom recorder $"The sequence must complete with the exception of %s{case} itself"

    /// <summary>
    /// Checks that nothing is emitted while the factory task of the sequence that
    /// <see cref="M:FSharp.Control.R3.Task.Extensions.Observable.ofTask``1(Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,System.Threading.Tasks.ValueTask{``0}},System.Boolean)"/>
    /// or <see cref="M:FSharp.Control.R3.Task.Extensions.Observable.ofTask(Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,System.Threading.Tasks.ValueTask},System.Boolean)"/>
    /// created is still running, and that disposing the recorder cancels the factory token.
    /// <para>
    /// A late result cannot be observed: an <see cref="T:R3.Observer`1"/> ignores every notification once it is disposed.
    /// </para>
    /// </summary>
    let assertDisposalCancelsFactory (recorder : Recorder<'T>) (factoryToken : CancellationToken ref) =
        Assert.IsFalse (factoryToken.Value.IsCancellationRequested, "The factory token must not be cancelled while the subscription is alive")
        Assert.IsEmpty (recorder.Values, "Nothing may be emitted while the factory task is running")
        Assert.IsTrue (recorder.Completion.IsNone, "The sequence must not complete while the factory task is running")
        recorder.Dispose ()
        // R3 passes the factory the token of a cancellation disposable, which is the subscription itself
        Assert.IsTrue (factoryToken.Value.IsCancellationRequested, "Disposing the subscription must cancel the token passed to the factory")

    /// Subscribes, with a recording synchronization context installed, to the sequence that build creates over an incomplete task,
    /// completes that task on a thread pool thread after the context is removed again,
    /// and returns how many continuations were posted to the context.
    let postsToCapturedContext (build : (CancellationToken -> ValueTask<int>) -> Observable<int>) : Task<int> = task {
        let context = RecordingSynchronizationContext ()
        let factoryTask = TaskCompletionSource<int>()
        use recorder =
            context.Run (fun () ->
                build (fun _ -> ValueTask<int> factoryTask.Task)
                |> Recorder.Attach
            )
        // R3 started to await the task while the context was current. The thread pool thread that completes the task has no context,
        // so a continuation that captured the context is posted to it, while one that did not runs on that thread
        do! Task.Run (fun () -> factoryTask.SetResult 7)
        let! completion = recorder.WaitForCompletionAsync testContext.CancellationToken
        CollectionAssert.AreEqual ([| 7 |], recorder.Values, "The result of the task must be emitted once the task completes")
        Assert.IsTrue (completion.IsSuccess, "The sequence must complete successfully after the result")
        return context.Posts
    }

    // iterAsync waits through iter, so it shares with the terminal functions the failures, cancellation and disposal
    // that all of them get from R3's TaskObserverBase
    /// Names of the terminal functions of the Task flavour, as the data rows of the tests that cover every one of them.
    static member TerminalFunctions = dataRows [| yield! Terminals.names; "iterAsync" |]

    [<TestMethod>]
    member _.``length subscribes when called and counts only the elements pushed afterwards`` () : Task = task {
        use subject = new Subject<int> ()
        // A subject drops the elements pushed while nobody is subscribed
        subject.OnNext 1
        let counting = Observable.length testContext.CancellationToken subject
        subject.OnNext 2
        subject.OnNext 3
        Assert.IsFalse (counting.IsCompleted, "length must wait for the source to complete")
        subject.OnCompleted (Result.Success)
        let! count = counting
        Assert.AreEqual (2, count, "length must count only the elements pushed after it was called")
    }

    [<TestMethod>]
    member _.``length completes synchronously with the number of elements of a synchronous source`` () : Task = task {
        let counting =
            Sources.values [| 1; 2; 3 |]
            |> Observable.length testContext.CancellationToken
        // The source emits and completes inside Subscribe, so the task is already completed when length returns
        Assert.IsTrue (counting.IsCompletedSuccessfully, "length must be completed by the time it returns for a synchronous source")
        let! count = counting
        Assert.AreEqual (3, count, "length must count every element")
    }

    [<TestMethod>]
    member _.``length is zero for an empty source`` () : Task = task {
        let! count =
            (Observable.empty () : Observable<int>)
            |> Observable.length testContext.CancellationToken
        Assert.AreEqual (0, count, "An empty source has no element to count")
    }

    [<TestMethod>]
    member _.``length is cancelled and unsubscribes from a hot source when its token is cancelled`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        use cancellation = CancellationTokenSource.CreateLinkedTokenSource testContext.CancellationToken
        let counting =
            subject
            |> probe.Watch
            |> Observable.length cancellation.Token
        subject.OnNext 1
        Assert.IsFalse (counting.IsCompleted, "length must wait for the source to complete")
        // R3 registers a callback on the token that runs inside Cancel: it disposes the subscription, then cancels the task
        cancellation.Cancel ()
        Assert.AreEqual (TaskStatus.Canceled, counting.Status, "Cancel must cancel the task before it returns")
        Assert.AreEqual (1, probe.Disposed, "Cancel must dispose the subscription to the source before it returns")
        let! error =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> counting :> Task), "Awaiting a cancelled length must throw")
        Assert.AreEqual (cancellation.Token, error.CancellationToken, "The exception must carry the token that cancelled length")
    }

    [<TestMethod>]
    member _.``aggregate folds the elements in order starting from the seed`` () : Task = task {
        let! folded =
            Sources.values [| 1; 2; 3 |]
            |> Observable.aggregate testContext.CancellationToken "seed" (fun state x -> $"%s{state},%d{x}")
        Assert.AreEqual ("seed,1,2,3", folded, "The accumulator must start from the seed and receive the elements in order")
    }

    [<TestMethod>]
    member _.``aggregate returns the seed for an empty source`` () : Task = task {
        let! folded =
            (Observable.empty () : Observable<int>)
            |> Observable.aggregate testContext.CancellationToken "seed" (fun state x -> $"%s{state},%d{x}")
        Assert.AreEqual ("seed", folded, "Without elements the seed must be the result")
    }

    [<TestMethod>]
    member _.``aggregate faults with the exception of the accumulator and stops folding`` () : Task = task {
        let folded = ResizeArray<int>()
        let folding =
            Sources.values [| 1; 2; 3 |]
            |> Observable.aggregate
                testContext.CancellationToken
                0
                (fun state x ->
                    folded.Add x
                    if x = 2 then
                        raise boom
                    state + x
                )
        // R3 turns the exception of the accumulator into an error that faults the task and disposes the observer,
        // which then ignores the element that the synchronous source still pushes
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> folding :> Task), "aggregate must fault when the accumulator throws")
        Assert.AreSame (boom, error, "aggregate must fault with the exception of the accumulator itself")
        CollectionAssert.AreEqual ([| 1; 2 |], folded.ToArray (), "The accumulator must not be called after it threw")
    }

    [<TestMethod>]
    member _.``all is true when every element satisfies the predicate`` () : Task = task {
        let! result =
            Sources.values [| 1; 2; 3 |]
            |> Observable.all testContext.CancellationToken (fun x -> x > 0)
        Assert.IsTrue (result, "all must be true when no element fails the predicate")
    }

    [<TestMethod>]
    member _.``all is true for an empty source`` () : Task = task {
        let! result =
            (Observable.empty () : Observable<int>)
            |> Observable.all testContext.CancellationToken (fun x -> x > 0)
        Assert.IsTrue (result, "all must be true when there is no element to fail the predicate")
    }

    [<TestMethod>]
    member _.``all returns false at the first element that fails the predicate and unsubscribes from the source`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let tested = ResizeArray<int>()
        let allPositive =
            subject
            |> probe.Watch
            |> Observable.all
                testContext.CancellationToken
                (fun x ->
                    tested.Add x
                    x > 0
                )
        subject.OnNext 1
        Assert.IsFalse (allPositive.IsCompleted, "all must wait while every element satisfies the predicate")
        // R3 completes the task on the deciding element and disposes the subscription at once
        subject.OnNext (-1)
        Assert.IsTrue (allPositive.IsCompletedSuccessfully, "all must complete on the first element that fails the predicate")
        Assert.AreEqual (1, probe.Disposed, "all must dispose the subscription to the source once it is decided")
        subject.OnNext 2
        CollectionAssert.AreEqual ([| 1; -1 |], tested.ToArray (), "The predicate must not be called after the result is decided")
        let! result = allPositive
        Assert.IsFalse (result, "all must be false when an element fails the predicate")
    }

    [<TestMethod>]
    member _.``existsAsync is false for an empty source`` () : Task = task {
        let! result =
            (Observable.empty () : Observable<int>)
            |> Observable.existsAsync testContext.CancellationToken
        Assert.IsFalse (result, "existsAsync must be false when the source completes without an element")
    }

    [<TestMethod>]
    member _.``existsAsync returns true at the first element and unsubscribes from the source`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let exists =
            subject
            |> probe.Watch
            |> Observable.existsAsync testContext.CancellationToken
        Assert.IsFalse (exists.IsCompleted, "existsAsync must wait for an element")
        // R3 completes the task on the first element and disposes the subscription at once
        subject.OnNext 5
        Assert.IsTrue (exists.IsCompletedSuccessfully, "existsAsync must complete on the first element")
        Assert.AreEqual (1, probe.Disposed, "existsAsync must dispose the subscription to the source once it is decided")
        let! result = exists
        Assert.IsTrue (result, "existsAsync must be true when the source has an element")
    }

    [<TestMethod>]
    member _.``firstAsync returns the first element and unsubscribes from the source`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let first =
            subject
            |> probe.Watch
            |> Observable.firstAsync testContext.CancellationToken
        Assert.IsFalse (first.IsCompleted, "firstAsync must wait for an element")
        // R3 completes the task on the first element and disposes the subscription at once
        subject.OnNext 5
        Assert.AreEqual (1, probe.Disposed, "firstAsync must dispose the subscription to the source once it has the first element")
        subject.OnNext 6
        let! result = first
        Assert.AreEqual (5, result, "firstAsync must return the first element")
    }

    [<TestMethod>]
    member _.``firstAsync faults with InvalidOperationException for an empty source`` () : Task = task {
        let first =
            (Observable.empty () : Observable<int>)
            |> Observable.firstAsync testContext.CancellationToken
        let! _ =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> first :> Task), "firstAsync must fault when the source has no element")
        ()
    }

    [<TestMethod>]
    member _.``iter invokes the action for every element in order`` () : Task = task {
        let seen = ResizeArray<int>()
        let iteration =
            Sources.values [| 1; 2; 3 |]
            |> Observable.iter testContext.CancellationToken seen.Add
        // The source emits and completes inside Subscribe, so the task is already completed when iter returns
        Assert.IsTrue (iteration.IsCompletedSuccessfully, "iter must be completed by the time it returns for a synchronous source")
        do! iteration
        CollectionAssert.AreEqual ([| 1; 2; 3 |], seen.ToArray (), "The action must receive every element in order")
    }

    [<TestMethod>]
    member _.``iter completes without invoking the action for an empty source`` () : Task = task {
        let seen = ResizeArray<int>()
        do!
            (Observable.empty () : Observable<int>)
            |> Observable.iter testContext.CancellationToken seen.Add
        Assert.IsEmpty (seen, "The action must not be invoked without elements")
    }

    [<TestMethod>]
    member _.``iter faults with the exception of the action and stops processing`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let seen = ResizeArray<int>()
        let iteration =
            subject
            |> probe.Watch
            |> Observable.iter
                testContext.CancellationToken
                (fun x ->
                    if x = 2 then
                        raise boom
                    seen.Add x
                )
        subject.OnNext 1
        // R3 turns the exception of the action into an error that faults the task and disposes the subscription
        subject.OnNext 2
        Assert.AreEqual (1, probe.Disposed, "iter must dispose the subscription to the source when the action throws")
        subject.OnNext 3
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> iteration), "iter must fault when the action throws")
        Assert.AreSame (boom, error, "iter must fault with the exception of the action itself")
        CollectionAssert.AreEqual ([| 1 |], seen.ToArray (), "No element may be processed after the action threw")
    }

    [<TestMethod>]
    member _.``iter with an already cancelled token is cancelled without invoking the action for a synchronous source`` () : Task = task {
        let seen = ResizeArray<int>()
        use cancellation = new CancellationTokenSource ()
        cancellation.Cancel ()
        let iteration =
            Sources.values [| 1; 2; 3 |]
            |> Observable.iter cancellation.Token seen.Add
        // R3 registers on the token before it subscribes, so the observer is disposed before the source emits
        Assert.AreEqual (TaskStatus.Canceled, iteration.Status, "iter must be cancelled by the time it returns")
        Assert.IsEmpty (seen, "The action must not be invoked for elements emitted after the cancellation")
        let! error =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> iteration), "Awaiting a cancelled iter must throw")
        Assert.AreEqual (cancellation.Token, error.CancellationToken, "The exception must carry the token that cancelled iter")
    }

    [<TestMethod>]
    member _.``mapAsync with the default options projects every element in order and completes`` () : Task = task {
        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.mapAsync ProcessingOptions.Default (fun _ x -> Task.FromResult (x * 10))
            |> Recorder.Attach
        let! completion = recorder.WaitForCompletionAsync testContext.CancellationToken
        // The default options run the selector for one element at a time, so the results keep the order of the source
        CollectionAssert.AreEqual ([| 10; 20; 30 |], recorder.Values, "Every element must be projected in order")
        Assert.IsEmpty (recorder.Errors, "No error may be reported")
        Assert.IsTrue (completion.IsSuccess, "The projected sequence must complete successfully with the source")
    }

    [<TestMethod>]
    member _.``iterAsync invokes the action for every element in order and completes with the source`` () : Task = task {
        let seen = ConcurrentQueue<int>()
        do!
            Sources.values [| 1; 2; 3 |]
            |> Observable.iterAsync testContext.CancellationToken ProcessingOptions.Default (fun _ x -> task { seen.Enqueue x })
        // The default options run the action for one element at a time, in the order of the source
        CollectionAssert.AreEqual ([| 1; 2; 3 |], seen.ToArray (), "The action must receive every element in order")
    }

    [<TestMethod>]
    member _.``iterAsync completes without invoking the action for an empty source`` () : Task = task {
        let seen = ConcurrentQueue<int>()
        do!
            (Observable.empty () : Observable<int>)
            |> Observable.iterAsync testContext.CancellationToken ProcessingOptions.Default (fun _ x -> task { seen.Enqueue x })
        Assert.IsEmpty (seen, "The action must not be invoked without elements")
    }

    [<TestMethod>]
    member _.``iterAsync faults with the exception of the action and stops invoking it`` () : Task = task {
        use subject = new Subject<int> ()
        let invoked = ConcurrentQueue<int>()
        let iteration =
            subject
            |> Observable.iterAsync
                testContext.CancellationToken
                ProcessingOptions.Default
                (fun _ x -> task {
                    invoked.Enqueue x
                    if x = 2 then
                        raise boom
                })
        subject.OnNext 1
        subject.OnNext 2
        // mapAsync reports the exception of the action as an error, which faults the task and disposes the subscription
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> iteration), "iterAsync must fault when the action throws")
        Assert.AreSame (boom, error, "iterAsync must fault with the exception of the action itself")
        // The disposal cancels the token that the worker of mapAsync checks before it takes the next element
        subject.OnNext 3
        CollectionAssert.AreEqual ([| 1; 2 |], invoked.ToArray (), "The action must not be invoked after it threw")
    }

    [<TestMethod>]
    member _.``iterAsync stops invoking the action after it threw even when a synchronous source emits during the subscription`` () : Task = task {
        let invoked = ConcurrentQueue<int>()
        // The source emits every element while iterAsync subscribes, before R3 could dispose the mapAsync stage,
        // so it is the library that skips the elements after the failure
        let iteration =
            Sources.values [| 1; 2; 3; 4; 5 |]
            |> Observable.iterAsync
                testContext.CancellationToken
                ProcessingOptions.Default
                (fun _ x -> task {
                    invoked.Enqueue x
                    if x = 2 then
                        raise boom
                })
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> iteration), "iterAsync must fault when the action throws")
        Assert.AreSame (boom, error, "iterAsync must fault with the exception of the action itself")
        CollectionAssert.AreEqual ([| 1; 2 |], invoked.ToArray (), "The action must not be invoked after it threw")
    }

    [<TestMethod>]
    member _.``iterAsync faults with a failure of the action that happens after the source completed`` () : Task = task {
        let gate = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)
        let iteration =
            Sources.values [| 1 |]
            |> Observable.iterAsync
                testContext.CancellationToken
                ProcessingOptions.Default
                (fun _ _ -> task {
                    do! gate.Task
                    raise boom
                })
        // The source has completed while the action still runs
        Assert.IsFalse (iteration.IsCompleted, "iterAsync must wait for the running action")
        // R3 drops an error that mapAsync reports after its source completed and would complete the iteration successfully;
        // the library faults the iteration with the failure it recorded when the action threw
        gate.SetResult ()
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> iteration), "iterAsync must fault with the late failure of the action")
        Assert.AreSame (boom, error, "iterAsync must fault with the exception of the action itself")
    }

    [<TestMethod>]
    member _.``iterAsync stops at an OperationCanceledException that the action throws by itself instead of hanging`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let invoked = ConcurrentQueue<int>()
        // Like the TaskCanceledException of an HttpClient timeout: thrown while the token passed to the action is not cancelled
        let timeout : exn = TaskCanceledException "timeout"
        let iteration =
            subject
            |> probe.Watch
            |> Observable.iterAsync
                testContext.CancellationToken
                ProcessingOptions.Default
                (fun _ x -> task {
                    invoked.Enqueue x
                    if x = 2 then
                        raise timeout
                })
        subject.OnNext 1
        subject.OnNext 2
        // R3 swallows an OperationCanceledException of the selector, which stops its sequential worker for good without completing;
        // the library ends the iteration with it instead, and a task that ends with an OperationCanceledException is cancelled
        let! error =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> iteration), "iterAsync must end with the exception of the action")
        Assert.AreSame (timeout, error, "iterAsync must end with the exception of the action itself")
        Assert.IsTrue (iteration.IsCanceled, "A task that ends with an OperationCanceledException must be cancelled")
        subject.OnNext 3
        CollectionAssert.AreEqual ([| 1; 2 |], invoked.ToArray (), "The action must not be invoked after it threw")
        // R3 completes the task of the terminal operator that iterAsync waits through before it disposes the subscription,
        // and awaiting the iteration may resume in between, so the disposal is awaited rather than raced
        do! probe.WaitForDisposedAsync (testContext.CancellationToken, 1)
        Assert.AreEqual (0, probe.Active, "The failure of the action must unsubscribe from the source")
    }

    [<TestMethod>]
    member _.``iterAsync completes successfully when an action superseded by AwaitSwitch throws a cancellation`` () : Task = task {
        use subject = new Subject<int> ()
        // Without RunContinuationsAsynchronously, releasing the first gate resumes its action inline,
        // so the action has thrown its cancellation, and the library has handled it, by the time SetResult returns
        let firstGate = TaskCompletionSource ()
        let lastGate = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)
        let options = { ProcessingOptions.Default with AwaitOperationConfiguration = AwaitSwitch }
        let iteration =
            subject
            |> Observable.iterAsync
                testContext.CancellationToken
                options
                (fun cancellationToken x -> task {
                    if x = 1 then
                        do! firstGate.Task
                        // A well-behaved action that notices its cancellation after a wait that does not observe the token
                        cancellationToken.ThrowIfCancellationRequested ()
                    else
                        do! lastGate.Task
                })
        subject.OnNext 1
        // AwaitSwitch cancels the token of the running first action when the next element arrives
        subject.OnNext 2
        firstGate.SetResult ()
        subject.OnCompleted (Result.Success)
        lastGate.SetResult ()
        // R3 swallows the cancellation of a superseded action, and the library must not count it as a failure either
        do! iteration
        Assert.IsTrue (iteration.IsCompletedSuccessfully, "iterAsync must complete successfully when only a superseded action was cancelled")
    }

    [<TestMethod>]
    member _.``iterAsync with an already cancelled token is cancelled without invoking the action for a synchronous source`` () : Task = task {
        let invoked = ConcurrentQueue<int>()
        let probe = SubscriptionProbe ()
        use cancellation = new CancellationTokenSource ()
        cancellation.Cancel ()
        let iteration =
            Sources.values [| 1; 2; 3 |]
            |> probe.Watch
            |> Observable.iterAsync cancellation.Token ProcessingOptions.Default (fun _ x -> task { invoked.Enqueue x })
        // The guard already skips the action once the iteration token is cancelled; the shortcut is what keeps
        // the library from subscribing at all, so a cold source does no work
        Assert.AreEqual (0, probe.Subscribed, "iterAsync must not subscribe to the source with an already cancelled token")
        Assert.AreEqual (TaskStatus.Canceled, iteration.Status, "iterAsync must be cancelled by the time it returns")
        Assert.IsEmpty (invoked, "The action must not be invoked with an already cancelled token")
        let! error =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> iteration), "Awaiting a cancelled iterAsync must throw")
        Assert.AreEqual (cancellation.Token, error.CancellationToken, "The exception must carry the token that cancelled iterAsync")
    }

    [<TestMethod>]
    member _.``iterAsync cancellation cancels the token of the running action and unsubscribes from the source`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let action = GatedSelector<int, unit> ignore
        use cancellation = CancellationTokenSource.CreateLinkedTokenSource testContext.CancellationToken
        let iteration =
            subject
            |> probe.Watch
            |> Observable.iterAsync cancellation.Token ProcessingOptions.Default action.InvokeTask
        subject.OnNext 1
        do! action.WaitForStartedAsync (testContext.CancellationToken, 1)
        Assert.IsFalse (action.Tokens[0].IsCancellationRequested, "The token of the running action must not be cancelled before Cancel")
        // R3 disposes the iteration inside Cancel, which disposes mapAsync: that cancels the token of the running action
        // and unsubscribes from the source before the task is cancelled
        cancellation.Cancel ()
        Assert.AreEqual (TaskStatus.Canceled, iteration.Status, "Cancel must cancel the task before it returns")
        Assert.IsTrue (action.Tokens[0].IsCancellationRequested, "Cancel must cancel the token of the running action")
        Assert.AreEqual (1, probe.Disposed, "Cancel must dispose the subscription to the source before it returns")
        let! error =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> iteration), "Awaiting a cancelled iterAsync must throw")
        Assert.AreEqual (cancellation.Token, error.CancellationToken, "The exception must carry the token that cancelled iterAsync")
    }

    [<TestMethod>]
    member _.``iterAsync rejects a concurrency limit of 0 or below -1 when called`` () =
        for limit in [| 0; -2 |] do
            for configuration in [| AwaitParallel limit; AwaitSequentialParallel limit |] do
                let options = { ProcessingOptions.Default with AwaitOperationConfiguration = configuration }
                // iterAsync subscribes when it is called, so R3 would reject these limits at the call too, but with a plain
                // ArgumentException; the exception type, the parameter name and the value come from the validation of the library
                assertArgumentRejected
                    (Observable.iterAsync testContext.CancellationToken options (fun _ _ -> Task.FromResult ()))
                    "options"
                    limit
                    $"iterAsync must reject %A{configuration} when it is called"

    [<TestMethod>]
    member _.``ofTask emits the result of a completed ValueTask and completes synchronously`` () =
        use recorder =
            Observable.ofTask (fun _ -> ValueTask<int> 42)
            |> Recorder.Attach
        // R3 awaits the task inside Subscribe, and the await of a completed task continues at once
        CollectionAssert.AreEqual ([| 42 |], recorder.Values, "The result must be emitted once, before Subscribe returns")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "The sequence must complete successfully before Subscribe returns")

    [<TestMethod>]
    member _.``ofTask invokes the factory once for every subscription`` () =
        let invocations = ref 0
        let source =
            Observable.ofTask (fun _ ->
                invocations.Value <- invocations.Value + 1
                ValueTask<int> invocations.Value
            )
        use first = Recorder.Attach source
        use second = Recorder.Attach source
        Assert.AreEqual (2, invocations.Value, "Every subscription must invoke the factory")
        CollectionAssert.AreEqual ([| 1 |], first.Values, "The first subscription must receive the result of the first invocation")
        CollectionAssert.AreEqual ([| 2 |], second.Values, "The second subscription must receive the result of the second invocation")

    [<TestMethod>]
    member _.``ofTask completes with the failure of a faulted task or of a throwing factory`` () =
        use faulted =
            Observable.ofTask (fun _ -> ValueTask<int>(Task.FromException<int> boom))
            |> Recorder.Attach
        // Annotated, because a lambda that only raises fits both overloads of ofTask
        use throwing =
            Observable.ofTask (fun _ -> (raise boom : ValueTask<int>))
            |> Recorder.Attach
        // R3 invokes the factory and awaits its task inside one try block, and completes the sequence with the exception it catches
        // unless that is a cancellation by the token of the subscription
        assertFailedWithBoom "a faulted task" faulted
        assertFailedWithBoom "a throwing factory" throwing

    [<TestMethod>]
    member _.``ofTask cancels the factory token when the subscription is disposed`` () =
        let factoryTask = TaskCompletionSource<int>()
        let factoryToken = ref CancellationToken.None
        use recorder =
            Observable.ofTask (fun cancellationToken ->
                factoryToken.Value <- cancellationToken
                ValueTask<int> factoryTask.Task
            )
            |> Recorder.Attach
        assertDisposalCancelsFactory recorder factoryToken

    [<TestMethod>]
    member _.``ofTask over a non-generic ValueTask emits a single Unit and completes synchronously`` () =
        use recorder =
            Observable.ofTask (fun _ -> ValueTask.CompletedTask)
            |> Recorder.Attach
        // R3 awaits the task inside Subscribe, and the await of a completed task continues at once
        CollectionAssert.AreEqual ([| Unit.Default |], recorder.Values, "A single Unit must be emitted before Subscribe returns")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "The sequence must complete successfully before Subscribe returns")

    [<TestMethod>]
    member _.``ofTask over a non-generic ValueTask completes with the failure of a faulted task or of a throwing factory`` () =
        use faulted =
            Observable.ofTask (fun _ -> ValueTask (Task.FromException boom))
            |> Recorder.Attach
        // Annotated, because a lambda that only raises fits both overloads of ofTask
        use throwing =
            Observable.ofTask (fun _ -> (raise boom : ValueTask))
            |> Recorder.Attach
        assertFailedWithBoom "a faulted task" faulted
        assertFailedWithBoom "a throwing factory" throwing

    [<TestMethod>]
    member _.``ofTask over a non-generic ValueTask cancels the factory token when the subscription is disposed`` () =
        let factoryTask = TaskCompletionSource ()
        let factoryToken = ref CancellationToken.None
        use recorder =
            Observable.ofTask (fun cancellationToken ->
                factoryToken.Value <- cancellationToken
                ValueTask factoryTask.Task
            )
            |> Recorder.Attach
        assertDisposalCancelsFactory recorder factoryToken

    [<TestMethod>]
    member _.``ofTask resumes on the captured synchronization context when configureAwait is omitted`` () : Task = task {
        let! posts = postsToCapturedContext (fun factory -> Observable.ofTask factory)
        // configureAwait defaults to true, like the configureAwait of R3's FromAsync
        Assert.IsGreaterThanOrEqualTo (1, posts, "Without configureAwait the continuation must be posted to the captured context")
    }

    [<TestMethod>]
    member _.``ofTask resumes on the captured synchronization context when configureAwait is true`` () : Task = task {
        let! posts = postsToCapturedContext (fun factory -> Observable.ofTask (factory, true))
        Assert.IsGreaterThanOrEqualTo (1, posts, "With configureAwait the continuation must be posted to the captured context")
    }

    [<TestMethod>]
    member _.``ofTask does not resume on the captured synchronization context when configureAwait is false`` () : Task = task {
        let! posts = postsToCapturedContext (fun factory -> Observable.ofTask (factory, false))
        Assert.AreEqual (0, posts, "Without capturing the context nothing may be posted to it")
    }

    [<TestMethod>]
    member _.``toArray collects every element in order`` () : Task = task {
        let! values =
            Sources.values [| 1; 2; 3 |]
            |> Observable.toArray testContext.CancellationToken
        CollectionAssert.AreEqual ([| 1; 2; 3 |], values, "toArray must collect every element in the order of the source")
    }

    [<TestMethod>]
    member _.``toArray returns an empty array for an empty source`` () : Task = task {
        let! values =
            (Observable.empty () : Observable<int>)
            |> Observable.toArray testContext.CancellationToken
        Assert.IsEmpty (values, "An empty source must give an empty array")
    }

    [<TestMethod>]
    member _.``toList collects every element in order`` () : Task = task {
        let! values =
            Sources.values [| 1; 2; 3 |]
            |> Observable.toList testContext.CancellationToken
        Assert.AreEqual<int list>([ 1; 2; 3 ], values, "toList must collect every element in the order of the source")
    }

    [<TestMethod>]
    member _.``toList returns an empty list for an empty source`` () : Task = task {
        let! values =
            (Observable.empty () : Observable<int>)
            |> Observable.toList testContext.CancellationToken
        Assert.IsEmpty (values, "An empty source must give an empty list")
    }

    [<TestMethod>]
    member _.``toList faults with a failure that follows values instead of returning them`` () : Task = task {
        let collecting =
            Sources.failingAfter [| 1; 2 |] boom
            |> Observable.toList testContext.CancellationToken
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> collecting :> Task), "toList must fault when the source fails after values")
        Assert.AreSame (boom, error, "toList must fault with the exception of the source itself")
    }

    [<TestMethod>]
    member _.``toLookup groups the elements by key with and without a token`` () : Task = task {
        let! withoutToken = Observable.toLookup (Fruits.source (), Fruits.initial)
        let! withToken = Observable.toLookup (Fruits.source (), Fruits.initial, testContext.CancellationToken)
        for struct (overload, lookup) in [ struct ("without a token", withoutToken); struct ("with a token", withToken) ] do
            // Without a comparer the keys are compared by the default comparer, which is case-sensitive for strings
            Assert.HasCount (5, lookup, $"Every initial, in either case, must form its own group %s{overload}")
            CollectionAssert.AreEqual ([| "apple" |], Seq.toArray lookup["a"], $"The lower case initial must group its own element %s{overload}")
            CollectionAssert.AreEqual ([| "Avocado" |], Seq.toArray lookup["A"], $"The upper case initial must group its own element %s{overload}")
    }

    [<TestMethod>]
    member _.``toLookup with a positional key comparer groups the keys that the comparer finds equal with and without a token`` () : Task = task {
        // The initials are meant to be case-insensitive here, so the comparer ignores case on purpose
        let! withoutToken =
            Observable.toLookup (Fruits.source (), Fruits.initial, StringComparer.OrdinalIgnoreCase)
        let! withToken =
            Observable.toLookup (Fruits.source (), Fruits.initial, StringComparer.OrdinalIgnoreCase, testContext.CancellationToken)
        for struct (overload, lookup) in [ struct ("without a token", withoutToken); struct ("with a token", withToken) ] do
            Assert.HasCount (3, lookup, $"Initials that differ only in case must share a group %s{overload}")
            CollectionAssert.AreEqual (
                [| "apple"; "Avocado" |],
                Seq.toArray lookup["A"],
                $"The group of an initial must hold the elements of both cases in source order %s{overload}"
            )
            CollectionAssert.AreEqual (
                [| "banana"; "Blueberry" |],
                Seq.toArray lookup["b"],
                $"The group must be found with either case of the initial %s{overload}"
            )
    }

    [<TestMethod>]
    member _.``toLookup with a positional element selector groups the projected elements with and without a token`` () : Task = task {
        let! withoutToken = Observable.toLookup (Fruits.source (), Fruits.initial, Fruits.nameLength)
        let! withToken =
            Observable.toLookup (Fruits.source (), Fruits.initial, Fruits.nameLength, testContext.CancellationToken)
        // The element type of the lookups proves that the third argument was taken as the element selector
        let lookups : struct (string * ILookup<string, int>) list = [ struct ("without a token", withoutToken); struct ("with a token", withToken) ]
        for struct (overload, lookup) in lookups do
            Assert.HasCount (5, lookup, $"Without a comparer every initial, in either case, must form its own group %s{overload}")
            CollectionAssert.AreEqual ([| 6 |], Seq.toArray lookup["b"], $"The group must hold the projected element %s{overload}")
            CollectionAssert.AreEqual ([| 9 |], Seq.toArray lookup["B"], $"The group must hold the projected element %s{overload}")
    }

    [<TestMethod>]
    member _.``toLookup with an element selector and a key comparer groups the projected elements of equal keys with and without a token`` () : Task =
        task {
            // The initials are meant to be case-insensitive here, so the comparer ignores case on purpose
            let! withoutToken =
                Observable.toLookup (Fruits.source (), Fruits.initial, Fruits.nameLength, StringComparer.OrdinalIgnoreCase)
            let! withToken =
                Observable.toLookup (
                    Fruits.source (),
                    Fruits.initial,
                    Fruits.nameLength,
                    StringComparer.OrdinalIgnoreCase,
                    testContext.CancellationToken
                )
            for struct (overload, lookup) in [ struct ("without a token", withoutToken); struct ("with a token", withToken) ] do
                Assert.HasCount (3, lookup, $"Initials that differ only in case must share a group %s{overload}")
                CollectionAssert.AreEqual (
                    [| 5; 7 |],
                    Seq.toArray lookup["a"],
                    $"The group must hold the projected elements in source order %s{overload}"
                )
                CollectionAssert.AreEqual (
                    [| 6; 9 |],
                    Seq.toArray lookup["B"],
                    $"The group must hold the projected elements in source order %s{overload}"
                )
        }

    [<TestMethod>]
    member _.``toLookup returns an empty lookup for an empty source`` () : Task = task {
        let! lookup =
            Observable.toLookup ((Observable.empty () : Observable<string>), Fruits.initial, testContext.CancellationToken)
        // R3's lookup answers a key it does not contain with an empty sequence rather than with an exception
        Assert.IsEmpty (lookup, "An empty source must give a lookup without groups")
        Assert.IsEmpty (lookup["a"], "A missing key must give an empty sequence")
        Assert.IsFalse (lookup.Contains "a", "An empty lookup must not contain any key")
    }

    [<TestMethod; DynamicData(nameof TaskObservableTests.TerminalFunctions)>]
    member _.``Task terminal functions fault with a terminal failure of the source as the same exception`` (functionName : string) : Task = task {
        let terminal =
            Terminals.runTask testContext.CancellationToken functionName (Sources.failingAfter [||] boom)
        // The failure arrives inside Subscribe, and R3 faults the task with it at once
        Assert.IsTrue (terminal.IsFaulted, $"%s{functionName} must be faulted by the time it returns")
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> terminal), $"Awaiting %s{functionName} must throw the failure of the source")
        Assert.AreSame (boom, error, $"%s{functionName} must fault with the exception of the source itself")
    }

    [<TestMethod; DynamicData(nameof TaskObservableTests.TerminalFunctions)>]
    member _.``Task terminal functions fault with an error resumed by a hot source and unsubscribe from it`` (functionName : string) : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        let terminal =
            subject
            |> probe.Watch
            |> Terminals.runTask testContext.CancellationToken functionName
        Assert.IsFalse (terminal.IsCompleted, $"%s{functionName} must wait for the source")
        // R3's terminal observers treat an error reported through OnErrorResume as fatal: they fault the task and dispose the subscription
        subject.OnErrorResume boom
        Assert.AreEqual (1, probe.Disposed, $"%s{functionName} must dispose the subscription to the source when the error arrives")
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>((fun () -> terminal), $"Awaiting %s{functionName} must throw the error")
        Assert.AreSame (boom, error, $"%s{functionName} must fault with the resumed exception itself")
    }

    [<TestMethod; DynamicData(nameof TaskObservableTests.TerminalFunctions)>]
    member _.``Task terminal functions are cancelled and unsubscribe from a hot source when their token is cancelled``
        (functionName : string)
        : Task
        = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        use cancellation = CancellationTokenSource.CreateLinkedTokenSource testContext.CancellationToken
        let terminal =
            subject
            |> probe.Watch
            |> Terminals.runTask cancellation.Token functionName
        Assert.IsFalse (terminal.IsCompleted, $"%s{functionName} must wait for the source")
        // R3 runs its cancellation callback inside Cancel, and the callback disposes the subscription before it cancels the task.
        // The status is checked only after awaiting, because toList cancels its own task in a continuation
        cancellation.Cancel ()
        Assert.AreEqual (1, probe.Disposed, $"Cancel must dispose the subscription of %s{functionName} to the source before it returns")
        let! error =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> terminal), $"Awaiting a cancelled %s{functionName} must throw")
        Assert.AreEqual (cancellation.Token, error.CancellationToken, $"The exception must carry the token that cancelled %s{functionName}")
        Assert.AreEqual (TaskStatus.Canceled, terminal.Status, $"%s{functionName} must be cancelled rather than faulted")
    }

    [<TestMethod; DynamicData(nameof TaskObservableTests.TerminalFunctions)>]
    member _.``Task terminal functions with an already cancelled token are cancelled when they return and keep no subscription``
        (functionName : string)
        : Task
        = task {
        // A hot source, because only a source that never completes by itself lets the probe prove that the subscription was disposed
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        use cancellation = new CancellationTokenSource ()
        cancellation.Cancel ()
        let terminal =
            subject
            |> probe.Watch
            |> Terminals.runTask cancellation.Token functionName
        // R3 registers on the token before it subscribes, so the callback disposes the observer and cancels the task at once;
        // the subscription that R3 still makes is disposed as soon as it is assigned to the disposed observer
        Assert.AreEqual (TaskStatus.Canceled, terminal.Status, $"%s{functionName} must be cancelled by the time it returns")
        Assert.AreEqual (0, probe.Active, $"%s{functionName} must not keep a subscription to the source")
        if functionName = "iterAsync" then
            // Unlike the R3 terminals, which subscribe and dispose at once, iterAsync does not subscribe at all
            Assert.AreEqual (0, probe.Subscribed, "iterAsync must not subscribe to the source with an already cancelled token")
        let! error =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> terminal), $"Awaiting a cancelled %s{functionName} must throw")
        Assert.AreEqual (cancellation.Token, error.CancellationToken, $"The exception must carry the token that cancelled %s{functionName}")
    }
