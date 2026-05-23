namespace FSharp.Control.R3.Tests.ProcessingOptions

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Control.R3
open R3

[<TestClass>]
type MillisecondsTests () =
    [<TestMethod>]
    member _.``Milliseconds should use default time provider`` () =
        match ChunkConfiguration.Milliseconds 15 with
        | ChunkMilliseconds (windowTime, provider) ->
            Assert.AreEqual (15, windowTime, "Milliseconds helper must keep provided value.")
            Assert.AreSame (ObservableSystem.DefaultTimeProvider, provider, "Milliseconds helper must use default time provider.")
        | _ -> Assert.Fail ("Milliseconds helper must create ChunkMilliseconds configuration.")
