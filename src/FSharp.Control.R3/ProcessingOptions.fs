namespace FSharp.Control.R3

open System
open System.Threading
open System.Threading.Tasks
open R3

/// <summary>
/// Defines how an asynchronous selector processes source elements that arrive while a previous invocation is still running.
/// <para>
/// The cases carry the <c>Await</c> prefix so that they never collide with other names opened next to
/// <see cref="N:FSharp.Control.R3"/>, such as <see cref="T:System.Threading.Tasks.Parallel"/>.
/// </para>
/// </summary>
type AwaitOperationConfiguration =
    /// <summary>All values are queued, and the next value waits for the completion of the asynchronous method.</summary>
    | AwaitSequential
    /// <summary>Drop new value when async operation is running.</summary>
    | AwaitDrop
    /// <summary>If the previous asynchronous method is running, it is cancelled and the next asynchronous method is executed.</summary>
    | AwaitSwitch
    /// <summary>All values are sent immediately to the asynchronous method.</summary>
    | AwaitParallel of
        /// Maximum number of concurrent invocations; -1 means no limit, otherwise it must be greater than 0.
        MaxConcurrent : int
    /// <summary>All values are sent immediately to the asynchronous method, but the results are queued and passed to the next operator in order.</summary>
    | AwaitSequentialParallel of
        /// Maximum number of concurrent invocations; -1 means no limit, otherwise it must be greater than 0.
        MaxConcurrent : int
    /// <summary>Send the first value and the last value while the asynchronous method is running.</summary>
    | AwaitThrottleFirstLast

type ProcessingOptions = {
    AwaitOperationConfiguration : AwaitOperationConfiguration
    ConfigureAwait : bool
    CancelOnCompleted : bool
} with

    static let ``default`` = {
        AwaitOperationConfiguration = AwaitOperationConfiguration.AwaitSequential
        ConfigureAwait = true
        CancelOnCompleted = false
    }

    static let ``parallel`` = {
        AwaitOperationConfiguration = AwaitOperationConfiguration.AwaitParallel -1
        ConfigureAwait = true
        CancelOnCompleted = false
    }

    static member Default = ``default``
    static member Parallel = ``parallel``

    member this.MaxConcurrent =
        match this.AwaitOperationConfiguration with
        | AwaitOperationConfiguration.AwaitSequential -> -1
        | AwaitOperationConfiguration.AwaitDrop -> -1
        | AwaitOperationConfiguration.AwaitSwitch -> -1
        | AwaitOperationConfiguration.AwaitParallel maxConcurrent -> maxConcurrent
        | AwaitOperationConfiguration.AwaitSequentialParallel maxConcurrent -> maxConcurrent
        | AwaitOperationConfiguration.AwaitThrottleFirstLast -> -1

    member this.AwaitOperation =
        match this.AwaitOperationConfiguration with
        | AwaitOperationConfiguration.AwaitSequential -> AwaitOperation.Sequential
        | AwaitOperationConfiguration.AwaitDrop -> AwaitOperation.Drop
        | AwaitOperationConfiguration.AwaitSwitch -> AwaitOperation.Switch
        | AwaitOperationConfiguration.AwaitParallel _ -> AwaitOperation.Parallel
        | AwaitOperationConfiguration.AwaitSequentialParallel _ -> AwaitOperation.SequentialParallel
        | AwaitOperationConfiguration.AwaitThrottleFirstLast -> AwaitOperation.ThrottleFirstLast

    /// <summary>
    /// Throws <see cref="T:System.ArgumentOutOfRangeException"/> when a parallel configuration has an invalid concurrency limit.
    /// <para>
    /// R3 validates the limit only when the mapped sequence is subscribed, far away from the code that built the options,
    /// so the operators that accept the options validate them eagerly.
    /// </para>
    /// </summary>
    member internal this.Validate (paramName : string) =
        match this.AwaitOperationConfiguration with
        | AwaitOperationConfiguration.AwaitParallel maxConcurrent
        | AwaitOperationConfiguration.AwaitSequentialParallel maxConcurrent when maxConcurrent = 0 || maxConcurrent < -1 ->
            raise (ArgumentOutOfRangeException (paramName, maxConcurrent, "MaxConcurrent must be -1 (no limit) or greater than 0."))
        | _ -> ()

type ChunkConfiguration<'T> =
    | ChunkCount of WindowLength : int
    | ChunkTimeSpan of WindowTime : TimeSpan * TimeProvider : TimeProvider
    | ChunkTimeSpanCount of WindowTime : TimeSpan * WindowLength : int * TimeProvider : TimeProvider
    | ChunkMilliseconds of WindowTime : int * TimeProvider : TimeProvider
    | ChunkMillisecondsCount of WindowTime : int * WindowLength : int * TimeProvider : TimeProvider
    | ChunkAsyncWindow of AsyncWindow : Func<'T, CancellationToken, ValueTask> * ConfigureAwait : bool
    | ChunkWindowBoundaries of WindowBoundaries : Observable<'T>

module ChunkConfiguration =
    let inline TimeSpan windowTime = ChunkTimeSpan (windowTime, ObservableSystem.DefaultTimeProvider)
    let inline TimeSpanCount windowTime windowLength =
        ChunkTimeSpanCount (windowTime, windowLength, ObservableSystem.DefaultTimeProvider)
    let inline Milliseconds windowTime = ChunkMilliseconds (windowTime, ObservableSystem.DefaultTimeProvider)
    let inline MillisecondsCount windowTime windowLength =
        ChunkMillisecondsCount (windowTime, windowLength, ObservableSystem.DefaultTimeProvider)
    let AsyncWindow (asyncWindow : 'T -> Async<unit>) =
        let asyncWindow element ct =
            Async.StartImmediateAsTask (asyncWindow element, ct) :> Task
            |> ValueTask
        ChunkAsyncWindow (asyncWindow, true)
