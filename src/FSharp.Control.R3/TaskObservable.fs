/// <summary>
/// The Task flavour: functions that consume observable sequences as <see cref="T:System.Threading.Tasks.Task`1"/> values
/// and asynchronous operators built on tasks.
/// </summary>
module FSharp.Control.R3.Task

open System
open System.Threading
open System.Threading.Tasks
open R3
open FSharp.Control.R3

/// <summary>
/// Functions that consume an observable sequence as a <see cref="T:System.Threading.Tasks.Task`1"/>.
/// <para>
/// The functions that return a task subscribe when they are called, so elements that a hot source emitted before the call are missed,
/// and their tasks complete when the deciding element arrives or the source terminates, which for an endless source may be never.
/// <see cref="M:FSharp.Control.R3.Task.Observable.mapAsync``2(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,Microsoft.FSharp.Core.FSharpFunc{``0,System.Threading.Tasks.Task{``1}}},R3.Observable{``0})"/>
/// is an operator instead: it returns an observable sequence and subscribes only when that sequence is subscribed.
/// </para>
/// <para>
/// A failure of the source, including an error reported through <see cref="M:R3.Observer`1.OnErrorResume(System.Exception)"/>,
/// faults the task with the original exception. Cancelling the token disposes the subscription and cancels the task.
/// </para>
/// </summary>
module Observable =

    /// Applies the accumulator to every element, starting from the seed, and returns the final accumulated value;
    /// the seed when the source has no element.
    let inline aggregate cancellationToken seed ([<InlineIfLambda>] f : 'R -> 'T -> 'R) source =
        ObservableExtensions.AggregateAsync (source, seed, f, cancellationToken)

    /// Determines whether every element satisfies the predicate; returns false at the first element that does not,
    /// true when the source completes.
    let inline all cancellationToken ([<InlineIfLambda>] f : 'T -> bool) source = ObservableExtensions.AllAsync (source, f, cancellationToken)

    /// <summary>
    /// Subscribes to the source and invokes the action for every element.
    /// <para>
    /// The task completes when the source completes. An exception thrown by the action, or an error of the source,
    /// stops the processing and faults the task.
    /// </para>
    /// </summary>
    let inline iter cancellationToken ([<InlineIfLambda>] action : 'T -> unit) source =
        ObservableExtensions.ForEachAsync (source, action, cancellationToken)

    /// Determines whether the source has any element; returns true at the first element,
    /// false when the source completes without one.
    let inline existsAsync cancellationToken source = ObservableExtensions.AnyAsync (source, cancellationToken)

    /// <summary>
    /// Returns the first element of the source.
    /// Faults with <see cref="T:System.InvalidOperationException"/> when the source completes without an element.
    /// </summary>
    let inline firstAsync cancellationToken source = ObservableExtensions.FirstAsync (source, cancellationToken)

    /// Returns the number of elements of the source once it completes.
    let length cancellationToken source = ObservableExtensions.CountAsync (source, cancellationToken)

    /// <summary>
    /// Projects every element with the asynchronous function, processing elements that arrive while a previous invocation
    /// is running as defined by <paramref name="options"/>.
    /// <para>
    /// The function receives a cancellation token that is cancelled when the subscription is disposed,
    /// when the source fails, with <see cref="P:FSharp.Control.R3.AwaitOperationConfiguration.AwaitSwitch"/> when the next element arrives,
    /// or, with <see cref="P:FSharp.Control.R3.ProcessingOptions.CancelOnCompleted"/>, when the source completes.
    /// An exception of the function is reported through <see cref="M:R3.Observer`1.OnErrorResume(System.Exception)"/>
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
    /// <see cref="M:FSharp.Control.R3.Task.Observable.iterAsync``1(System.Threading.CancellationToken,FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,Microsoft.FSharp.Core.FSharpFunc{``0,System.Threading.Tasks.Task{Microsoft.FSharp.Core.Unit}}},R3.Observable{``0})"/>
    /// is not affected by these limitations.
    /// </para>
    /// </summary>
    /// <exception cref="T:System.ArgumentOutOfRangeException">Thrown when the concurrency limit of the options is 0 or below -1.</exception>
    let mapAsync (options : ProcessingOptions) (f : CancellationToken -> 'T -> Task<'R>) source =
        options.Validate (nameof options)
        let selector x ct = ValueTask<'R>(f ct x)
        ObservableExtensions.SelectAwait (
            source,
            selector,
            options.AwaitOperation,
            options.ConfigureAwait,
            options.CancelOnCompleted,
            options.MaxConcurrent
        )

    /// <summary>
    /// Subscribes to the source and invokes the asynchronous action for the elements, processing elements that arrive
    /// while a previous invocation is running as defined by <paramref name="options"/>.
    /// <para>
    /// Depending on the options, not every element reaches the action: <see cref="P:FSharp.Control.R3.AwaitOperationConfiguration.AwaitDrop"/>
    /// and <see cref="P:FSharp.Control.R3.AwaitOperationConfiguration.AwaitThrottleFirstLast"/> skip elements.
    /// The task completes when the source and the running actions complete; with
    /// <see cref="P:FSharp.Control.R3.ProcessingOptions.CancelOnCompleted"/> it completes as soon as the source completes,
    /// the running actions are cancelled without being awaited, and the queued elements never reach the action.
    /// </para>
    /// <para>
    /// The first exception of the action, including an <see cref="T:System.OperationCanceledException"/> that the token passed to it
    /// did not cause, stops the processing at once, also over a source that emits synchronously, and the task fails with that exception.
    /// An error of the source faults the task too. An already cancelled token cancels the task without subscribing.
    /// </para>
    /// </summary>
    /// <exception cref="T:System.ArgumentOutOfRangeException">Thrown when the concurrency limit of the options is 0 or below -1.</exception>
    let iterAsync
        (cancellationToken : CancellationToken)
        (options : ProcessingOptions)
        (action : CancellationToken -> 't -> Task<unit>)
        (source : Observable<'t>)
        : Task =
        options.Validate (nameof options)
        if cancellationToken.IsCancellationRequested then
            // The guard would already skip every action; the shortcut keeps the iteration from subscribing at all
            Task.FromCanceled cancellationToken
        else
            let guard = IterationGuard cancellationToken
            let guardedAction ct value : Task<unit> = task {
                if not guard.IsStopped then
                    try
                        do! action ct value
                    with
                    | :? OperationCanceledException when ct.IsCancellationRequested ->
                        // R3 cancelled this invocation (switch, cancel on completion, disposal), which is not a failure of the iteration
                        ()
                    | error ->
                        // Not rethrown: the guard stops the iteration and reports the failure itself (see IterationGuard)
                        guard.Fail error
            }
            // Waits through iter: waiting through length counted the elements with a checked add, which overflows on long-lived sources
            let iteration =
                source
                |> mapAsync options guardedAction
                |> _.TakeUntil(guard.StopToken)
                |> iter cancellationToken ignore
            task {
                do! iteration
                guard.ThrowIfFailed ()
            }

    // toArray and toList are curried module functions taking the token first, like every other function of this module.
    // As static members of the extension type they were shadowed by the Async module functions whenever both flavours were opened.

    /// Collects the elements of the source into an array once it completes.
    let toArray (cancellationToken : CancellationToken) (source : Observable<'T>) = ObservableExtensions.ToArrayAsync (source, cancellationToken)

    /// Collects the elements of the source into a list once it completes.
    let toList (cancellationToken : CancellationToken) (source : Observable<'T>) = task {
        let! array = ObservableExtensions.ToArrayAsync (source, cancellationToken)
        return List.ofArray array
    }

/// <summary>
/// Overloaded functions of the Task flavour, reachable through the type name <c>Observable</c>, such as
/// <see cref="M:FSharp.Control.R3.Task.Extensions.Observable.ofTask``1(Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,System.Threading.Tasks.ValueTask{``0}},System.Boolean)"/>,
/// after opening <see cref="T:FSharp.Control.R3.Task"/>.
/// </summary>
[<AutoOpen>]
module Extensions =

    open System.Collections.Generic
    open System.Runtime.InteropServices

    /// Overloaded functions of the Task flavour.
    [<AbstractClass; Sealed>]
    type Observable private () =

        /// <summary>
        /// Creates an observable sequence that invokes the factory on every subscription, emits <see cref="F:R3.Unit.Default"/>
        /// when its <see cref="T:System.Threading.Tasks.ValueTask"/> completes, and then completes.
        /// <para>
        /// Disposing the subscription cancels the token passed to the factory. A failure of the task completes the sequence
        /// with that failure. <paramref name="configureAwait"/> (true by default) defines whether the continuation
        /// resumes on the captured synchronization context.
        /// </para>
        /// </summary>
        static member inline ofTask (asyncFactory : CancellationToken -> ValueTask, [<Optional; DefaultParameterValue(true)>] configureAwait : bool) =
            Observable.FromAsync (asyncFactory, configureAwait)

        /// <summary>
        /// Creates an observable sequence that invokes the factory on every subscription, emits the result of its
        /// <see cref="T:System.Threading.Tasks.ValueTask`1"/>, and then completes.
        /// <para>
        /// Disposing the subscription cancels the token passed to the factory. A failure of the task completes the sequence
        /// with that failure. <paramref name="configureAwait"/> (true by default) defines whether the continuation
        /// resumes on the captured synchronization context.
        /// </para>
        /// </summary>
        static member inline ofTask
            (asyncFactory : CancellationToken -> ValueTask<'T>, [<Optional; DefaultParameterValue(true)>] configureAwait : bool)
            =
            Observable.FromAsync (asyncFactory, configureAwait)

        /// Groups the elements of the source by key once it completes.
        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key, [<Optional>] cancellationToken : CancellationToken) =
            ObservableExtensions.ToLookupAsync (source, keySelector, cancellationToken)

        /// Groups the elements of the source by key, compared with the comparer, once it completes.
        static member toLookup
            (
                source : Observable<'T>,
                keySelector : 'T -> 'Key,
                keyComparer : IEqualityComparer<'Key>,
                [<Optional>] cancellationToken : CancellationToken
            )
            =
            ObservableExtensions.ToLookupAsync (source, keySelector, keyComparer = keyComparer, cancellationToken = cancellationToken)

        /// Groups the projected elements of the source by key once it completes.
        static member toLookup
            (source : Observable<'T>, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element, [<Optional>] cancellationToken : CancellationToken)
            =
            ObservableExtensions.ToLookupAsync (source, keySelector, elementSelector = elementSelector, cancellationToken = cancellationToken)

        /// Groups the projected elements of the source by key, compared with the comparer, once it completes.
        static member toLookup
            (
                source : Observable<'T>,
                keySelector : 'T -> 'Key,
                elementSelector : 'T -> 'Element,
                keyComparer : IEqualityComparer<'Key>,
                [<Optional>] cancellationToken : CancellationToken
            )
            =
            ObservableExtensions.ToLookupAsync (
                source,
                keySelector,
                elementSelector,
                keyComparer = keyComparer,
                cancellationToken = cancellationToken
            )
