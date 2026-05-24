namespace FSharp.Control.R3.Tests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Control.R3
open R3

[<TestClass>]
type ProcessingOptionsTests () =
    [<TestMethod>]
    member _.``TimeSpan should use default time provider`` () =
        match ChunkConfiguration.TimeSpan (TimeSpan.FromMilliseconds 10.) with
        | ChunkTimeSpan (windowTime, provider) ->
            Assert.AreEqual (TimeSpan.FromMilliseconds 10., windowTime, "TimeSpan helper must keep provided window time.")
            Assert.AreSame (ObservableSystem.DefaultTimeProvider, provider, "TimeSpan helper must use default time provider.")
        | _ -> Assert.Fail ("TimeSpan helper must create ChunkTimeSpan configuration.")

    [<TestMethod>]
    member _.``TimeSpanCount should use default time provider`` () =
        match ChunkConfiguration.TimeSpanCount (TimeSpan.FromMilliseconds 10.) 3 with
        | ChunkTimeSpanCount (windowTime, windowLength, provider) ->
            Assert.AreEqual (TimeSpan.FromMilliseconds 10., windowTime, "TimeSpanCount helper must keep provided window time.")
            Assert.AreEqual (3, windowLength, "TimeSpanCount helper must keep provided window length.")
            Assert.AreSame (ObservableSystem.DefaultTimeProvider, provider, "TimeSpanCount helper must use default time provider.")
        | _ -> Assert.Fail ("TimeSpanCount helper must create ChunkTimeSpanCount configuration.")

    [<TestMethod>]
    member _.``Milliseconds should use default time provider`` () =
        match ChunkConfiguration.Milliseconds 15 with
        | ChunkMilliseconds (windowTime, provider) ->
            Assert.AreEqual (15, windowTime, "Milliseconds helper must keep provided value.")
            Assert.AreSame (ObservableSystem.DefaultTimeProvider, provider, "Milliseconds helper must use default time provider.")
        | _ -> Assert.Fail ("Milliseconds helper must create ChunkMilliseconds configuration.")

    [<TestMethod>]
    member _.``MillisecondsCount should use default time provider`` () =
        match ChunkConfiguration.MillisecondsCount 20 4 with
        | ChunkMillisecondsCount (windowTime, windowLength, provider) ->
            Assert.AreEqual (20, windowTime, "MillisecondsCount helper must keep provided window time.")
            Assert.AreEqual (4, windowLength, "MillisecondsCount helper must keep provided window length.")
            Assert.AreSame (ObservableSystem.DefaultTimeProvider, provider, "MillisecondsCount helper must use default time provider.")
        | _ -> Assert.Fail ("MillisecondsCount helper must create ChunkMillisecondsCount configuration.")

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
