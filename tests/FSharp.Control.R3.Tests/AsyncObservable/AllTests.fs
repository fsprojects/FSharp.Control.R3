namespace FSharp.Control.R3.Tests.AsyncObservable

open System
open System.Threading.Tasks
open global.FSharp.Control
open Microsoft.VisualStudio.TestTools.UnitTesting
open global.R3
open FSharp.Control.R3
open FSharp.Control.R3.Async
open FSharp.Control.R3.Tests

[<TestClass>]
type AllTests () =
    [<TestMethod>]
    member _.``all should match direct AllAsync`` () : Task = task {
        let source = TestHelpers.createObservable [| 2; 4; 6 |]
        let expected =
            ObservableExtensions.AllAsync (source, (fun x -> x % 2 = 0), TestHelpers.cancellationToken)
        let! expectedValue = expected
        let! actual = source |> Observable.all (fun x -> x % 2 = 0)
        Assert.AreEqual (expectedValue, actual, "Async all must match direct AllAsync result.")
    }
