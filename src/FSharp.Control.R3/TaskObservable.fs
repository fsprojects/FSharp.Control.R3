module FSharp.Control.R3.Task

open R3
open System.Threading
open System.Threading.Tasks
open FSharp.Control.R3

module Observable =

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

    let inline iter cancellationToken (action : 't -> unit) source = ObservableExtensions.ForEachAsync (source, action, cancellationToken)

    let iterAsync cancellationToken options (action : CancellationToken -> 't -> Task<unit>) source =
        source
        |> mapAsync options action
        |> length cancellationToken
        :> Task
