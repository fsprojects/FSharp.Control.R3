namespace FSharp.Control.R3.Tests.TaskObservable

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Task
open FSharp.Control.R3.Tests

[<TestClass>]
type ExistsAsyncTests () =
    [<TestMethod>]
    member _.``existsAsync should detect available values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1 |]
        let! actual =
            source
            |> Observable.existsAsync TestHelpers.cancellationToken
        Assert.IsTrue (actual, "Task existsAsync must return true for non-empty source.")
    }
