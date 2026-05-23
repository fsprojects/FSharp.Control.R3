namespace FSharp.Control.R3.Tests.AsyncObservable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type ToListTests () =
    [<TestMethod>]
    member _.``toList should return all values as list`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let! actual = source |> Observable.toList |> Async.StartAsTask
        CollectionAssert.AreEqual ([| 1; 2; 3 |], actual |> List.toArray, "toList must return all source values in order.")
    }
