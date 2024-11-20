module FSharp.Control.R3.Async

open R3
open System.Threading
open System.Threading.Tasks
open FSharp.Control.R3

module Observable =

    let mapAsync (options : ProcessingOptions) (f : 't -> Async<'r>) source =
        let selector x ct = ValueTask<'r> (Async.StartImmediateAsTask (f x, ct))
        ObservableExtensions.SelectAwait (
            source,
            selector,
            options.AwaitOperation,
            options.ConfigureAwait,
            options.CancelOnCompleted,
            options.MaxConcurrent
        )

    let length source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.CountAsync (source, ct)
            |> Async.AwaitTask
    }

    let inline iter (action : 't -> unit) source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.ForEachAsync (source, action, ct)
            |> Async.AwaitTask
    }

    let iterAsync options (action : 't -> Async<unit>) source = source |> mapAsync options action |> length |> Async.Ignore
