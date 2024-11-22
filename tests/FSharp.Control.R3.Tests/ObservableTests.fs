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


    [<TestMethod>]
    member _.``Test filter`` () =

        let mutable hasvisited = false
        use r3Bus = new R3.Subject<int> ()
        let interesting =
            r3Bus
            |> FSharp.Control.R3.Observable.filter (fun x -> x % 2 = 0)

        // No-one listens yet (vs R3.ReplaySubject<int>
        r3Bus.OnNext 2

        use subscription =
            R3.ObservableExtensions.SubscribeAwait (
                interesting,
                fun i cancellationToken ->
                    task {
                        // Listen events

                        hasvisited <- true

                        Assert.AreEqual<int> (4, i)

                        return ()
                    }
                    |> System.Threading.Tasks.ValueTask
            )

        // Publish some events, "4" should be heard
        [ 3..5 ] |> List.iter r3Bus.OnNext
        Assert.AreEqual<bool> (true, hasvisited)
