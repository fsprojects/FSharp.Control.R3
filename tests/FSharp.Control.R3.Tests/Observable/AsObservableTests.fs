namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type AsObservableTests () =
    [<TestMethod>]
    member _.``asObservable should keep source values`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let expected = source |> TestHelpers.toArrayTask |> TestHelpers.waitTask
        let actual =
            source
            |> Observable.asObservable
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual (expected, actual, "asObservable must preserve source values.")
