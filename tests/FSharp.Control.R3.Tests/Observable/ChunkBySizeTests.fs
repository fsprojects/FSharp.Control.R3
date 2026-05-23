namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type ChunkBySizeTests () =
    [<TestMethod>]
    member _.``chunkBySize should split by fixed size`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3; 4; 5 |]
        let actual =
            source
            |> Observable.chunkBySize 2
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
            |> Array.map Seq.toArray
        Assert.AreEqual (3, actual.Length, "chunkBySize should produce expected number of chunks.")
        CollectionAssert.AreEqual ([| 1; 2 |], actual[0], "First chunk should contain first two items.")
        CollectionAssert.AreEqual ([| 3; 4 |], actual[1], "Second chunk should contain next two items.")
        CollectionAssert.AreEqual ([| 5 |], actual[2], "Third chunk should contain the remaining item.")
