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
type ToArrayTests () =
    [<TestMethod>]
    member _.``toArray should return all values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let! actual = source |> Observable.toArray
        CollectionAssert.AreEqual ([| 1; 2; 3 |], actual, "toArray must return all source values.")
    }
