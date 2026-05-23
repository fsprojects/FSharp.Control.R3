namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type MergeTests () =
    [<TestMethod>]
    member _.``merge should match direct R3 merge`` () =
        let source1 = TestHelpers.createObservable [| 1; 2 |]
        let source2 = TestHelpers.createObservable [| 10; 20 |]
        let expected =
            ObservableExtensions.Merge (source1, source2)
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        let actual =
            Observable.merge (source1, source2)
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual (expected, actual, "merge wrapper must match direct R3 merge output.")
