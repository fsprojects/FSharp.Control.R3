namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type ConversionCategoryTests () =
    [<TestMethod>]
    member _.``asObservable should keep source values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let! expected = source |> TestHelpers.toArrayTask
        let! actual = source |> Observable.asObservable |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual (expected, actual, "asObservable must preserve source values.")
    }

    [<TestMethod>]
    member _.``cast should convert values to target type`` () : Task = task {
        let source = TestHelpers.createObservable [| box 1; box 2 |]
        let! actual =
            source
            |> Observable.cast<obj, int>
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 1; 2 |], actual, "cast must convert boxed integers.")
    }

    [<TestMethod>]
    member _.``ofType should keep only requested runtime type`` () : Task = task {
        let source = TestHelpers.createObservable [| box 1; box "x"; box 2 |]
        let! actual =
            source
            |> Observable.ofType<obj, int>
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 1; 2 |], actual, "ofType must emit only values of requested type.")
    }
