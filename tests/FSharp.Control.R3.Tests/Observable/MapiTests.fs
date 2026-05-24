namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type MapiTests () =
    [<TestMethod>]
    member _.``mapi should pass index as first argument`` () : Task = task {
        let source = TestHelpers.createObservable [| 4; 5; 6 |]
        let! expected =
            ObservableExtensions.Select (source, fun value index -> (index * 10) + value)
            |> TestHelpers.toArrayTask
        let! actual =
            source
            |> Observable.mapi (fun index value -> (index * 10) + value)
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual (expected, actual, "mapi must pass index first and value second.")
    }
