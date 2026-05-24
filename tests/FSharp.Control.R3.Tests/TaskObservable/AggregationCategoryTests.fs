namespace FSharp.Control.R3.Tests.TaskObservable

open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Task
open FSharp.Control.R3.Tests

[<TestClass>]
type AggregationCategoryTests () =
    [<TestMethod>]
    member _.``aggregate should match direct AggregateAsync`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let! actual =
            source
            |> Observable.aggregate TestHelpers.cancellationToken 0 (fun acc x -> acc + x)
        Assert.AreEqual (6, actual, "Task aggregate must return aggregated sum.")
    }

    [<TestMethod>]
    member _.``length should count values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let! actual = source |> Observable.length TestHelpers.cancellationToken
        Assert.AreEqual (4, actual, "Task length must return source value count.")
    }
