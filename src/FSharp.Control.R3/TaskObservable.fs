module FSharp.Control.R3.Task

open R3
open System.Threading
open System.Threading.Tasks
open FSharp.Control.R3

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
