namespace FSharp.Control.R3.Tests

open System
open System.Threading.Tasks
open FSharp.Control.R3.Async
open Microsoft.VisualStudio.TestTools.UnitTesting
open Swensen.Unquote

[<TestClass>]
type ObservableTests () =

    [<TestMethod>]
    member _.``Test length`` () : Task =

        async {
            use r3Bus = new R3.Subject<int> ()

            r3Bus.OnNext 1

            let lengthObs = Observable.length r3Bus

            r3Bus.OnNext 2
            r3Bus.OnNext 3
            r3Bus.OnCompleted (R3.Result.Success)

            let! res = lengthObs

            Assert.AreEqual<int> (0, res)

        }
        |> Async.StartImmediateAsTask
        :> Task
