namespace FSharp.Control.R3

open System
open System.Runtime.ExceptionServices
open System.Threading

/// <summary>
/// Tracks one iteration of an asynchronous action over an observable sequence: the
/// <see cref="M:FSharp.Control.R3.Task.Observable.iterAsync``1(System.Threading.CancellationToken,FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,Microsoft.FSharp.Core.FSharpFunc{``0,System.Threading.Tasks.Task{Microsoft.FSharp.Core.Unit}}},R3.Observable{``0})"/>
/// and <see cref="M:FSharp.Control.R3.Async.Observable.iterAsync``1(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{``0,Microsoft.FSharp.Control.FSharpAsync{Microsoft.FSharp.Core.Unit}},R3.Observable{``0})"/>
/// of both flavours.
/// <para>
/// The failures of the action never travel through R3, because R3 1.3.1 mishandles them in three ways. It attaches the terminal
/// operator to the mapped stage only after <see cref="M:R3.Observable`1.Subscribe(R3.Observer{`0})"/> returns, so over a synchronous
/// source the actions that are still queued keep running after a failure. It drops a failure that happens after the source completed.
/// And it swallows an <see cref="T:System.OperationCanceledException"/>, which stops the sequential modes for good without completing them.
/// </para>
/// <para>
/// Instead the guard records the first failure, skips the remaining actions and cancels
/// <see cref="P:FSharp.Control.R3.IterationGuard.StopToken"/>, which completes the iteration through
/// <see cref="M:R3.ObservableExtensions.TakeUntil``1(R3.Observable{``0},System.Threading.CancellationToken)"/>;
/// the iteration then fails with the recorded exception.
/// </para>
/// </summary>
[<Sealed>]
type internal IterationGuard (cancellationToken : CancellationToken) =

    // Never disposed: an action still running after the iteration completed may fail and cancel it late,
    // and a token source without a timer or linked tokens holds nothing that needs disposal
    let stop = new CancellationTokenSource ()

    [<DefaultValue>]
    val mutable private failure : exn | null

    /// Whether the remaining actions must be skipped because an action failed or the iteration was cancelled.
    member _.IsStopped =
        stop.IsCancellationRequested
        || cancellationToken.IsCancellationRequested

    /// Cancelled when an action fails, to complete the iteration.
    member _.StopToken = stop.Token

    /// Records the failure of an action and stops the iteration; only the first failure is kept.
    member this.Fail (error : exn) =
        match Interlocked.CompareExchange (&this.failure, error, null) with
        | null -> stop.Cancel ()
        | _ -> ()

    /// Raises the recorded failure with its original stack trace when an action failed.
    member this.ThrowIfFailed () =
        match Volatile.Read &this.failure with
        | null -> ()
        | error -> ExceptionDispatchInfo.Capture(error).Throw()
