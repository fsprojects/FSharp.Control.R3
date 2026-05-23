namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type BindTests () =
    [<TestMethod>]
    member _.``bind should match SelectMany behavior`` () =
        let source = TestHelpers.createObservable [| 1; 2 |]
        let expected =
            ObservableExtensions.SelectMany (source, fun x -> Observable.Return (x * 10))
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        let actual =
            source
            |> Observable.bind (fun x -> Observable.singleton (x * 10))
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        CollectionAssert.AreEqual (expected, actual, "bind must match SelectMany behavior.")
