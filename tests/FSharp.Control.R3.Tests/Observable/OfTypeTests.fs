namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type OfTypeTests () =
    [<TestMethod>]
    member _.``ofType should keep only requested runtime type`` () =
        let source = TestHelpers.createObservable [| box 1; box "x"; box 2 |]
        let actual =
            source
            |> Observable.ofType<obj, int>
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 1; 2 |], actual, "ofType must emit only values of requested type.")
