namespace FSharp.Control.R3.Tests.ProcessingOptions

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Control.R3

[<TestClass>]
type AsyncWindowTests () =
    [<TestMethod>]
    member _.``AsyncWindow should wrap async callback`` () : Task = task {
        let mutable observed = 0
        let configuration = ChunkConfiguration.AsyncWindow (fun value -> async { observed <- value })
        match configuration with
        | ChunkAsyncWindow (callback, configureAwait) ->
            do! callback.Invoke (11, CancellationToken.None)
            Assert.AreEqual (11, observed, "AsyncWindow helper must invoke wrapped callback with value.")
            Assert.IsTrue (configureAwait, "AsyncWindow helper must set configureAwait to true.")
        | _ -> Assert.Fail ("AsyncWindow helper must create ChunkAsyncWindow configuration.")
    }
