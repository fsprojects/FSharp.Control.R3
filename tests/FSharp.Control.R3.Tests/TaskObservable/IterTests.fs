namespace FSharp.Control.R3.Tests.TaskObservable

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Task
open FSharp.Control.R3.Tests

[<TestClass>]
type IterTests () =
    [<TestMethod>]
    member _.``iter should invoke action for each value`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let mutable sum = 0
        do!
            source
            |> Observable.iter TestHelpers.cancellationToken (fun x -> sum <- sum + x)
        Assert.AreEqual (6, sum, "Task iter must invoke action for each value.")
    }
