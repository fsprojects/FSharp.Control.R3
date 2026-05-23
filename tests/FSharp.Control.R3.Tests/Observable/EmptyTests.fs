namespace FSharp.Control.R3.Tests.Observable

open System
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type EmptyTests () =
    [<TestMethod>]
    member _.``empty should emit no values`` () =
        let actual =
            Observable.empty ()
            |> TestHelpers.toArrayTask
            |> TestHelpers.waitTask
        Assert.AreEqual (0, actual.Length, "empty must complete without values.")
