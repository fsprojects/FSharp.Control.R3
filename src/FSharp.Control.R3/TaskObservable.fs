module FSharp.Control.R3.Task

open R3
open System.Threading
open System.Threading.Tasks
open FSharp.Control.R3

/// <remarks>Caution! All functions returning <see cref="Task"/>/<see cref="Task`1"/> are blocking and may never return if awaited</remarks>
module Observable =

    /// Applies an accumulator function over an observable sequence, returning the
    /// result of the aggregation as a single element in the result sequence
    let inline aggregate cancellationToken seed ([<InlineIfLambda>] f : 'r -> 't -> 'r) source =
        ObservableExtensions.AggregateAsync (source, seed, f, cancellationToken)

    /// Determines whether all elements of an observable satisfy a predicate
    let inline all cancellationToken ([<InlineIfLambda>] f : 't -> bool) source = ObservableExtensions.AllAsync (source, f, cancellationToken)

    /// <summary>
    /// Invokes an action for each element in the observable sequence, and propagates all observer
    /// messages through the result sequence.
    /// </summary>
    /// <remarks>
    /// This method can be used for debugging, logging, etc. of query behavior
    /// by intercepting the message stream to run arbitrary actions for messages on the pipeline.
    /// </remarks>
    let inline iter cancellationToken ([<InlineIfLambda>] action : 't -> unit) source =
        ObservableExtensions.ForEachAsync (source, action, cancellationToken)

    /// Determines whether an observable sequence contains a specified value
    /// which satisfies the given predicate
    let inline existsAsync cancellationToken source = ObservableExtensions.AnyAsync (source, cancellationToken)

    /// Returns the first element of an observable sequence
    let inline firstAsync cancellationToken source = ObservableExtensions.FirstAsync (source, cancellationToken)

    /// Returns the length of the observable sequence till its completion or cancellation
    let length cancellationToken source = ObservableExtensions.CountAsync (source, cancellationToken)

    /// Maps the given observable with the given asynchronous function
    let mapAsync (options : ProcessingOptions) (f : CancellationToken -> 't -> Task<'r>) source =
        let selector x ct = ValueTask<'r> (f ct x)
        ObservableExtensions.SelectAwait (
            source,
            selector,
            options.AwaitOperation,
            options.ConfigureAwait,
            options.CancelOnCompleted,
            options.MaxConcurrent
        )

    /// <summary>
    /// Invokes an asynchronous action for each element in the observable sequence, and propagates all observer
    /// messages through the result sequence.
    /// </summary>
    /// <remarks>
    /// This method can be used for debugging, logging, etc. of query behavior
    /// by intercepting the message stream to run arbitrary actions for messages on the pipeline.
    /// </remarks>
    let iterAsync cancellationToken options (action : CancellationToken -> 't -> Task<unit>) source =
        source
        |> mapAsync options action
        |> length cancellationToken
        :> Task

[<AutoOpen>]
module Extensions =

    open System.Runtime.CompilerServices
    open System.Runtime.InteropServices

    [<AbstractClass; Sealed; Extension>]
    type Observable private () =

        /// <summary>
        /// Creates observable sequence from a single element returned by cancellable <see cref="ValueTask"/>
        /// </summary>
        static member inline ofTask (asyncFactory : CancellationToken -> ValueTask, [<Optional>] configureAwait) =
            Observable.FromAsync (asyncFactory, configureAwait)

        /// <summary>
        /// Creates observable sequence from a single element returned by cancellable <see cref="ValueTask"/>
        /// </summary>
        static member inline ofTask (asyncFactory : CancellationToken -> ValueTask<'T>, [<Optional>] configureAwait) =
            Observable.FromAsync (asyncFactory, configureAwait)

        static member inline toArray (source, [<Optional>] cancellationToken) = ObservableExtensions.ToArrayAsync (source, cancellationToken)

        static member toList (source, [<Optional>] cancellationToken) = task {
            let! array = ObservableExtensions.ToArrayAsync (source, cancellationToken)
            return List.ofArray array
        }

        static member toLookup (source, keySelector : 'T -> 'Key, [<Optional>] cancellationToken) =
            ObservableExtensions.ToLookupAsync (source, keySelector, cancellationToken)

        static member toLookup (source, keySelector : 'T -> 'Key, keyComparer, [<Optional>] cancellationToken) =
            ObservableExtensions.ToLookupAsync (source, keySelector, keyComparer = keyComparer, cancellationToken = cancellationToken)

        static member toLookup (source, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element, [<Optional>] cancellationToken) =
            ObservableExtensions.ToLookupAsync (source, keySelector, elementSelector = elementSelector, cancellationToken = cancellationToken)

        static member toLookup (source, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element, keyComparer, [<Optional>] cancellationToken) =
            ObservableExtensions.ToLookupAsync (
                source,
                keySelector,
                elementSelector,
                keyComparer = keyComparer,
                cancellationToken = cancellationToken
            )
