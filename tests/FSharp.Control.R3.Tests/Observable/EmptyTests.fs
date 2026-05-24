namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type EmptyTests () =
    [<TestMethod>]
    member _.``empty should emit no values`` () : Task = task {
        let! actual = Observable.empty () |> TestHelpers.toArrayTask
        Assert.AreEqual (0, actual.Length, "empty must complete without values.")
    }
