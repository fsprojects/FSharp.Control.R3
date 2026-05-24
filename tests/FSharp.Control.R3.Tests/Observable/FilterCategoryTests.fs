namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type FilterCategoryTests () =
    [<TestMethod>]
    member _.``where should keep matching values`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let actual =
            source
            |> Observable.where (fun x -> x > 2)
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 3; 4 |], actual, "where must keep values matching the predicate.")

    [<TestMethod>]
    member _.``filter should keep matching values`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let actual =
            source
            |> Observable.filter (fun x -> x % 2 = 0)
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 2; 4 |], actual, "filter must keep only matching elements.")

    [<TestMethod>]
    member _.``choose should keep only Some values`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let actual =
            source
            |> FSharp.Control.R3.Observable.OptionExtensions.Observable.choose (fun x -> if x % 2 = 0 then Some (x * 10) else None)
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 20; 40 |], actual, "choose must emit only mapped values from Some results.")
