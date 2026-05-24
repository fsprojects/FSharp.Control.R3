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
type FirstAsyncTests () =
    [<TestMethod>]
    member _.``firstAsync should return first value`` () : Task = task {
        let source = TestHelpers.createObservable [| 9; 8 |]
        let! actual = source |> Observable.firstAsync
        Assert.AreEqual (9, actual, "firstAsync must return the first emitted value.")
    }
