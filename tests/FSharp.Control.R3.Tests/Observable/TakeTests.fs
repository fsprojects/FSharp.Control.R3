namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type TakeTests () =
    [<TestMethod>]
    member _.``take should keep leading values`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let actual =
            source
            |> Observable.take 2
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 1; 2 |], actual, "take must emit only the configured number of leading values.")
