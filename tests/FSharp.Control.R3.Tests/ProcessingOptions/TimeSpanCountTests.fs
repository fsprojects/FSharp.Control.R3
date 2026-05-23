namespace FSharp.Control.R3.Tests.ProcessingOptions

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Control.R3
open R3

[<TestClass>]
type TimeSpanCountTests () =
    [<TestMethod>]
    member _.``TimeSpanCount should use default time provider`` () =
        match ChunkConfiguration.TimeSpanCount (TimeSpan.FromMilliseconds 10.) 3 with
        | ChunkTimeSpanCount (windowTime, windowLength, provider) ->
            Assert.AreEqual (TimeSpan.FromMilliseconds 10., windowTime, "TimeSpanCount helper must keep provided window time.")
            Assert.AreEqual (3, windowLength, "TimeSpanCount helper must keep provided window length.")
            Assert.AreSame (ObservableSystem.DefaultTimeProvider, provider, "TimeSpanCount helper must use default time provider.")
        | _ -> Assert.Fail ("TimeSpanCount helper must create ChunkTimeSpanCount configuration.")
