namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type ChunkByTests () =
    [<TestMethod>]
    member _.``chunkBy ChunkCount should match chunkBySize`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let expected =
            source
            |> Observable.chunkBySize 2
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
            |> Array.map Seq.toArray
        let actual =
            source
            |> Observable.chunkBy (ChunkCount 2)
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
            |> Array.map Seq.toArray
        Assert.AreEqual (expected.Length, actual.Length, "chunkBy ChunkCount must produce same chunk count as chunkBySize.")
        CollectionAssert.AreEqual (expected[0], actual[0], "First chunk must match chunkBySize output.")
        CollectionAssert.AreEqual (expected[1], actual[1], "Second chunk must match chunkBySize output.")
