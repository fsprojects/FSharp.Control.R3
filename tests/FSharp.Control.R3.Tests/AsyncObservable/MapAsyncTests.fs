namespace FSharp.Control.R3.Tests.AsyncObservable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type MapAsyncTests () =
    [<TestMethod>]
    member _.``mapAsync should match direct SelectAwait`` () : Task = task {
        let options = ProcessingOptions.Default
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let expected =
            ObservableExtensions.SelectAwait (
                source,
                (fun x (_ : Threading.CancellationToken) -> ValueTask.FromResult (x + 1)),
                options.AwaitOperation,
                options.ConfigureAwait,
                options.CancelOnCompleted,
                options.MaxConcurrent
            )
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask

        let actual =
            source
            |> Observable.mapAsync options (fun x -> async { return x + 1 })
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask

        CollectionAssert.AreEqual (expected, actual, "Async mapAsync must match direct SelectAwait behavior.")
    }
