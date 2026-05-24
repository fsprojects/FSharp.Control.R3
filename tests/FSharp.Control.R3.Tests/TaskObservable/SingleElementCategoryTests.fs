namespace FSharp.Control.R3.Tests.TaskObservable

open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Task
open FSharp.Control.R3.Tests

[<TestClass>]
type SingleElementCategoryTests () =
    [<TestMethod>]
    member _.``firstAsync should return first value`` () : Task = task {
        let source = TestHelpers.createObservable [| 5; 6 |]
        let! actual =
            source
            |> Observable.firstAsync TestHelpers.cancellationToken
        Assert.AreEqual (5, actual, "Task firstAsync must return first emitted value.")
    }
