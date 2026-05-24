namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type SingleElementCategoryTests () =
    [<TestMethod>]
    member _.``singleton should emit one value`` () : Task = task {
        let! actual = Observable.singleton 42 |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 42 |], actual, "singleton must emit exactly one value.")
    }

    [<TestMethod>]
    member _.``empty should emit no values`` () : Task = task {
        let! actual = Observable.empty () |> TestHelpers.toArrayTask
        Assert.AreEqual (0, actual.Length, "empty must complete without values.")
    }
