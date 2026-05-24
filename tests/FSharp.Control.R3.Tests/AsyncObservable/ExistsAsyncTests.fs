namespace FSharp.Control.R3.Tests.AsyncObservable

open System
open System.Threading.Tasks
open global.FSharp.Control
open Microsoft.VisualStudio.TestTools.UnitTesting
open global.R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type ExistsAsyncTests () =
    [<TestMethod>]
    member _.``existsAsync should detect available values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1 |]
        let! actual = source |> Observable.existsAsync
        Assert.IsTrue (actual, "existsAsync must return true for non-empty sequence.")
    }
