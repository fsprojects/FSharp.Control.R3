namespace FSharp.Control.R3.Tests.AsyncObservable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type TransformationCategoryTests () =
    [<TestMethod>]
    member _.``mapAsync should match direct SelectAwait`` () : Task = task {
        let options = ProcessingOptions.Default
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let expected =
            ObservableExtensions.SelectAwait (
                source,
                (fun x (_ : System.Threading.CancellationToken) -> ValueTask.FromResult (x + 1)),
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

    [<TestMethod>]
    member _.``iter should invoke action for each value`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let mutable sum = 0
        do! source |> Observable.iter (fun x -> sum <- sum + x)
        Assert.AreEqual (6, sum, "iter must invoke action for each emitted value.")
    }

    [<TestMethod>]
    member _.``iterAsync should await async action for each value`` () : Task = task {
        let options = ProcessingOptions.Default
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let mutable sum = 0
        do!
            source
            |> Observable.iterAsync options (fun x -> async { sum <- sum + x })
        Assert.AreEqual (6, sum, "iterAsync must apply asynchronous action to every value.")
    }

    [<TestMethod>]
    member _.``ofAsync should emit computation result`` () =
        let actual =
            Observable.ofAsync (async { return 7 })
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 7 |], actual, "ofAsync must emit the async computation result.")
