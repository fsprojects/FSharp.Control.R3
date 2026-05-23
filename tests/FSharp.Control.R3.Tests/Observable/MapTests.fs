namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type MapTests () =
    [<TestMethod>]
    member _.``map should transform each value`` () =
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let actual =
            source
            |> Observable.map (fun x -> x * 10)
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual ([| 10; 20; 30 |], actual, "map must transform each emitted value.")
