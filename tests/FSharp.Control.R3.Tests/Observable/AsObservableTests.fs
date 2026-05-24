namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type AsObservableTests () =
    [<TestMethod>]
    member _.``asObservable should keep source values`` () : Task = task {
        let source = TestHelpers.createObservable [| 1; 2; 3 |]
        let! expected = source |> TestHelpers.toArrayTask
        let! actual = source |> Observable.asObservable |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual (expected, actual, "asObservable must preserve source values.")
    }
