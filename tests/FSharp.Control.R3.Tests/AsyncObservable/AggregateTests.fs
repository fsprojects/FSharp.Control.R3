namespace FSharp.Control.R3.Tests.AsyncObservable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type AggregateTests () =
    [<TestMethod>]
    member _.``aggregate should match direct AggregateAsync`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let expected =
            ObservableExtensions.AggregateAsync (source, 0, (fun acc x -> acc + x), TestHelpers.cancellationToken)
        let! actual =
            source
            |> Observable.aggregate 0 (fun acc x -> acc + x)
            |> Async.StartAsTask
        Assert.AreEqual (expected.Result, actual, "Async aggregate must match direct AggregateAsync result.")
    }
