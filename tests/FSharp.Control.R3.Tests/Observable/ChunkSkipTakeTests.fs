namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type ChunkSkipTakeTests () =
    [<TestMethod>]
    member _.``chunkBySize should split by fixed size`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3; 4; 5 |]
        let! actual =
            source
            |> Observable.chunkBySize 2
            |> TestHelpers.toArrayTask
        let chunks = actual |> Array.map Seq.toArray
        Assert.AreEqual (3, chunks.Length, "chunkBySize should produce expected number of chunks.")
        CollectionAssert.AreEqual ([| 1; 2 |], chunks[0], "First chunk should contain first two items.")
        CollectionAssert.AreEqual ([| 3; 4 |], chunks[1], "Second chunk should contain next two items.")
        CollectionAssert.AreEqual ([| 5 |], chunks[2], "Third chunk should contain the remaining item.")
    }

    [<TestMethod>]
    member _.``chunkBy ChunkCount should match chunkBySize`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let! expectedChunks =
            source
            |> Observable.chunkBySize 2
            |> TestHelpers.toArrayTask
        let! actualChunks =
            source
            |> Observable.chunkBy (ChunkCount 2)
            |> TestHelpers.toArrayTask
        let expected = expectedChunks |> Array.map Seq.toArray
        let actual = actualChunks |> Array.map Seq.toArray
        Assert.AreEqual (expected.Length, actual.Length, "chunkBy ChunkCount must produce same chunk count as chunkBySize.")
        CollectionAssert.AreEqual (expected[0], actual[0], "First chunk must match chunkBySize output.")
        CollectionAssert.AreEqual (expected[1], actual[1], "Second chunk must match chunkBySize output.")
    }

    [<TestMethod>]
    member _.``skip should skip leading values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let! actual = source |> Observable.skip 2 |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 3; 4 |], actual, "skip must ignore the configured number of leading values.")
    }

    [<TestMethod>]
    member _.``take should keep leading values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let! actual = source |> Observable.take 2 |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 1; 2 |], actual, "take must emit only the configured number of leading values.")
    }
