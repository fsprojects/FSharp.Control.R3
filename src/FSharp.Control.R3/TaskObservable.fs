module FSharp.Control.R3.Task

open R3
open System.Threading
open System.Threading.Tasks
open FSharp.Control.R3

module Observable =

    /// Maps the given observable with the given function
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

    let length cancellationToken source = ObservableExtensions.CountAsync (source, cancellationToken)

    /// Invokes an action for each element in the observable sequence, and propagates all observer
    /// messages through the result sequence. This method can be used for debugging, logging, etc. of query
    /// behavior by intercepting the message stream to run arbitrary actions for messages on the pipeline.
    let inline iter cancellationToken (action : 't -> unit) source = ObservableExtensions.ForEachAsync (source, action, cancellationToken)

    let iterAsync cancellationToken options (action : CancellationToken -> 't -> Task<unit>) source =
        source
        |> mapAsync options action
        |> length cancellationToken
        :> Task
