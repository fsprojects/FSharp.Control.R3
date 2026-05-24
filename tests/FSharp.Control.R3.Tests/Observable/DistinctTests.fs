namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type DistinctTests () =
    [<TestMethod>]
    member _.``distinct should remove duplicate values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 1; 2; 2; 3 |]
        let! actual = source |> Observable.distinct |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 1; 2; 3 |], actual, "distinct must emit unique values in encounter order.")
    }
