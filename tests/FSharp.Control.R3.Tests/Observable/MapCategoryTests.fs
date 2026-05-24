namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type MapCategoryTests () =
    [<TestMethod>]
    member _.``map should transform each value`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let! actual =
            source
            |> Observable.map (fun x -> x * 10)
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 10; 20; 30 |], actual, "map must transform each emitted value.")
    }

    [<TestMethod>]
    member _.``bind should match SelectMany behavior`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2 |]
        let! expected =
            ObservableExtensions.SelectMany (source, fun x -> Observable.Return (x * 10))
            |> TestHelpers.toArrayTask
        let! actual =
            source
            |> Observable.bind (fun x -> Observable.singleton (x * 10))
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual (expected, actual, "bind must match SelectMany behavior.")
    }
