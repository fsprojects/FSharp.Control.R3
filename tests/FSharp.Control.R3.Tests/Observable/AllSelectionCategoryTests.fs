namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type AllSelectionCategoryTests () =
    [<TestMethod>]
    member _.``where should keep matching values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let! actual =
            source
            |> Observable.where (fun x -> x > 2)
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 3; 4 |], actual, "where must keep values matching the predicate.")
    }

    [<TestMethod>]
    member _.``filter should keep matching values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let! actual =
            source
            |> Observable.filter (fun x -> x % 2 = 0)
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 2; 4 |], actual, "filter must keep only matching elements.")
    }

    [<TestMethod>]
    member _.``choose should keep only Some values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let! actual =
            source
            |> FSharp.Control.R3.Observable.OptionExtensions.Observable.choose (fun x -> if x % 2 = 0 then Some (x * 10) else None)
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 20; 40 |], actual, "choose must emit only mapped values from Some results.")
    }

    [<TestMethod>]
    member _.``distinct should remove duplicate values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 1; 2; 2; 3 |]
        let! actual = source |> Observable.distinct |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 1; 2; 3 |], actual, "distinct must emit unique values in encounter order.")
    }

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
