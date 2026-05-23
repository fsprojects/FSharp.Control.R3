namespace FSharp.Control.R3.Tests.AsyncObservable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type OfAsyncTests () =
    [<TestMethod>]
    member _.``ofAsync should emit computation result`` () =
        let actual =
            Observable.ofAsync (async { return 7 })
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 7 |], actual, "ofAsync must emit the async computation result.")
