namespace FSharp.Control.R3.Tests.Observable

open System
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests

[<TestClass>]
type CatchTests () =
    [<TestMethod>]
    member _.``catch should continue with fallback observable`` () : Task = task {
        use subject = new Subject<int> ()
        let recovered =
            subject
            |> Observable.catch (fun (_ : exn) -> Observable.singleton 99)
        let pending = TestHelpers.toArrayTask recovered
        subject.OnNext 1
        subject.OnCompleted (Result.Failure (Exception ("boom")))
        let! actual = pending
        CollectionAssert.AreEqual ([| 1; 99 |], actual, "catch must append fallback values after source error.")
    }
