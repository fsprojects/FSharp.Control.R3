namespace FSharp.Control.R3.Tests.TaskObservable

open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Task
open FSharp.Control.R3.Tests

[<TestClass>]
type TransformationCategoryTests () =
    [<TestMethod>]
    member _.``mapAsync should transform each value`` () : Task = task {
        let options = ProcessingOptions.Default
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let! actual =
            source
            |> Observable.mapAsync options (fun (_ : CancellationToken) x -> Task.FromResult (x + 1))
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 2; 3; 4 |], actual, "Task mapAsync must transform each source value.")
    }

    [<TestMethod>]
    member _.``iter should invoke action for each value`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let mutable sum = 0
        do!
            source
            |> Observable.iter TestHelpers.cancellationToken (fun x -> sum <- sum + x)
        Assert.AreEqual (6, sum, "Task iter must invoke action for each value.")
    }

    [<TestMethod>]
    member _.``iterAsync should await action for each value`` () : Task = task {
        let options = ProcessingOptions.Default
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let mutable sum = 0
        do!
            source
            |> Observable.iterAsync TestHelpers.cancellationToken options (fun (_ : CancellationToken) x -> task { sum <- sum + x })
        Assert.AreEqual (6, sum, "Task iterAsync must run action for each emitted value.")
    }
