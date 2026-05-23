namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type WhereTests () =
    [<TestMethod>]
    member _.``where should keep matching values`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let actual =
            source
            |> Observable.where (fun x -> x > 2)
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 3; 4 |], actual, "where must keep values matching the predicate.")
