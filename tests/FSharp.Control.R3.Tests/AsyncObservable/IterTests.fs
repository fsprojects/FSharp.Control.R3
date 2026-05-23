namespace FSharp.Control.R3.Tests.AsyncObservable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type IterTests () =
    [<TestMethod>]
    member _.``iter should invoke action for each value`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let mutable sum = 0
        do!
            source
            |> Observable.iter (fun x -> sum <- sum + x)
            |> Async.StartAsTask
            :> Task
        Assert.AreEqual (6, sum, "iter must invoke action for each emitted value.")
    }
