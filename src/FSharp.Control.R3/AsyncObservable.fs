/// <summary>
/// The Async flavour: functions that consume observable sequences as <see cref="T:Microsoft.FSharp.Control.FSharpAsync`1"/> computations
/// and asynchronous operators built on Async computations.
/// </summary>
module FSharp.Control.R3.Async

open System
open System.Threading
open System.Threading.Tasks
open R3
open FSharp.Control.R3

/// Awaiting helpers shared by the Async wrappers.
module internal Interop =

    // Async.AwaitTask resumes on the synchronization context of the caller, but it raises the AggregateException of a faulted task
    // and turns a cancelled task into a failure, while the Task flavour of this library surfaces the original exception and the
    // cancellation. The helpers keep Async.AwaitTask and translate its outcome on the thread it resumed on.

    /// Raises the outcome of a task that did not complete successfully through the continuations of the computation,
    /// which keeps the stack trace of the original throw.
    let private failedOutcome (task : Task) (error : exn) : Async<'T> =
        Async.FromContinuations (fun (_, onError, onCancel) ->
            if task.IsCanceled then
                onCancel (TaskCanceledException task)
            else
                match error with
                | :? AggregateException as aggregate when aggregate.InnerExceptions.Count = 1 -> onError aggregate.InnerExceptions[0]
                | error -> onError error
        )

    /// Awaits the task, raising the original exception of a fault and cancelling the computation when the task is cancelled.
    let awaitTask (task : Task<'T>) : Async<'T> = async {
        match! Async.AwaitTask task |> Async.Catch with
        | Choice1Of2 result -> return result
        | Choice2Of2 error -> return! failedOutcome task error
    }

    /// Awaits the task, raising the original exception of a fault and cancelling the computation when the task is cancelled.
    let awaitUnitTask (task : Task) : Async<unit> = async {
        match! Async.AwaitTask task |> Async.Catch with
        | Choice1Of2 () -> return ()
        | Choice2Of2 error -> return! failedOutcome task error
    }

/// <summary>
/// Functions that consume an observable sequence as an <see cref="T:Microsoft.FSharp.Control.FSharpAsync`1"/>.
/// <para>
/// The computations are cold: they subscribe when they start, so elements that a hot source emits before the start are missed,
/// and they complete when the deciding element arrives or the source terminates, which for an endless source may be never.
/// Like <see cref="M:Microsoft.FSharp.Control.FSharpAsync.AwaitTask``1(System.Threading.Tasks.Task{``0})"/> they resume on the
/// synchronization context that was current when they started to wait.
/// <see cref="M:FSharp.Control.R3.Async.Observable.mapAsync``2(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{``0,Microsoft.FSharp.Control.FSharpAsync{``1}},R3.Observable{``0})"/>
/// and <see cref="M:FSharp.Control.R3.Async.Observable.ofAsync``1(Microsoft.FSharp.Control.FSharpAsync{``0})"/> are operators instead:
/// they return an observable sequence.
/// </para>
/// <para>
/// A failure of the source, including an error reported through <see cref="M:R3.Observer`1.OnErrorResume(System.Exception)"/>,
/// raises the original exception. Cancelling the computation disposes the subscription and cancels the computation.
/// </para>
/// </summary>
module Observable =

    /// Applies the accumulator to every element, starting from the seed, and returns the final accumulated value;
    /// the seed when the source has no element.
    let aggregate seed (f : 'r -> 't -> 'r) source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.AggregateAsync (source, seed, f, ct)
            |> Interop.awaitTask
    }

    /// Determines whether every element satisfies the predicate; returns false at the first element that does not,
    /// true when the source completes.
    let all (f : 't -> bool) source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.AllAsync (source, f, ct)
            |> Interop.awaitTask
    }

    /// Determines whether the source has any element; returns true at the first element,
    /// false when the source completes without one.
    let existsAsync source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.AnyAsync (source, ct)
            |> Interop.awaitTask
    }

    /// <summary>
    /// Returns the first element of the source.
    /// Raises <see cref="T:System.InvalidOperationException"/> when the source completes without an element.
    /// </summary>
    let firstAsync source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.FirstAsync (source, ct)
            |> Interop.awaitTask
    }

    /// <summary>
    /// Subscribes to the source and invokes the action for every element.
    /// <para>
    /// The computation completes when the source completes. An exception thrown by the action, or an error of the source,
    /// stops the processing and is raised by the computation.
    /// </para>
    /// </summary>
    let iter (action : 't -> unit) source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.ForEachAsync (source, action, ct)
            |> Interop.awaitUnitTask
    }

    /// Returns the number of elements of the source once it completes.
    let length source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.CountAsync (source, ct)
            |> Interop.awaitTask
    }

    /// <summary>
    /// Projects every element with the asynchronous function, processing elements that arrive while a previous invocation
    /// is running as defined by <paramref name="options"/>.
    /// <para>
    /// The computation runs with a cancellation token that is cancelled when the subscription is disposed,
    /// when the source fails, with <see cref="P:FSharp.Control.R3.AwaitOperationConfiguration.AwaitSwitch"/> when the next element arrives,
    /// or, with <see cref="P:FSharp.Control.R3.ProcessingOptions.CancelOnCompleted"/>, when the source completes.
    /// An exception raised by the computation is reported through <see cref="M:R3.Observer`1.OnErrorResume(System.Exception)"/>
    /// and the sequence continues, with these exceptions in R3 1.3.1:
    /// </para>
    /// <list type="bullet">
    /// <item><description>an exception that happens after the source completed is dropped, and the sequence completes successfully without it;</description></item>
    /// <item><description>
    /// an <see cref="T:System.OperationCanceledException"/> is never reported: it drops the element with
    /// <see cref="P:FSharp.Control.R3.AwaitOperationConfiguration.AwaitDrop"/>, <see cref="P:FSharp.Control.R3.AwaitOperationConfiguration.AwaitSwitch"/>
    /// and <see cref="T:FSharp.Control.R3.AwaitOperationConfiguration.AwaitParallel"/>, and stops the sequence without completing it
    /// with the other configurations;
    /// </description></item>
    /// <item><description>with a limited <see cref="T:FSharp.Control.R3.AwaitOperationConfiguration.AwaitSequentialParallel"/>, every exception permanently takes up one of the slots.</description></item>
    /// </list>
    /// <para>
    /// <see cref="M:FSharp.Control.R3.Async.Observable.iterAsync``1(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{``0,Microsoft.FSharp.Control.FSharpAsync{Microsoft.FSharp.Core.Unit}},R3.Observable{``0})"/>
    /// is not affected by these limitations.
    /// </para>
    /// </summary>
    /// <exception cref="T:System.ArgumentOutOfRangeException">Thrown when the concurrency limit of the options is 0 or below -1.</exception>
    let mapAsync (options : ProcessingOptions) (f : 't -> Async<'r>) source =
        options.Validate (nameof options)
        let selector x ct = ValueTask<'r>(Async.StartImmediateAsTask (f x, ct))
        ObservableExtensions.SelectAwait (
            source,
            selector,
            options.AwaitOperation,
            options.ConfigureAwait,
            options.CancelOnCompleted,
            options.MaxConcurrent
        )

    /// <summary>
    /// Creates an observable sequence that starts the computation on every subscription, emits its result and completes.
    /// <para>
    /// Disposing the subscription cancels the computation. A failure of the computation completes the sequence with that failure.
    /// </para>
    /// </summary>
    let ofAsync (computation : Async<'T>) =
        Observable.FromAsync (fun ct ->
            Async.StartImmediateAsTask (computation, cancellationToken = ct)
            |> ValueTask<'T>
        )

    /// Collects the elements of the source into an array once it completes.
    let toArray source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.ToArrayAsync (source, ct)
            |> Interop.awaitTask
    }

    /// Collects the elements of the source into a list once it completes.
    let toList source = async {
        let! ct = Async.CancellationToken
        let! array =
            ObservableExtensions.ToArrayAsync (source, ct)
            |> Interop.awaitTask
        return List.ofArray array
    }

    /// <summary>
    /// Subscribes to the source and invokes the asynchronous action for the elements, processing elements that arrive
    /// while a previous invocation is running as defined by <paramref name="options"/>.
    /// <para>
    /// Depending on the options, not every element reaches the action: <see cref="P:FSharp.Control.R3.AwaitOperationConfiguration.AwaitDrop"/>
    /// and <see cref="P:FSharp.Control.R3.AwaitOperationConfiguration.AwaitThrottleFirstLast"/> skip elements.
    /// The computation completes when the source and the running actions complete; with
    /// <see cref="P:FSharp.Control.R3.ProcessingOptions.CancelOnCompleted"/> it completes as soon as the source completes,
    /// the running actions are cancelled without being awaited, and the queued elements never reach the action.
    /// </para>
    /// <para>
    /// The first exception raised by the action, including an <see cref="T:System.OperationCanceledException"/> that the cancellation
    /// of its computation did not cause, stops the processing at once, also over a source that emits synchronously, and is raised
    /// by the computation. An error of the source is raised too. A computation started with an already cancelled token is cancelled
    /// without subscribing.
    /// </para>
    /// </summary>
    /// <exception cref="T:System.ArgumentOutOfRangeException">Thrown when the concurrency limit of the options is 0 or below -1.</exception>
    let iterAsync (options : ProcessingOptions) (action : 't -> Async<unit>) (source : Observable<'t>) =
        options.Validate (nameof options)
        async {
            // Binding the token cancels a computation started with an already cancelled token before it subscribes
            let! cancellationToken = Async.CancellationToken
            let guard = IterationGuard cancellationToken
            let guardedAction value = async {
                if not guard.IsStopped then
                    let! actionToken = Async.CancellationToken
                    try
                        do! action value
                    with
                    | :? OperationCanceledException when actionToken.IsCancellationRequested ->
                        // Defensive: when R3 cancels this invocation (switch, cancel on completion, disposal), FSharp.Core cancels the
                        // computation without running this handler at all; the branch only catches a cancellation that races with it,
                        // and keeps the guard aligned with the Task flavour, where such a cancellation does reach the handler
                        ()
                    | error ->
                        // Not rethrown: the guard stops the iteration and reports the failure itself (see IterationGuard)
                        guard.Fail error
            }
            // Waits through iter: waiting through length counted the elements with a checked add, which overflows on long-lived sources
            do!
                source
                |> mapAsync options guardedAction
                |> _.TakeUntil(guard.StopToken)
                |> iter ignore
            guard.ThrowIfFailed ()
        }

/// <summary>
/// Overloaded functions of the Async flavour, reachable through the type name <c>Observable</c>, such as
/// <see cref="M:FSharp.Control.R3.Async.Extensions.Observable.toLookup``2(R3.Observable{``0},Microsoft.FSharp.Core.FSharpFunc{``0,``1})"/>,
/// after opening <see cref="T:FSharp.Control.R3.Async"/>.
/// </summary>
[<AutoOpen>]
module Extensions =

    open System.Collections.Generic

    // The overloads take no cancellation token: like every other Async function they observe the token of the computation.
    // An unused generic [<Optional>] token parameter used to swallow a positional keyComparer or elementSelector.

    /// Overloaded functions of the Async flavour.
    [<AbstractClass; Sealed>]
    type Observable private () =

        /// Groups the elements of the source by key once it completes.
        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key) = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, ct)
                |> Interop.awaitTask
        }

        /// Groups the elements of the source by key, compared with the comparer, once it completes.
        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key, keyComparer : IEqualityComparer<'Key>) = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, keyComparer = keyComparer, cancellationToken = ct)
                |> Interop.awaitTask
        }

        /// Groups the projected elements of the source by key once it completes.
        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element) = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, elementSelector = elementSelector, cancellationToken = ct)
                |> Interop.awaitTask
        }

        /// Groups the projected elements of the source by key, compared with the comparer, once it completes.
        static member toLookup
            (source : Observable<'T>, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element, keyComparer : IEqualityComparer<'Key>)
            = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, elementSelector, keyComparer = keyComparer, cancellationToken = ct)
                |> Interop.awaitTask
        }
