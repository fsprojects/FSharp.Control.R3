namespace FSharp.Control.R3

open R3

type AwaitOperationConfiguration =
    /// <summary>All values are queued, and the next value waits for the completion of the asynchronous method.</summary>
    | Sequential
    /// <summary>Drop new value when async operation is running.</summary>
    | Drop
    /// <summary>If the previous asynchronous method is running, it is cancelled and the next asynchronous method is executed.</summary>
    | Switch
    /// <summary>All values are sent immediately to the asynchronous method.</summary>
    | Parallel of
        /// If set to -1, there is no limit.
        MaxConcurrent : int
    /// <summary>All values are sent immediately to the asynchronous method, but the results are queued and passed to the next operator in order.</summary>
    | SequentialParallel of
        /// If set to -1, there is no limit.
        MaxConcurrent : int
    /// <summary>Send the first value and the last value while the asynchronous method is running.</summary>
    | ThrottleFirstLast

type ProcessingOptions = {
    AwaitOperationConfiguration : AwaitOperationConfiguration
    ConfigureAwait : bool
    CancelOnCompleted : bool
} with

    static let ``default`` = {
        AwaitOperationConfiguration = AwaitOperationConfiguration.Sequential
        ConfigureAwait = true
        CancelOnCompleted = false
    }

    static let ``parallel`` = {
        AwaitOperationConfiguration = AwaitOperationConfiguration.Parallel -1
        ConfigureAwait = true
        CancelOnCompleted = false
    }

    static member Default = ``default``
    static member Parallel = ``parallel``

    member this.MaxConcurrent =
        match this.AwaitOperationConfiguration with
        | AwaitOperationConfiguration.Sequential -> -1
        | AwaitOperationConfiguration.Drop -> -1
        | AwaitOperationConfiguration.Switch -> -1
        | AwaitOperationConfiguration.Parallel maxConcurrent -> maxConcurrent
        | AwaitOperationConfiguration.SequentialParallel maxConcurrent -> maxConcurrent
        | AwaitOperationConfiguration.ThrottleFirstLast -> -1

    member this.AwaitOperation =
        match this.AwaitOperationConfiguration with
        | AwaitOperationConfiguration.Sequential -> AwaitOperation.Sequential
        | AwaitOperationConfiguration.Drop -> AwaitOperation.Drop
        | AwaitOperationConfiguration.Switch -> AwaitOperation.Switch
        | AwaitOperationConfiguration.Parallel _ -> AwaitOperation.Parallel
        | AwaitOperationConfiguration.SequentialParallel _ -> AwaitOperation.SequentialParallel
        | AwaitOperationConfiguration.ThrottleFirstLast -> AwaitOperation.ThrottleFirstLast
