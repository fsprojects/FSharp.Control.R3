namespace FSharp.Control.R3.Tests

open System
open System.Threading.Tasks
open FSharp.Control.R3.Async
open FSharp.Control.R3.Observable.Builders
open Microsoft.VisualStudio.TestTools.UnitTesting
open Swensen.Unquote

[<TestClass>]
type BuilderTests () =

    [<TestMethod>]
    member _.``Test builder rxquery`` () =

        let mutable hasvisited = false
        use r3Bus = new R3.Subject<int> ()

        let interesting = rxquery {
            for i in r3Bus do
                where (i % 2 = 0)
                select i

        //      Same as:
        //      if i % 2 = 0 then
        //          yield i

        }

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
        // Note: Query will not be awaited, that's why delay.
        System.Threading.Thread.Sleep 300
        Assert.AreEqual<bool> (true, hasvisited)
