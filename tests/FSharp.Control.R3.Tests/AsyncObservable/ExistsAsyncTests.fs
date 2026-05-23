namespace FSharp.Control.R3.Tests.AsyncObservable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type ExistsAsyncTests () =
    [<TestMethod>]
    member _.``existsAsync should detect available values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1 |]
        let! actual = source |> Observable.existsAsync |> Async.StartAsTask
        Assert.IsTrue (actual, "existsAsync must return true for non-empty sequence.")
    }
