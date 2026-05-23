namespace FSharp.Control.R3.Tests

open System.Threading
open System.Threading.Tasks
open FSharp.Control.R3
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3

module ObservableModule = FSharp.Control.R3.Observable
module AsyncObservable = FSharp.Control.R3.Async.Observable
module TaskObservable = FSharp.Control.R3.Task.Observable

[<TestClass>]
type IntegrationParityTests () =

    static member private SourceValues = [| 1; 2; 3; 4; 5; 6 |]

    static member private CreateSourceObservable () = R3.Observable.ToObservable IntegrationParityTests.SourceValues

    [<TestMethod>]
    member _.``Observable wrappers should match direct R3 pipeline results`` () : Task = task {
        let expectedPipeline =
            IntegrationParityTests.CreateSourceObservable ()
            |> _.Where(fun x -> x % 2 = 0)
            |> _.Select(fun x -> x * 10)
            |> _.Skip(1)
            |> _.Take(1)

        let actualPipeline =
            IntegrationParityTests.CreateSourceObservable ()
            |> ObservableModule.filter (fun x -> x % 2 = 0)
            |> ObservableModule.map (fun x -> x * 10)
            |> ObservableModule.skip 1
            |> ObservableModule.take 1

        let! expected = R3.ObservableExtensions.ToArrayAsync expectedPipeline
        let! actual = R3.ObservableExtensions.ToArrayAsync actualPipeline

        CollectionAssert.AreEqual (expected, actual, "Wrapper pipeline result must match direct R3 pipeline result.")
    }

    [<TestMethod>]
    member _.``Async Observable toArray should match direct R3 ToArrayAsync`` () : Task = task {
        let! expected = R3.ObservableExtensions.ToArrayAsync (IntegrationParityTests.CreateSourceObservable ())

        let! actual =
            IntegrationParityTests.CreateSourceObservable ()
            |> AsyncObservable.toArray
            |> Async.StartAsTask

        CollectionAssert.AreEqual (expected, actual, "Async wrapper toArray must match direct R3 ToArrayAsync.")
    }

    [<TestMethod>]
    member _.``Task Observable mapAsync should match direct R3 SelectAwait`` () : Task = task {
        let options = ProcessingOptions.Default

        let addOne x = x + 1

        let expectedPipeline =
            R3.ObservableExtensions.SelectAwait (
                IntegrationParityTests.CreateSourceObservable (),
                (fun x (_ : CancellationToken) -> ValueTask.FromResult (addOne x)),
                options.AwaitOperation,
                options.ConfigureAwait,
                options.CancelOnCompleted,
                options.MaxConcurrent
            )

        let actualPipeline =
            IntegrationParityTests.CreateSourceObservable ()
            |> TaskObservable.mapAsync options (fun (_ : CancellationToken) x -> Task.FromResult (addOne x))

        let! expected = R3.ObservableExtensions.ToArrayAsync expectedPipeline
        let! actual = R3.ObservableExtensions.ToArrayAsync actualPipeline

        CollectionAssert.AreEqual (expected, actual, "Task wrapper mapAsync must match direct R3 SelectAwait pipeline.")
    }
