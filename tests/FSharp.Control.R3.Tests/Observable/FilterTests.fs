namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type FilterTests () =
    [<TestMethod>]
    member _.``filter should keep matching values`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let actual =
            source
            |> Observable.filter (fun x -> x % 2 = 0)
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 2; 4 |], actual, "filter must keep only matching elements.")
