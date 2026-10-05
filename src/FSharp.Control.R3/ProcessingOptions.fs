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
    /// <summary>Up to MaxConcurrent values, or all of them when it is -1, are sent to the asynchronous method at once; the others wait in a queue.</summary>
    | AwaitParallel of
        /// Maximum number of concurrent invocations; -1 means no limit, otherwise it must be greater than 0.
        MaxConcurrent : int
    /// <summary>
    /// Up to MaxConcurrent values, or all of them when it is -1, are sent to the asynchronous method at once and the others wait in a queue,
    /// but the results are passed to the next operator in the order of the values.
    /// </summary>
    | AwaitSequentialParallel of
        /// Maximum number of concurrent invocations; -1 means no limit, otherwise it must be greater than 0.
        MaxConcurrent : int
    /// <summary>Send the first value and the last value while the asynchronous method is running.</summary>
    | AwaitThrottleFirstLast

/// Options that configure how an asynchronous selector is applied to an observable sequence.
type ProcessingOptions = {
    /// Defines how elements that arrive while a previous invocation is running are processed.
    AwaitOperationConfiguration : AwaitOperationConfiguration
    /// Whether R3 resumes on the captured synchronization context after awaiting the result of the asynchronous selector.
    /// The awaits inside the selector capture the context on their own.
    ConfigureAwait : bool
    /// Whether the running invocations are cancelled, their results dropped and the queued elements discarded when the source completes successfully.
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

    /// Processes the elements one at a time in arrival order, capturing the synchronization context.
    static member Default = ``default``

    /// Processes all elements concurrently without a limit, capturing the synchronization context.
    static member Parallel = ``parallel``

    /// The concurrency limit of the parallel configurations; -1 (no limit) for every other configuration.
    member this.MaxConcurrent =
        match this.AwaitOperationConfiguration with
        | AwaitOperationConfiguration.AwaitSequential -> -1
        | AwaitOperationConfiguration.AwaitDrop -> -1
        | AwaitOperationConfiguration.AwaitSwitch -> -1
        | AwaitOperationConfiguration.AwaitParallel maxConcurrent -> maxConcurrent
        | AwaitOperationConfiguration.AwaitSequentialParallel maxConcurrent -> maxConcurrent
        | AwaitOperationConfiguration.AwaitThrottleFirstLast -> -1

    /// <summary>The R3 <see cref="T:R3.AwaitOperation"/> that corresponds to <see cref="P:FSharp.Control.R3.ProcessingOptions.AwaitOperationConfiguration"/>.</summary>
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

/// Defines how an observable sequence is divided into chunks.
type ChunkConfiguration<'T> =
    /// Chunks of at most a number of elements.
    | ChunkCount of
        /// Maximum number of elements of a chunk.
        WindowLength : int
    /// Chunks of the elements received during a time span, measured from the first element of every chunk.
    | ChunkTimeSpan of
        /// Duration of a chunk, measured from its first element.
        WindowTime : TimeSpan *
        /// Time provider that measures the duration.
        TimeProvider : TimeProvider
    /// Chunks of at most a number of elements received during a time span, measured from the first element of every chunk.
    | ChunkTimeSpanCount of
        /// Duration of a chunk, measured from its first element.
        WindowTime : TimeSpan *
        /// Maximum number of elements of a chunk.
        WindowLength : int *
        /// Time provider that measures the duration.
        TimeProvider : TimeProvider
    /// Chunks of the elements received during a number of milliseconds, measured from the first element of every chunk.
    | ChunkMilliseconds of
        /// Duration of a chunk in milliseconds, measured from its first element.
        WindowTime : int *
        /// Time provider that measures the duration.
        TimeProvider : TimeProvider
    /// Chunks of at most a number of elements received during a number of milliseconds, measured from the first element of every chunk.
    | ChunkMillisecondsCount of
        /// Duration of a chunk in milliseconds, measured from its first element.
        WindowTime : int *
        /// Maximum number of elements of a chunk.
        WindowLength : int *
        /// Time provider that measures the duration.
        TimeProvider : TimeProvider
    /// Chunks that start with an element and end when the asynchronous window started for that element completes.
    | ChunkAsyncWindow of
        /// Starts the window of a chunk for its first element; the chunk ends when the returned task completes.
        AsyncWindow : Func<'T, CancellationToken, ValueTask> *
        /// Whether the continuation after the window resumes on the captured synchronization context.
        ConfigureAwait : bool
    /// <summary>
    /// Chunks that end on every element of a boundary sequence.
    /// <para>
    /// The boundaries must have the element type of the source;
    /// <see cref="M:FSharp.Control.R3.Observable.chunkByBoundaries``2(R3.Observable{``0},R3.Observable{``1})"/>
    /// accepts boundaries of any element type.
    /// </para>
    /// </summary>
    | ChunkWindowBoundaries of
        /// Every element of this sequence ends a chunk.
        WindowBoundaries : Observable<'T>

/// <summary>
/// Helpers that create <see cref="T:FSharp.Control.R3.ChunkConfiguration`1"/> values.
/// <para>
/// The time based helpers read <see cref="P:R3.ObservableSystem.DefaultTimeProvider"/> when they are called;
/// use the union cases directly to pass another time provider.
/// </para>
/// </summary>
module ChunkConfiguration =

    /// <summary>Chunks of the elements received during <paramref name="windowTime"/>, measured by the default time provider.</summary>
    let inline TimeSpan windowTime = ChunkTimeSpan (windowTime, ObservableSystem.DefaultTimeProvider)

    /// <summary>
    /// Chunks of at most <paramref name="windowLength"/> elements received during <paramref name="windowTime"/>,
    /// measured by the default time provider.
    /// </summary>
    let inline TimeSpanCount windowTime windowLength =
        ChunkTimeSpanCount (windowTime, windowLength, ObservableSystem.DefaultTimeProvider)

    /// <summary>Chunks of the elements received during <paramref name="windowTime"/> milliseconds, measured by the default time provider.</summary>
    let inline Milliseconds windowTime = ChunkMilliseconds (windowTime, ObservableSystem.DefaultTimeProvider)

    /// <summary>
    /// Chunks of at most <paramref name="windowLength"/> elements received during <paramref name="windowTime"/> milliseconds,
    /// measured by the default time provider.
    /// </summary>
    let inline MillisecondsCount windowTime windowLength =
        ChunkMillisecondsCount (windowTime, windowLength, ObservableSystem.DefaultTimeProvider)

    /// <summary>
    /// Chunks that start with an element and end when the computation <paramref name="asyncWindow"/> returns for that element completes.
    /// The computation runs with a cancellation token that is cancelled when the chunked sequence completes or is disposed.
    /// </summary>
    let AsyncWindow (asyncWindow : 'T -> Async<unit>) =
        let asyncWindow element ct =
            Async.StartImmediateAsTask (asyncWindow element, ct) :> Task
            |> ValueTask
        ChunkAsyncWindow (asyncWindow, true)
