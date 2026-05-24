namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type AggregationCategoryTests () =
    [<TestMethod>]
    member _.``concat should append second sequence`` () : Task = task {
        let first = TestHelpers.createObservable [| 1; 2 |]
        let second = TestHelpers.createObservable [| 3; 4 |]
        let! actual = Observable.concat first second |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 1; 2; 3; 4 |], actual, "concat must append second observable after first completes.")
    }

    [<TestMethod>]
    member _.``merge should match direct R3 merge`` () : Task = task {
        let source1 = TestHelpers.createObservable [| 1; 2 |]
        let source2 = TestHelpers.createObservable [| 10; 20 |]
        let! expected =
            ObservableExtensions.Merge (source1, source2)
            |> TestHelpers.toArrayTask
        let! actual =
            Observable.merge (source1, source2)
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual (expected, actual, "merge wrapper must match direct R3 merge output.")
    }

    [<TestMethod>]
    member _.``catch should continue with fallback observable`` () : Task = task {
        use subject = new Subject<int> ()
        let recovered =
            subject
            |> Observable.catch (fun (_ : exn) -> Observable.singleton 99)
        let pending = TestHelpers.toArrayTask recovered
        subject.OnNext 1
        subject.OnCompleted (Result.Failure (Exception ("boom")))
        let! actual = pending
        CollectionAssert.AreEqual ([| 1; 99 |], actual, "catch must append fallback values after source error.")
    }
