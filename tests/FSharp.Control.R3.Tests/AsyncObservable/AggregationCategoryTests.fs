namespace FSharp.Control.R3.Tests.AsyncObservable

open System.Threading.Tasks
open global.FSharp.Control
open Microsoft.VisualStudio.TestTools.UnitTesting
open global.R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type AggregationCategoryTests () =
    [<TestMethod>]
    member _.``aggregate should match direct AggregateAsync`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let expected =
            ObservableExtensions.AggregateAsync (source, 0, (fun acc x -> acc + x), TestHelpers.cancellationToken)
        let! expectedValue = expected
        let! actual = source |> Observable.aggregate 0 (fun acc x -> acc + x)
        Assert.AreEqual (expectedValue, actual, "Async aggregate must match direct AggregateAsync result.")
    }

    [<TestMethod>]
    member _.``length should count values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3; 4 |]
        let! actual = source |> Observable.length
        Assert.AreEqual (4, actual, "length must return emitted value count.")
    }
