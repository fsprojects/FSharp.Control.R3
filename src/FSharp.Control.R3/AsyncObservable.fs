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

/// <remarks>Caution! All functions returning <see cref="Async`1"/> are blocking and may never return if awaited</remarks>
module Observable =

    /// Applies an accumulator function over an observable sequence, returning the
    /// result of the aggregation as a single element in the result sequence
    let aggregate seed (f : 'r -> 't -> 'r) source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.AggregateAsync (source, seed, f, ct)
            |> Interop.awaitTask
    }

    /// Determines whether all elements of an observable satisfy a predicate
    let all (f : 't -> bool) source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.AllAsync (source, f, ct)
            |> Interop.awaitTask
    }

    /// Determines whether an observable sequence contains a specified value
    /// which satisfies the given predicate
    let existsAsync source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.AnyAsync (source, ct)
            |> Interop.awaitTask
    }

    /// Returns the first element of an observable sequence
    let firstAsync source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.FirstAsync (source, ct)
            |> Interop.awaitTask
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
            |> Interop.awaitUnitTask
    }

    /// Returns the last element of an observable sequence till its completion or cancellation
    let length source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.CountAsync (source, ct)
            |> Interop.awaitTask
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

    let toArray source = async {
        let! ct = Async.CancellationToken
        return!
            ObservableExtensions.ToArrayAsync (source, ct)
            |> Interop.awaitTask
    }

    let toList source = async {
        let! ct = Async.CancellationToken
        let! array =
            ObservableExtensions.ToArrayAsync (source, ct)
            |> Interop.awaitTask
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
                |> Interop.awaitTask
        }

        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key, keyComparer : IEqualityComparer<'Key>) = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, keyComparer = keyComparer, cancellationToken = ct)
                |> Interop.awaitTask
        }

        static member toLookup (source : Observable<'T>, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element) = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, elementSelector = elementSelector, cancellationToken = ct)
                |> Interop.awaitTask
        }

        static member toLookup
            (source : Observable<'T>, keySelector : 'T -> 'Key, elementSelector : 'T -> 'Element, keyComparer : IEqualityComparer<'Key>)
            = async {
            let! ct = Async.CancellationToken
            return!
                ObservableExtensions.ToLookupAsync (source, keySelector, elementSelector, keyComparer = keyComparer, cancellationToken = ct)
                |> Interop.awaitTask
        }
