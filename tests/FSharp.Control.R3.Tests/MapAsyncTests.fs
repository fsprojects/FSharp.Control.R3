namespace FSharp.Control.R3.Tests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests.TestHelpers

/// <summary>
/// Integration tests of
/// <see cref="M:FSharp.Control.R3.Task.Observable.mapAsync``2(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,Microsoft.FSharp.Core.FSharpFunc{``0,System.Threading.Tasks.Task{``1}}},R3.Observable{``0})"/>
/// and <see cref="M:FSharp.Control.R3.Async.Observable.mapAsync``2(FSharp.Control.R3.ProcessingOptions,Microsoft.FSharp.Core.FSharpFunc{``0,Microsoft.FSharp.Control.FSharpAsync{``1}},R3.Observable{``0})"/>
/// over <see cref="M:R3.ObservableExtensions.SelectAwait``2(R3.Observable{``0},System.Func{``0,System.Threading.CancellationToken,System.Threading.Tasks.ValueTask{``1}},R3.AwaitOperation,System.Boolean,System.Boolean,System.Int32)"/>:
/// every test runs once per flavour and covers one behaviour of the <see cref="T:FSharp.Control.R3.ProcessingOptions"/>.
/// </summary>
[<TestClass; MapAsyncTestCategory>]
type MapAsyncTests (testContext : TestContext) =

    /// Every await operation, with and without a concurrency limit where R3 uses a different observer for each.
    let allConfigurations = [|
        AwaitSequential
        AwaitDrop
        AwaitSwitch
        AwaitParallel -1
        AwaitParallel 2
        AwaitSequentialParallel -1
        AwaitSequentialParallel 2
        AwaitThrottleFirstLast
    |]

    /// The default options with another await operation.
    let optionsWith configuration = { ProcessingOptions.Default with AwaitOperationConfiguration = configuration }

    /// Subscribes and pushes one element with a recording synchronization context installed, then completes the selector
    /// from outside that context and reports what the context and the recorder saw.
    let mapUnderRecordingContext (cancellationToken : CancellationToken) (flavour : string) (configureAwait : bool) = task {
        use subject = new Subject<int> ()
        let context = RecordingSynchronizationContext ()
        let gate = TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously)
        // The selectors hand the gate to R3 without awaiting it themselves, so the await of R3, configured by the option,
        // is the only continuation that can capture the context. The gated selector of the helpers would post on its own:
        // its task expression captures the context, and so does Async.AwaitTask, even with ConfigureAwait set to false.
        // The Async selector is therefore resumed through an awaiter of the gate that does not capture the context
        let awaitGate (_ : int) =
            Async.FromContinuations (fun (onSuccess, _, _) ->
                let awaiter = gate.Task.ConfigureAwait(false).GetAwaiter()
                awaiter.OnCompleted (fun () -> onSuccess (awaiter.GetResult ()))
            )
        let options = { ProcessingOptions.Parallel with ConfigureAwait = configureAwait }

        // AwaitParallel invokes the selector inside OnNext, so the await of R3 captures the context installed around OnNext.
        // The modes with a worker invoke the selector on the worker, where the captured context depends on thread timing
        use recorder =
            context.Run (fun () ->
                let recorder =
                    subject
                    |> Flavour.mapAsyncWith flavour options (fun _ _ -> gate.Task) awaitGate
                    |> Recorder.Attach
                subject.OnNext 1
                recorder
            )

        // The unlimited parallel mode of R3 checks its completion without a lock and can lose a completion
        // that races with its last selector, so the source completes while the selector is still pending
        subject.OnCompleted (Result.Success)
        // Completed outside the context, so a continuation can only get back to the context through a post
        gate.SetResult 10
        let! completion = recorder.WaitForCompletionAsync cancellationToken
        return struct {|
            Posts = context.Posts
            Values = recorder.Values
            Completion = completion
        |}
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``AwaitSequential runs one selector at a time and emits in input order`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        use recorder =
            subject
            |> Flavour.mapWith flavour ProcessingOptions.Default selector
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        subject.OnNext 3
        // The worker of R3 takes the queued elements one by one, so the second element waits for the first selector
        do! selector.WaitForStartedAsync (ct, 1)
        CollectionAssert.AreEqual ([| 1 |], selector.Started, "Only the first element may reach the selector while it runs")

        subject.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "The completion must wait for the queued elements")

        // Releasing the later elements first cannot reorder the results: they only start once the earlier ones finish
        selector.Release 3
        selector.Release 2
        selector.Release 1
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 10; 20; 30 |], recorder.Values, "The results must be emitted in input order")
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully")
        CollectionAssert.AreEqual ([| 1; 2; 3 |], selector.Started, "Every element must reach the selector in input order")
        Assert.AreEqual (1, selector.MaxInFlight, "The selector invocations must never overlap")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``AwaitDrop discards the elements that arrive while a selector runs`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        use recorder =
            subject
            |> Flavour.mapWith flavour (optionsWith AwaitDrop) selector
            |> Recorder.Attach

        subject.OnNext 1
        do! selector.WaitForStartedAsync (ct, 1)
        // R3 marks the operator as running inside OnNext and discards, rather than queues, what arrives meanwhile
        subject.OnNext 2
        subject.OnNext 3
        subject.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "The completion must wait for the running selector")

        selector.Release 1
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 10 |], recorder.Values, "Only the result of the first element may be emitted")
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully")
        CollectionAssert.AreEqual ([| 1 |], selector.Started, "The elements pushed while the selector ran must never reach it")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``AwaitSwitch cancels the token of the running selector and suppresses its result`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        use recorder =
            subject
            |> Flavour.mapWith flavour (optionsWith AwaitSwitch) selector
            |> Recorder.Attach

        // R3 starts a selector inside every OnNext and cancels the token of the one still running
        subject.OnNext 1
        subject.OnNext 2
        subject.OnNext 3
        do! selector.WaitForStartedAsync (ct, 3)

        // Checked before the latest selector finishes: R3 disposes the operator once that selector publishes the completion,
        // and the disposal cancels its token too
        let tokens = selector.Tokens
        Assert.HasCount (3, tokens, "Every element must reach the selector")
        Assert.IsTrue (tokens[0].IsCancellationRequested, "The second element must cancel the token of the first selector")
        Assert.IsTrue (tokens[1].IsCancellationRequested, "The third element must cancel the token of the second selector")
        Assert.IsFalse (tokens[2].IsCancellationRequested, "The token of the latest selector must stay active")
        Assert.AreEqual (3, selector.MaxInFlight, "A new selector must start without waiting for the superseded ones")

        // The superseded selectors finish after the latest one started, and R3 must drop whatever they return
        selector.Release 1
        selector.Release 2
        subject.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "The completion must wait for the latest selector")

        selector.Release 3
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 30 |], recorder.Values, "Only the result of the latest selector may be emitted")
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``AwaitParallel without a limit runs every selector at once and emits in completion order`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        use recorder =
            subject
            |> Flavour.mapWith flavour ProcessingOptions.Parallel selector
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        subject.OnNext 3
        do! selector.WaitForStartedAsync (ct, 3)
        Assert.AreEqual (3, selector.MaxInFlight, "Every element must reach the selector without waiting for the others")

        selector.Release 2
        do! recorder.WaitForValuesAsync (ct, 1)
        selector.Release 3
        do! recorder.WaitForValuesAsync (ct, 2)
        CollectionAssert.AreEqual ([| 20; 30 |], recorder.Values, "Every result must be emitted as soon as its selector finishes")

        // The unlimited parallel mode of R3 checks its completion without a lock and can lose a completion
        // that races with its last selector, so the source completes while a selector is still pending
        subject.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "The completion must wait for the pending selector")

        selector.Release 1
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 20; 30; 10 |], recorder.Values, "The results must be emitted in completion order")
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully")
        CollectionAssert.AreEqual ([| 1; 2; 3 |], selector.Started, "Every element must reach the selector in input order")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``AwaitParallel with a limit queues the extra elements first in first out`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        use recorder =
            subject
            |> Flavour.mapWith flavour (optionsWith (AwaitParallel 2)) selector
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        subject.OnNext 3
        subject.OnNext 4
        do! selector.WaitForStartedAsync (ct, 2)
        CollectionAssert.AreEqual ([| 1; 2 |], selector.Started, "Only two elements may reach the selector while both selectors run")

        // R3 starts the oldest queued element when a selector finishes, right after emitting its result
        selector.Release 2
        do! recorder.WaitForValuesAsync (ct, 1)
        do! selector.WaitForStartedAsync (ct, 3)
        CollectionAssert.AreEqual ([| 1; 2; 3 |], selector.Started, "The oldest queued element must take the free slot")

        selector.Release 3
        do! recorder.WaitForValuesAsync (ct, 2)
        do! selector.WaitForStartedAsync (ct, 4)
        selector.Release 4
        do! recorder.WaitForValuesAsync (ct, 3)
        subject.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "The completion must wait for the pending selector")

        selector.Release 1
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 20; 30; 40; 10 |], recorder.Values, "The results must be emitted in completion order")
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully")
        CollectionAssert.AreEqual ([| 1; 2; 3; 4 |], selector.Started, "The queued elements must reach the selector in input order")
        Assert.AreEqual (2, selector.MaxInFlight, "No more than two selectors may run at the same time")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``AwaitSequentialParallel without a limit runs every selector at once and emits in input order`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        use recorder =
            subject
            |> Flavour.mapWith flavour (optionsWith (AwaitSequentialParallel -1)) selector
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        subject.OnNext 3
        do! selector.WaitForStartedAsync (ct, 3)
        Assert.AreEqual (3, selector.MaxInFlight, "Every element must reach the selector without waiting for the others")

        // R3 queues the running selectors in input order, so the results of the later ones wait for the first one
        selector.Release 3
        selector.Release 2
        subject.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "The completion must wait for the pending selector")

        selector.Release 1
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 10; 20; 30 |], recorder.Values, "The results must be emitted in input order")
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully")
        CollectionAssert.AreEqual ([| 1; 2; 3 |], selector.Started, "Every element must reach the selector in input order")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``AwaitSequentialParallel with a limit runs at most that many selectors and emits in input order`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        use recorder =
            subject
            |> Flavour.mapWith flavour (optionsWith (AwaitSequentialParallel 2)) selector
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        subject.OnNext 3
        do! selector.WaitForStartedAsync (ct, 2)
        CollectionAssert.AreEqual ([| 1; 2 |], selector.Started, "Only two elements may reach the selector while both selectors run")

        // R3 starts the queued element as soon as a selector finishes, before the result of that selector is emitted
        selector.Release 1
        do! selector.WaitForStartedAsync (ct, 3)
        do! recorder.WaitForValuesAsync (ct, 1)
        CollectionAssert.AreEqual ([| 10 |], recorder.Values, "The result of the first element must be emitted once its selector finishes")

        subject.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "The completion must wait for the pending selectors")

        // The third selector finishes first, but its result must wait for the second one.
        // No selector fails here: R3 leaks a slot of this mode when a selector fails
        selector.Release 3
        selector.Release 2
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 10; 20; 30 |], recorder.Values, "The results must be emitted in input order")
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully")
        Assert.AreEqual (2, selector.MaxInFlight, "No more than two selectors may run at the same time")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``AwaitThrottleFirstLast runs the first and the last element of a burst`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        use recorder =
            subject
            |> Flavour.mapWith flavour (optionsWith AwaitThrottleFirstLast) selector
            |> Recorder.Attach

        subject.OnNext 1
        // The worker of R3 must take the first element before the burst arrives, or the burst would replace it
        do! selector.WaitForStartedAsync (ct, 1)
        // While the selector runs, R3 buffers a single element and drops the oldest one when another arrives
        subject.OnNext 2
        subject.OnNext 3
        subject.OnNext 4
        subject.OnNext 5
        subject.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "The completion must wait for the running selector and the buffered element")

        selector.Release 1
        do! selector.WaitForStartedAsync (ct, 2)
        CollectionAssert.AreEqual ([| 1; 5 |], selector.Started, "Only the last element of the burst may follow the first one")

        selector.Release 5
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 10; 50 |], recorder.Values, "The results of the first and the last element must be emitted")
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``CancelOnCompleted completes at once, cancels the running selector token and drops its pending result`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        for configuration in allConfigurations do
            use subject = new Subject<int> ()
            let selector = GatedSelector<int, int>(fun x -> x * 10)
            let options = {
                ProcessingOptions.Default with
                    AwaitOperationConfiguration = configuration
                    CancelOnCompleted = true
            }
            use recorder =
                subject
                |> Flavour.mapWith flavour options selector
                |> Recorder.Attach

            subject.OnNext 1
            do! selector.WaitForStartedAsync (ct, 1)
            // R3 cancels its token and publishes the completion inside OnCompleted of the source
            subject.OnCompleted (Result.Success)

            Assert.IsTrue (recorder.IsCompletedSuccessfully, $"%A{configuration}: the mapped sequence must complete with the source")
            Assert.IsTrue (selector.Tokens[0].IsCancellationRequested, $"%A{configuration}: the token of the running selector must be cancelled")
            // The recorder has completed, so the result the selector may still return can never be emitted
            Assert.IsEmpty (recorder.Values, $"%A{configuration}: the pending result must be dropped")
            Assert.IsEmpty (recorder.Errors, $"%A{configuration}: no error may be resumed")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``A source failure completes at once and cancels the running selector token even without CancelOnCompleted`` (flavour : string) : Task =
        task {
            let ct = testContext.CancellationToken
            let boom : exn = InvalidOperationException "boom"
            for configuration in allConfigurations do
                use subject = new Subject<int> ()
                let selector = GatedSelector<int, int>(fun x -> x * 10)
                use recorder =
                    subject
                    |> Flavour.mapWith flavour (optionsWith configuration) selector
                    |> Recorder.Attach

                subject.OnNext 1
                do! selector.WaitForStartedAsync (ct, 1)
                // R3 handles a failure of the source like CancelOnCompleted, whatever the option says
                subject.OnCompleted (Result.Failure boom)

                assertFailedWith boom recorder $"%A{configuration}: the mapped sequence must fail at once with the failure of the source"
                Assert.IsTrue (selector.Tokens[0].IsCancellationRequested, $"%A{configuration}: the token of the running selector must be cancelled")
                Assert.IsEmpty (recorder.Values, $"%A{configuration}: the pending result must be dropped")
                Assert.IsEmpty (recorder.Errors, $"%A{configuration}: no error may be resumed")
        }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``An exception of the selector is resumed and the later elements are still mapped`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        let boom : exn = InvalidOperationException "boom"
        let selector = GatedSelector<int, int>(fun x -> if x = 2 then raise boom else x * 10)
        // Released up front, so every invocation finishes as soon as it starts
        selector.Release 1
        selector.Release 2
        selector.Release 3
        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Flavour.mapWith flavour ProcessingOptions.Default selector
            |> Recorder.Attach

        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 10; 30 |], recorder.Values, "The elements around the failing one must still be mapped")
        let error =
            Assert.ContainsSingle (recorder.Errors, "The exception of the selector must be resumed once")
        // R3 awaits the task of the selector, which rethrows the original exception in both flavours:
        // the Async flavour raises it inside the computation, which faults the task with that same exception
        Assert.AreSame (boom, error, "The resumed error must be the exception of the selector")
        Assert.IsTrue (completion.IsSuccess, "An exception of the selector must not fail the mapped sequence")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``An error resumed by the source is forwarded and the sequence continues`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        let boom : exn = InvalidOperationException "boom"
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        selector.Release 1
        selector.Release 2
        use recorder =
            Sources.resumingError boom
            |> Flavour.mapWith flavour ProcessingOptions.Default selector
            |> Recorder.Attach

        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 10; 20 |], recorder.Values, "The elements around the error must still be mapped")
        let error = Assert.ContainsSingle (recorder.Errors, "The error of the source must be forwarded once")
        Assert.AreSame (boom, error, "The forwarded error must be the error of the source")
        Assert.IsTrue (completion.IsSuccess, "An error resumed by the source must not fail the mapped sequence")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``An empty source completes successfully without invoking the selector`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        for configuration in allConfigurations do
            let selector = GatedSelector<int, int>(fun x -> x * 10)
            use recorder =
                Sources.values [||]
                |> Flavour.mapWith flavour (optionsWith configuration) selector
                |> Recorder.Attach

            // The modes with a worker publish the completion from the worker, which may run later
            let! completion = recorder.WaitForCompletionAsync ct

            Assert.IsTrue (completion.IsSuccess, $"%A{configuration}: the mapped sequence must complete successfully")
            Assert.IsEmpty (recorder.Values, $"%A{configuration}: no value may be emitted")
            Assert.IsEmpty (recorder.Errors, $"%A{configuration}: no error may be resumed")
            Assert.IsEmpty (selector.Started, $"%A{configuration}: the selector must never be invoked")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``A concurrency limit of 0 or below -1 is rejected when mapAsync is called, before any subscription`` (flavour : string) =
        let selector = GatedSelector<int, int>(fun x -> x * 10)

        for limit in [| 0; -2 |] do
            for configuration in [| AwaitParallel limit; AwaitSequentialParallel limit |] do
                // R3 itself validates the limit only when the mapped sequence is subscribed, with an ArgumentException.
                // The helper never subscribes the mapped sequence, so only the eager validation of the library can throw here
                assertArgumentRejected
                    (Flavour.mapWith flavour (optionsWith configuration) selector)
                    "options"
                    limit
                    $"mapAsync must reject %A{configuration} as soon as it is called"

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``AwaitParallel 1 is accepted and runs one selector at a time`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        use subject = new Subject<int> ()
        let selector = GatedSelector<int, int>(fun x -> x * 10)
        // Like R3, the eager validation of the library rejects only 0 and limits below -1,
        // although the message of R3 asks for a limit greater than 1
        use recorder =
            subject
            |> Flavour.mapWith flavour (optionsWith (AwaitParallel 1)) selector
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        do! selector.WaitForStartedAsync (ct, 1)
        CollectionAssert.AreEqual ([| 1 |], selector.Started, "The second element must be queued while the first selector runs")

        subject.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "The completion must wait for the running and the queued element")

        selector.Release 2
        selector.Release 1
        let! completion = recorder.WaitForCompletionAsync ct

        CollectionAssert.AreEqual ([| 10; 20 |], recorder.Values, "The results must be emitted in input order")
        Assert.IsEmpty (recorder.Errors, "No error may be resumed")
        Assert.IsTrue (completion.IsSuccess, "The mapped sequence must complete successfully")
        Assert.AreEqual (1, selector.MaxInFlight, "The selector invocations must never overlap")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``Disposing the subscription cancels the running selector token and unsubscribes from the source`` (flavour : string) : Task = task {
        let ct = testContext.CancellationToken
        for configuration in allConfigurations do
            use subject = new Subject<int> ()
            let probe = SubscriptionProbe ()
            let selector = GatedSelector<int, int>(fun x -> x * 10)
            use recorder =
                subject
                |> probe.Watch
                |> Flavour.mapWith flavour (optionsWith configuration) selector
                |> Recorder.Attach

            subject.OnNext 1
            do! selector.WaitForStartedAsync (ct, 1)
            // Disposing the recorder disposes the observer of R3, which cancels its token and unsubscribes from the source.
            // The disposed recorder ignores every later notification, so only the token and the probe can show the teardown
            recorder.Dispose ()

            Assert.IsTrue (selector.Tokens[0].IsCancellationRequested, $"%A{configuration}: the token of the running selector must be cancelled")
            // The subject never completes by itself, so the disposal seen by the probe can only come from an unsubscription
            Assert.AreEqual (1, probe.Disposed, $"%A{configuration}: the subscription to the source must be disposed")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``ConfigureAwait true resumes the selector continuation on the synchronization context of OnNext`` (flavour : string) : Task = task {
        let! outcome = mapUnderRecordingContext testContext.CancellationToken flavour true

        Assert.IsTrue (outcome.Posts >= 1, "The continuation of the selector must be posted to the context captured inside OnNext")
        CollectionAssert.AreEqual ([| 10 |], outcome.Values, "The result of the selector must be emitted")
        Assert.IsTrue (outcome.Completion.IsSuccess, "The mapped sequence must complete successfully")
    }

    [<TestMethod; DataRow "Task"; DataRow "Async">]
    member _.``ConfigureAwait false resumes the selector continuation without the synchronization context of OnNext`` (flavour : string) : Task = task {
        let! outcome = mapUnderRecordingContext testContext.CancellationToken flavour false

        Assert.AreEqual (0, outcome.Posts, "No continuation may be posted to the context captured inside OnNext")
        CollectionAssert.AreEqual ([| 10 |], outcome.Values, "The result of the selector must be emitted")
        Assert.IsTrue (outcome.Completion.IsSuccess, "The mapped sequence must complete successfully")
    }
