namespace FSharp.Control.R3.Tests.TaskObservable

open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Task
open FSharp.Control.R3.Tests

[<TestClass>]
type AllSelectionCategoryTests () =
    [<TestMethod>]
    member _.``all should return true when all values match`` () : Task = task {
        let source = TestHelpers.createObservable [| 2; 4; 6 |]
        let! actual =
            source
            |> Observable.all TestHelpers.cancellationToken (fun x -> x % 2 = 0)
        Assert.IsTrue (actual, "Task all must return true when all elements satisfy predicate.")
    }

    [<TestMethod>]
    member _.``existsAsync should detect available values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1 |]
        let! actual =
            source
            |> Observable.existsAsync TestHelpers.cancellationToken
        Assert.IsTrue (actual, "Task existsAsync must return true for non-empty source.")
    }
