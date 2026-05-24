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
type IterAsyncTests () =
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
