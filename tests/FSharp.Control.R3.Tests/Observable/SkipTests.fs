namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type SkipTests () =
    [<TestMethod>]
    member _.``skip should skip leading values`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let actual =
            source
            |> Observable.skip 2
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 3; 4 |], actual, "skip must ignore the configured number of leading values.")
