module FSharp.Control.R3.Task

open System
open System.Threading
open System.Threading.Tasks
open R3
open FSharp.Control.R3

/// <remarks>Caution! All functions returning <see cref="Task"/>/<see cref="Task`1"/> are blocking and may never return if awaited</remarks>
module Observable =

    /// Applies an accumulator function over an observable sequence, returning the
    /// result of the aggregation as a single element in the result sequence
    let inline aggregate cancellationToken seed ([<InlineIfLambda>] f : 'R -> 'T -> 'R) source =
        ObservableExtensions.AggregateAsync (source, seed, f, cancellationToken)

    /// Determines whether all elements of an observable satisfy a predicate
    let inline all cancellationToken ([<InlineIfLambda>] f : 'T -> bool) source = ObservableExtensions.AllAsync (source, f, cancellationToken)

    /// <summary>
    /// Invokes an action for each element in the observable sequence, and propagates all observer
    /// messages through the result sequence.
    /// </summary>
    /// <remarks>
    /// This method can be used for debugging, logging, etc. of query behavior
    /// by intercepting the message stream to run arbitrary actions for messages on the pipeline.
    /// </remarks>
    let inline iter cancellationToken ([<InlineIfLambda>] action : 'T -> unit) source =
        ObservableExtensions.ForEachAsync (source, action, cancellationToken)

    /// Determines whether an observable sequence contains a specified value
    /// which satisfies the given predicate
    let inline existsAsync cancellationToken source = ObservableExtensions.AnyAsync (source, cancellationToken)

    /// Returns the first element of an observable sequence
    let inline firstAsync cancellationToken source = ObservableExtensions.FirstAsync (source, cancellationToken)

    /// Returns the length of the observable sequence till its completion or cancellation
    let length cancellationToken source = ObservableExtensions.CountAsync (source, cancellationToken)

    /// <summary>Maps the given observable with the given asynchronous function</summary>
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

[<AutoOpen>]
module Extensions =

    open System.Collections.Generic
    open System.Runtime.InteropServices

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

        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key, [<Optional>] cancellationToken : CancellationToken) =
            ObservableExtensions.ToLookupAsync (source, keySelector, cancellationToken)

        static member toLookup
            (
                source : Observable<'T>,
                keySelector : 'T -> 'Key,
                keyComparer : IEqualityComparer<'Key>,
                [<Optional>] cancellationToken : CancellationToken
            )
            =
            ObservableExtensions.ToLookupAsync (source, keySelector, keyComparer = keyComparer, cancellationToken = cancellationToken)

        static member toLookup
            (source : Observable<'T>, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element, [<Optional>] cancellationToken : CancellationToken)
            =
            ObservableExtensions.ToLookupAsync (source, keySelector, elementSelector = elementSelector, cancellationToken = cancellationToken)

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
