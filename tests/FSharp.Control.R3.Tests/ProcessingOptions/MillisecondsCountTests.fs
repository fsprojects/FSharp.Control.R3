namespace FSharp.Control.R3.Tests.ProcessingOptions

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Control.R3
open R3

[<TestClass>]
type MillisecondsCountTests () =
    [<TestMethod>]
    member _.``MillisecondsCount should use default time provider`` () =
        match ChunkConfiguration.MillisecondsCount 20 4 with
        | ChunkMillisecondsCount (windowTime, windowLength, provider) ->
            Assert.AreEqual (20, windowTime, "MillisecondsCount helper must keep provided window time.")
            Assert.AreEqual (4, windowLength, "MillisecondsCount helper must keep provided window length.")
            Assert.AreSame (ObservableSystem.DefaultTimeProvider, provider, "MillisecondsCount helper must use default time provider.")
        | _ -> Assert.Fail ("MillisecondsCount helper must create ChunkMillisecondsCount configuration.")
