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
type MapAsyncTests () =
    [<TestMethod>]
    member _.``mapAsync should transform each value`` () =
        let options = ProcessingOptions.Default
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let actual =
            source
            |> Observable.mapAsync options (fun (_ : CancellationToken) x -> Task.FromResult (x + 1))
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 2; 3; 4 |], actual, "Task mapAsync must transform each source value.")
