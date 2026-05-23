namespace FSharp.Control.R3.Tests

open System.Threading
open System.Threading.Tasks
open FSharp.Control.R3
open Microsoft.VisualStudio.TestTools.UnitTesting

[<TestClass>]
type IntegrationParityTests () =

    static member private SourceValues = [ 1; 2; 3; 4; 5; 6 ]

    static member private CreateSource () = R3.Observable.ToObservable IntegrationParityTests.SourceValues

    [<TestMethod>]
    member _.``Observable wrappers should match direct R3 pipeline results`` () : Task = task {
        let expectedPipeline =
            IntegrationParityTests.CreateSource ()
            |> fun source -> R3.ObservableExtensions.Where (source, fun x -> x % 2 = 0)
            |> fun source -> R3.ObservableExtensions.Select (source, fun x -> x * 10)
            |> fun source -> R3.ObservableExtensions.Skip (source, 1)
            |> fun source -> R3.ObservableExtensions.Take (source, 1)

        let actualPipeline =
            IntegrationParityTests.CreateSource ()
            |> FSharp.Control.R3.Observable.filter (fun x -> x % 2 = 0)
            |> FSharp.Control.R3.Observable.map (fun x -> x * 10)
            |> FSharp.Control.R3.Observable.skip 1
            |> FSharp.Control.R3.Observable.take 1

        let! expected = R3.ObservableExtensions.ToArrayAsync expectedPipeline
        let! actual = R3.ObservableExtensions.ToArrayAsync actualPipeline

        CollectionAssert.AreEqual (expected, actual, "Wrapper pipeline result must match direct R3 pipeline result.")
    }

    [<TestMethod>]
    member _.``Async Observable toArray should match direct R3 ToArrayAsync`` () : Task = task {
        let! expected = R3.ObservableExtensions.ToArrayAsync (IntegrationParityTests.CreateSource ())

        let! actual =
            IntegrationParityTests.CreateSource ()
            |> FSharp.Control.R3.Async.Observable.toArray
            |> Async.StartImmediateAsTask

        CollectionAssert.AreEqual (expected, actual, "Async wrapper toArray must match direct R3 ToArrayAsync.")
    }

    [<TestMethod>]
    member _.``Task Observable mapAsync should match direct R3 SelectAwait`` () : Task = task {
        let options = ProcessingOptions.Default

        let selector x (ct : CancellationToken) =
            ValueTask<int> (Task.FromResult (x + (if ct.IsCancellationRequested then 0 else 1)))

        let expectedPipeline =
            R3.ObservableExtensions.SelectAwait (
                IntegrationParityTests.CreateSource (),
                selector,
                options.AwaitOperation,
                options.ConfigureAwait,
                options.CancelOnCompleted,
                options.MaxConcurrent
            )

        let actualPipeline =
            IntegrationParityTests.CreateSource ()
            |> FSharp.Control.R3.Task.Observable.mapAsync
                options
                (fun (ct : CancellationToken) x -> Task.FromResult (x + (if ct.IsCancellationRequested then 0 else 1)))

        let! expected = R3.ObservableExtensions.ToArrayAsync expectedPipeline
        let! actual = R3.ObservableExtensions.ToArrayAsync actualPipeline

        CollectionAssert.AreEqual (expected, actual, "Task wrapper mapAsync must match direct R3 SelectAwait pipeline.")
    }
