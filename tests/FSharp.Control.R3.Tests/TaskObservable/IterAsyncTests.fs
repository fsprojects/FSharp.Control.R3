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
type IterAsyncTests () =
    [<TestMethod>]
    member _.``iterAsync should await action for each value`` () : Task = task {
        let options = ProcessingOptions.Default
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let mutable sum = 0
        do!
            source
            |> Observable.iterAsync TestHelpers.cancellationToken options (fun (_ : CancellationToken) x -> task { sum <- sum + x })
        Assert.AreEqual (6, sum, "Task iterAsync must run action for each emitted value.")
    }
