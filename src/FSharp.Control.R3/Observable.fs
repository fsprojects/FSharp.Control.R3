module FSharp.Control.Observable

open R3
open System.Threading
open System.Threading.Tasks

let asyncMap (f : 't -> Async<'r>) source =
    let selector x ct = ValueTask<'r> (Async.StartImmediateAsTask (f x, ct))
    ObservableExtensions.SelectAwait (source, selector)

let mapAsync (f : CancellationToken -> 't -> Task<'r>) source =
    let selector x ct = ValueTask<'r> (f ct x)
    ObservableExtensions.SelectAwait (source, selector)

let inline map (f : 't -> 'r) source = ObservableExtensions.Select (source, f)

let length source = async {
    let! ct = Async.CancellationToken
    return!
        ObservableExtensions.CountAsync (source, ct)
        |> Async.AwaitTask
}

let inline iter (action : 't -> unit) source = ObservableExtensions.ForEachAsync (source, action)

let asyncIter (action : 't -> Async<unit>) source = source |> asyncMap action |> length |> Async.Ignore

let iterAsync (action : CancellationToken -> 't -> Task<unit>) source = source |> mapAsync action |> length |> Async.Ignore
