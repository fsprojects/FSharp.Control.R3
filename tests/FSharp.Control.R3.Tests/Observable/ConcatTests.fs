namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type ConcatTests () =
    [<TestMethod>]
    member _.``concat should append second sequence`` () =
        let first = TestHelpers.createObservable [| 1; 2 |]
        let second = TestHelpers.createObservable [| 3; 4 |]
        let actual =
            Observable.concat first second
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 1; 2; 3; 4 |], actual, "concat must append second observable after first completes.")
