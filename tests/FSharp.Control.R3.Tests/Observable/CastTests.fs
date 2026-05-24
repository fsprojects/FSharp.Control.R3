namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type CastTests () =
    [<TestMethod>]
    member _.``cast should convert values to target type`` () : Task = task {
        let source = TestHelpers.createObservable [| box 1; box 2 |]
        let! actual =
            source
            |> Observable.cast<obj, int>
            |> TestHelpers.toArrayTask
        CollectionAssert.AreEqual ([| 1; 2 |], actual, "cast must convert boxed integers.")
    }
