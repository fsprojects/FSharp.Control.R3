module FSharp.Control.R3.Tests.TestHelpers

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3

// The helpers below reach both flavours through abbreviations, so that no test file has to open both flavour modules,
// whose functions shadow each other. Module abbreviations stay local to this file even where it is opened, so a test file
// that calls both flavours directly declares its own
module TaskObservable = FSharp.Control.R3.Task.Observable
module AsyncObservable = FSharp.Control.R3.Async.Observable
type private TaskConversions = FSharp.Control.R3.Task.Extensions.Observable
type private AsyncConversions = FSharp.Control.R3.Async.Extensions.Observable

/// Deterministic sources: every one of them emits synchronously on the subscribing thread.
[<RequireQualifiedAccess>]
module Sources =

    /// Emits the items, then completes successfully.
    let values (items : 'T array) : Observable<'T> = Observable.ToObservable items

    /// Emits the items, then completes with the failure.
    let failingAfter (items : 'T array) (error : exn) : Observable<'T> =
        ObservableExtensions.Concat (Observable.ToObservable items, Observable.Throw<'T> error)

    /// <summary>
    /// Emits 1, reports the error through <see cref="M:R3.Observer`1.OnErrorResume(System.Exception)"/>, emits 2,
    /// then completes successfully.
    /// </summary>
    let resumingError (error : exn) : Observable<int> =
        Observable.Create<int>(fun observer ->
            observer.OnNext 1
            observer.OnErrorResume error
            observer.OnNext 2
            observer.OnCompleted ()
            Disposable.Empty
        )

/// <summary>
/// Fruits whose initials differ only in case, to test grouping with and without a case-insensitive comparer.
/// <para>
/// Grouped by <see cref="M:FSharp.Control.R3.Tests.TestHelpers.Fruits.initial(System.String)"/> with the default comparer,
/// they form five groups (a, A, b, B, c). With <see cref="P:System.StringComparer.OrdinalIgnoreCase"/> they form three:
/// apple and Avocado, banana and Blueberry, cherry.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module Fruits =

    /// A synchronous source of the fruits apple, Avocado, banana, Blueberry and cherry, in this order.
    let source () = Sources.values [| "apple"; "Avocado"; "banana"; "Blueberry"; "cherry" |]

    // Substring rather than a span: the key leaves the function and is stored in the lookup
    /// The grouping key of a fruit, its first letter; every test picks the comparer that decides whether its case matters.
    let initial (fruit : string) = fruit.Substring (0, 1)

    /// The element that the element selectors project a fruit to: 5, 7, 6, 9 and 6 in source order.
    let nameLength (fruit : string) = fruit.Length

/// Helpers for running Async computations from task-based tests.
[<RequireQualifiedAccess>]
module AsyncTest =

    /// <summary>
    /// Starts the computation on the calling thread with the token, so a cold Async function subscribes before this returns.
    /// <para>
    /// Binding an Async in a task expression starts it the same way but with the default token,
    /// so use this helper whenever the test needs to cancel the computation.
    /// </para>
    /// </summary>
    let start (cancellationToken : CancellationToken) (computation : Async<'T>) = Async.StartImmediateAsTask (computation, cancellationToken)

/// A counter whose reaching a target can be awaited; it owns its lock, so a wait registered while the counter changes is never missed.
[<Sealed>]
type private CountWaiters () =
    let sync = obj ()
    let mutable count = 0
    let mutable failure : (int -> exn) voption = ValueNone
    let mutable waiters : struct (int * TaskCompletionSource) list = []

    /// The current value of the counter.
    member _.Count = lock sync (fun () -> count)

    /// Increments the counter and completes the waits whose target it reached.
    member _.Increment () =
        let ready =
            lock
                sync
                (fun () ->
                    count <- count + 1
                    let ready, pending =
                        waiters
                        |> List.partition (fun struct (target, _) -> count >= target)
                    waiters <- pending
                    ready
                )
        for struct (_, waiter) in ready do
            waiter.TrySetResult () |> ignore

    /// Fails the pending waits, and every later wait for a target not reached yet, with the error for its target.
    member _.FailAll (error : int -> exn) =
        let pending =
            lock
                sync
                (fun () ->
                    failure <- ValueSome error
                    let pending = waiters
                    waiters <- []
                    pending
                )
        for struct (target, waiter) in pending do
            waiter.TrySetException (error target) |> ignore

    /// Completes once the counter reaches the target.
    member _.WaitAsync (cancellationToken : CancellationToken, target : int) : Task =
        let waiter =
            lock
                sync
                (fun () ->
                    if count >= target then
                        Task.CompletedTask
                    else
                        match failure with
                        | ValueSome error -> Task.FromException (error target)
                        | ValueNone ->
                            // RunContinuationsAsynchronously keeps the awaiting test code off the thread that increments the counter,
                            // so a test continuation never runs inside the lock of an operator
                            let waiter = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)
                            waiters <- struct (target, waiter) :: waiters
                            waiter.Task
                )
        waiter.WaitAsync cancellationToken

/// <summary>
/// An observer that records every notification of the source it is attached to.
/// <para>
/// Unlike <see cref="T:R3.Collections.LiveList`1"/> it also records the errors reported through
/// <see cref="M:R3.Observer`1.OnErrorResume(System.Exception)"/>, which LiveList forwards to the process-wide unhandled
/// exception handler, and it lets a test wait for values or completion that arrive on another thread.
/// </para>
/// <para>
/// Like every R3 <see cref="T:R3.Observer`1"/> it ignores every notification once it is disposed or has completed,
/// so it cannot show that an operator stopped forwarding: prove that with a
/// <see cref="T:FSharp.Control.R3.Tests.TestHelpers.SubscriptionProbe"/> read while the watched source is still open,
/// or with the cancellation tokens.
/// </para>
/// </summary>
[<Sealed>]
type Recorder<'T> () =
    inherit Observer<'T> ()

    let sync = obj ()
    let values = ResizeArray<'T>()
    let errors = ResizeArray<exn>()
    // RunContinuationsAsynchronously keeps the awaiting test code off the thread that delivers the notification,
    // so a test continuation never runs inside the lock of an operator
    let completion = TaskCompletionSource<Result>(TaskCreationOptions.RunContinuationsAsynchronously)
    let mutable result = ValueNone
    let arrivals = CountWaiters ()

    let notEnoughValues count : exn =
        InvalidOperationException $"The source completed before emitting %d{count} values"

    override _.OnNextCore value =
        lock sync (fun () -> values.Add value)
        arrivals.Increment ()

    override _.OnErrorResumeCore error = lock sync (fun () -> errors.Add error)

    override _.OnCompletedCore completed =
        lock sync (fun () -> result <- ValueSome completed)
        // A wait for values that can no longer arrive fails at once instead of hanging until the test timeout
        arrivals.FailAll notEnoughValues
        completion.TrySetResult completed |> ignore

    /// Snapshot of the received values in arrival order.
    member _.Values = lock sync (fun () -> values.ToArray ())

    /// <summary>Snapshot of the errors received through <see cref="M:R3.Observer`1.OnErrorResume(System.Exception)"/> in arrival order.</summary>
    member _.Errors = lock sync (fun () -> errors.ToArray ())

    /// The completion result, or ValueNone while the source has not completed.
    member _.Completion = lock sync (fun () -> result)

    /// Whether the source has completed successfully.
    member this.IsCompletedSuccessfully = this.Completion |> ValueOption.exists _.IsSuccess

    /// The exception of a failed completion, or ValueNone when the source has not completed or completed successfully.
    member this.Failure =
        this.Completion
        |> ValueOption.bind (fun completed ->
            if completed.IsFailure then
                ValueSome (nonNull completed.Exception)
            else
                ValueNone
        )

    /// Completes once at least count values have arrived; fails if the source completes before that.
    member _.WaitForValuesAsync (cancellationToken : CancellationToken, count : int) : Task = arrivals.WaitAsync (cancellationToken, count)

    /// Completes with the completion result of the source.
    member _.WaitForCompletionAsync (cancellationToken : CancellationToken) : Task<Result> = completion.Task.WaitAsync cancellationToken

    /// Subscribes a new recorder to the source; disposing the recorder disposes the subscription.
    static member Attach (source : Observable<'T>) =
        let recorder = new Recorder<'T> ()
        source.Subscribe recorder |> ignore
        recorder

/// <summary>
/// Asserts that the recorded sequence failed with this very exception, not with a wrapper or an equal copy.
/// <para>
/// Exceptions do not override <see cref="M:System.Object.Equals(System.Object)"/>, so comparing the voptions compares the
/// exceptions by reference, and on a mismatch MSTest reports the actual failure, or that the sequence has not failed.
/// </para>
/// </summary>
let assertFailedWith (expected : exn) (recorder : Recorder<'T>) (message : string) = Assert.AreEqual (ValueSome expected, recorder.Failure, message)

/// Counts subscriptions to and disposals of a source, to prove that an operator subscribed or unsubscribed.
[<Sealed>]
type SubscriptionProbe () =
    let mutable subscribed = 0
    let disposals = CountWaiters ()

    /// Number of subscriptions made to the watched source.
    member _.Subscribed = Volatile.Read &subscribed

    /// Number of subscriptions to the watched source that were disposed.
    member _.Disposed = disposals.Count

    /// Number of subscriptions to the watched source that are still alive.
    member this.Active = this.Subscribed - this.Disposed

    /// <summary>
    /// Wraps the source so that its subscriptions and disposals are counted.
    /// <para>
    /// R3 also disposes the wrapper when the watched source completes by itself,
    /// so a disposal only proves an unsubscription when the watched source never completes, such as an open subject.
    /// </para>
    /// </summary>
    member _.Watch (source : Observable<'T>) =
        source.Do (onSubscribe = (fun () -> Interlocked.Increment &subscribed |> ignore), onDispose = (fun () -> disposals.Increment ()))

    /// <summary>
    /// Completes once at least count subscriptions to the watched source have been disposed.
    /// <para>
    /// R3 completes the task of a terminal operator before it disposes the subscription, and code awaiting the task may resume
    /// in between, so a test awaits this before it asserts that a terminal function unsubscribed after awaiting it.
    /// </para>
    /// </summary>
    member _.WaitForDisposedAsync (cancellationToken : CancellationToken, count : int) : Task = disposals.WaitAsync (cancellationToken, count)

/// <summary>
/// An asynchronous selector whose invocations are recorded and whose results the test releases,
/// so the interleaving of asynchronous work is decided by the test and not by timing.
/// <para>
/// Gates are keyed by value: invocations with equal values share one gate, so tests use distinct values.
/// Releasing a value before its invocation starts makes that invocation finish at once.
/// </para>
/// <para>
/// Releasing never resumes the invocation on the releasing thread, so a test always waits with
/// <see cref="M:FSharp.Control.R3.Tests.TestHelpers.Recorder`1.WaitForValuesAsync(System.Threading.CancellationToken,System.Int32)"/>
/// or <see cref="M:FSharp.Control.R3.Tests.TestHelpers.Recorder`1.WaitForCompletionAsync(System.Threading.CancellationToken)"/>
/// before it asserts on the outcome of a release.
/// </para>
/// <para>
/// The task flavour deliberately does not observe the selector token: in R3 an
/// <see cref="T:System.OperationCanceledException"/> stops the worker loop of the sequential, sequential parallel
/// and throttle-first-last modes for good, without completing, which would turn a failed assertion into a hang.
/// Tests inspect <see cref="P:FSharp.Control.R3.Tests.TestHelpers.GatedSelector`2.Tokens"/> instead.
/// </para>
/// </summary>
[<Sealed>]
type GatedSelector<'T, 'R when 'T : equality and 'T : not null> (project : 'T -> 'R) =
    let sync = obj ()
    let gates = Dictionary<'T, TaskCompletionSource>(HashIdentity.Structural)
    let started = ResizeArray<struct ('T * CancellationToken)>()
    let mutable inFlight = 0
    let mutable maxInFlight = 0
    let starts = CountWaiters ()

    let gateFor value =
        lock
            sync
            (fun () ->
                match gates.TryGetValue value with
                | true, gate -> gate
                | false, _ ->
                    let gate = TaskCompletionSource (TaskCreationOptions.RunContinuationsAsynchronously)
                    gates.Add (value, gate)
                    gate
            )

    let enter (cancellationToken : CancellationToken) value =
        lock
            sync
            (fun () ->
                started.Add (struct (value, cancellationToken))
                inFlight <- inFlight + 1
                maxInFlight <- max maxInFlight inFlight
            )
        starts.Increment ()

    let waitAsync (cancellationToken : CancellationToken) value : Task = task {
        enter cancellationToken value
        try
            do! (gateFor value).Task
        finally
            lock sync (fun () -> inFlight <- inFlight - 1)
    }

    /// Lets the invocation for the value finish.
    member _.Release value = (gateFor value).TrySetResult() |> ignore

    /// Values the selector was invoked with, in invocation order.
    member _.Started =
        lock
            sync
            (fun () ->
                started
                |> Seq.map (fun struct (value, _) -> value)
                |> Seq.toArray
            )

    /// Tokens the selector was invoked with, in invocation order.
    member _.Tokens =
        lock
            sync
            (fun () ->
                started
                |> Seq.map (fun struct (_, token) -> token)
                |> Seq.toArray
            )

    /// Highest number of invocations that were running at the same time.
    member _.MaxInFlight = lock sync (fun () -> maxInFlight)

    /// Completes once the selector has been invoked at least count times.
    member _.WaitForStartedAsync (cancellationToken : CancellationToken, count : int) : Task = starts.WaitAsync (cancellationToken, count)

    /// <summary>
    /// The selector in the shape of <see cref="M:FSharp.Control.R3.Task.Observable.mapAsync``2(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,Microsoft.FSharp.Core.FSharpFunc{``0,System.Threading.Tasks.Task{``1}}},R3.Observable{``0})"/>.
    /// </summary>
    member _.InvokeTask (cancellationToken : CancellationToken) (value : 'T) : Task<'R> = task {
        do! waitAsync cancellationToken value
        return project value
    }

    /// <summary>
    /// The selector in the shape of <see cref="M:FSharp.Control.R3.Async.Observable.mapAsync``2(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{``0,Microsoft.FSharp.Control.FSharpAsync{``1}},R3.Observable{``0})"/>;
    /// it records the token of the computation.
    /// </summary>
    member _.InvokeAsync (value : 'T) : Async<'R> = async {
        let! cancellationToken = Async.CancellationToken
        do! waitAsync cancellationToken value |> Async.AwaitTask
        // Projected after the gate rather than inside it, so an exception of the projection is raised by the computation itself
        return project value
    }

/// <summary>
/// Asserts that applying the function to a source throws <see cref="T:System.ArgumentOutOfRangeException"/> at once,
/// before its result could be subscribed, and that the exception names the parameter and carries the rejected value.
/// </summary>
let assertArgumentRejected (apply : Observable<int> -> 'Result) (paramName : string) (rejected : int) (message : string) =
    use subject = new Subject<int> ()
    let error =
        Assert.Throws<ArgumentOutOfRangeException>(Action (fun () -> subject |> apply |> ignore), message)
    Assert.AreEqual (paramName, error.ParamName, $"%s{message}: the exception must name the parameter")
    Assert.AreEqual (box rejected, error.ActualValue, $"%s{message}: the exception must carry the rejected value")

/// MSTest dynamic data: one row per name, with the name as its only argument.
// :> obj rather than box: box returns objnull, which raises FS3261 under <Nullable>enable</Nullable>
let dataRows (names : string seq) : obj array seq = names |> Seq.map (fun name -> [| name :> obj |])

/// Dispatches to the flavour named by a data row, "Task" or "Async", of the functions that both flavours provide.
[<RequireQualifiedAccess>]
module Flavour =

    /// Fails a test whose data row names neither flavour.
    let unknown (flavour : string) : 'Result = invalidArg (nameof flavour) $"Unknown flavour %s{flavour}"

    /// Projects the source with the mapAsync function of the flavour, which invokes the selector of that flavour.
    let mapAsyncWith
        (flavour : string)
        (options : ProcessingOptions)
        (taskSelector : CancellationToken -> 'T -> Task<'R>)
        (asyncSelector : 'T -> Async<'R>)
        (source : Observable<'T>)
        : Observable<'R> =
        match flavour with
        | "Task" -> source |> TaskObservable.mapAsync options taskSelector
        | "Async" -> source |> AsyncObservable.mapAsync options asyncSelector
        | _ -> unknown flavour

    /// Projects the source with the mapAsync function of the flavour, which invokes the gated selector.
    let mapWith (flavour : string) (options : ProcessingOptions) (selector : GatedSelector<'T, 'R>) (source : Observable<'T>) : Observable<'R> =
        mapAsyncWith flavour options selector.InvokeTask selector.InvokeAsync source

/// Terminal functions run by name with fixed arguments and their results discarded, for the tests that cover every one of them.
[<RequireQualifiedAccess>]
module Terminals =

    /// The terminal functions that both flavours provide.
    let names = [|
        "aggregate"
        "all"
        "existsAsync"
        "firstAsync"
        "iter"
        "length"
        "toArray"
        "toList"
        "toLookup"
    |]

    // all checks x > 0, so it decides early only at an element that is not positive: the tests that feed positive elements
    // rely on it waiting for the completion like aggregate, iter, length, toArray, toList and toLookup

    /// Starts the Task terminal function; iterAsync is accepted as well.
    let runTask (cancellationToken : CancellationToken) (name : string) (source : Observable<int>) : Task =
        match name with
        | "aggregate" -> TaskObservable.aggregate cancellationToken 0 (+) source
        | "all" -> TaskObservable.all cancellationToken (fun x -> x > 0) source
        | "existsAsync" -> TaskObservable.existsAsync cancellationToken source
        | "firstAsync" -> TaskObservable.firstAsync cancellationToken source
        | "iter" -> TaskObservable.iter cancellationToken ignore source
        | "iterAsync" -> TaskObservable.iterAsync cancellationToken ProcessingOptions.Default (fun _ _ -> Task.FromResult ()) source
        | "length" -> TaskObservable.length cancellationToken source
        | "toArray" -> TaskObservable.toArray cancellationToken source
        | "toList" -> TaskObservable.toList cancellationToken source
        | "toLookup" -> TaskConversions.toLookup (source, (fun x -> x % 2), cancellationToken)
        | other -> invalidArg (nameof name) $"Unknown terminal function %s{other}"

    /// The Async terminal function as a cold computation.
    let runAsync (name : string) (source : Observable<int>) : Async<unit> =
        match name with
        | "aggregate" -> source |> AsyncObservable.aggregate 0 (+) |> Async.Ignore
        | "all" ->
            source
            |> AsyncObservable.all (fun x -> x > 0)
            |> Async.Ignore
        | "existsAsync" -> source |> AsyncObservable.existsAsync |> Async.Ignore
        | "firstAsync" -> source |> AsyncObservable.firstAsync |> Async.Ignore
        | "iter" -> source |> AsyncObservable.iter ignore
        | "length" -> source |> AsyncObservable.length |> Async.Ignore
        | "toArray" -> source |> AsyncObservable.toArray |> Async.Ignore
        | "toList" -> source |> AsyncObservable.toList |> Async.Ignore
        | "toLookup" ->
            AsyncConversions.toLookup (source, fun x -> x % 2)
            |> Async.Ignore
        | other -> invalidArg (nameof name) $"Unknown terminal function %s{other}"

    /// Starts the terminal function of the flavour; the Async flavour runs with the token as the token of its computation.
    let runWith (cancellationToken : CancellationToken) (flavour : string) (name : string) (source : Observable<int>) : Task =
        match flavour with
        | "Task" -> runTask cancellationToken name source
        | "Async" -> AsyncTest.start cancellationToken (runAsync name source)
        | _ -> Flavour.unknown flavour

/// <summary>
/// A synchronization context that counts the callbacks posted to it and runs them on the thread pool,
/// to observe whether a continuation resumed on the captured context.
/// </summary>
[<Sealed>]
type RecordingSynchronizationContext () =
    inherit SynchronizationContext ()

    let mutable posts = 0

    /// Number of callbacks posted to this context.
    member _.Posts = Volatile.Read &posts

    /// <inheritdoc />
    override _.Post (callback, state) =
        Interlocked.Increment &posts |> ignore
        ThreadPool.QueueUserWorkItem (fun _ -> callback.Invoke state)
        |> ignore

    /// <summary>
    /// Runs the action with this context installed as the current one and restores the previous context afterwards.
    /// The action must not await, because the current synchronization context belongs to the thread.
    /// </summary>
    member this.Run (action : unit -> 'Result) =
        let previous = SynchronizationContext.Current
        SynchronizationContext.SetSynchronizationContext this
        try
            action ()
        finally
            SynchronizationContext.SetSynchronizationContext previous
