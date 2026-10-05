module FSharp.Control.R3.Async

open R3
open System.Threading
open System.Threading.Tasks
open FSharp.Control.R3

/// <remarks>Caution! All functions returning <see cref="Async`1"/> are blocking and may never return if awaited</remarks>
module Observable =

    /// Applies an accumulator function over an observable sequence, returning the
    /// result of the aggregation as a single element in the result sequence
    let aggregate seed (f : 'r -> 't -> 'r) source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.AggregateAsync (source, seed, f, ct)
            |> Async.AwaitTask
    }

    /// Determines whether all elements of an observable satisfy a predicate
    let all (f : 't -> bool) source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.AllAsync (source, f, ct)
            |> Async.AwaitTask
    }

    /// Determines whether an observable sequence contains a specified value
    /// which satisfies the given predicate
    let existsAsync source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.AnyAsync (source, ct)
            |> Async.AwaitTask
    }

    /// Returns the first element of an observable sequence
    let firstAsync source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.FirstAsync (source, ct)
            |> Async.AwaitTask
    }

    /// <summary>
    /// Invokes an action for each element in the observable sequence, and propagates all observer
    /// messages through the result sequence.
    /// </summary>
    /// <remarks>
    /// This method can be used for debugging, logging, etc. of query behavior
    /// by intercepting the message stream to run arbitrary actions for messages on the pipeline.
    /// </remarks>
    let iter (action : 't -> unit) source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.ForEachAsync (source, action, ct)
            |> Async.AwaitTask
    }

    /// Returns the last element of an observable sequence till its completion or cancellation
    let length source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.CountAsync (source, ct)
            |> Async.AwaitTask
    }

    /// <summary>Maps the given observable with the given asynchronous function</summary>
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

    /// Creates observable sequence from a single element returned by asynchronous computation
    let ofAsync (computation : Async<'T>) =
        Observable.FromAsync (fun ct ->
            Async.StartImmediateAsTask (computation, cancellationToken = ct)
            |> ValueTask<'T>
        )

    let inline toArray source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.ToArrayAsync (source, ct)
            |> Async.AwaitTask
    }

    let toList source = async {
        let! ct = Async.CancellationToken
        let! array =
            ObservableExtensions.ToArrayAsync (source, ct)
            |> Async.AwaitTask
        return List.ofArray array
    }

    /// <summary>
    /// Invokes an asynchronous action for each element in the observable sequence, and propagates all observer
    /// messages through the result sequence.
    /// </summary>
    /// <remarks>
    /// This method can be used for debugging, logging, etc. of query behavior
    /// by intercepting the message stream to run arbitrary actions for messages on the pipeline.
    /// </remarks>
    /// <exception cref="T:System.ArgumentOutOfRangeException">Thrown when the concurrency limit of the options is 0 or below -1.</exception>
    let iterAsync options (action : 't -> Async<unit>) source = source |> mapAsync options action |> length |> Async.Ignore

[<AutoOpen>]
module Extensions =

    open System.Collections.Generic

    // The overloads take no cancellation token: like every other Async function they observe the token of the computation.
    // An unused generic [<Optional>] token parameter used to swallow a positional keyComparer or elementSelector.
    [<AbstractClass; Sealed>]
    type Observable private () =

        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key) = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, ct)
                |> Async.AwaitTask
        }

        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key, keyComparer : IEqualityComparer<'Key>) = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, keyComparer = keyComparer, cancellationToken = ct)
                |> Async.AwaitTask
        }

        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element) = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, elementSelector = elementSelector, cancellationToken = ct)
                |> Async.AwaitTask
        }

        static member toLookup
            (source : Observable<'T>, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element, keyComparer : IEqualityComparer<'Key>)
            = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, elementSelector, keyComparer = keyComparer, cancellationToken = ct)
                |> Async.AwaitTask
        }
