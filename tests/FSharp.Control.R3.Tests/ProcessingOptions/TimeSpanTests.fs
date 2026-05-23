namespace FSharp.Control.R3.Tests.ProcessingOptions

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Control.R3
open R3

[<TestClass>]
type TimeSpanTests () =
    [<TestMethod>]
    member _.``TimeSpan should use default time provider`` () =
        match ChunkConfiguration.TimeSpan (TimeSpan.FromMilliseconds 10.) with
        | ChunkTimeSpan (windowTime, provider) ->
            Assert.AreEqual (TimeSpan.FromMilliseconds 10., windowTime, "TimeSpan helper must keep provided window time.")
            Assert.AreSame (ObservableSystem.DefaultTimeProvider, provider, "TimeSpan helper must use default time provider.")
        | _ -> Assert.Fail ("TimeSpan helper must create ChunkTimeSpan configuration.")
