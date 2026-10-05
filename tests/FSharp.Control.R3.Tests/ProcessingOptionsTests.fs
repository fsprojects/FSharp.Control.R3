namespace FSharp.Control.R3.Tests

open Microsoft.VisualStudio.TestTools.UnitTesting
open FSharp.Reflection
open R3
open FSharp.Control.R3

/// <summary>
/// Every <see cref="T:FSharp.Control.R3.AwaitOperationConfiguration"/> case with the <see cref="T:R3.AwaitOperation"/> that
/// <see cref="P:FSharp.Control.R3.ProcessingOptions.AwaitOperation"/> selects for it and the concurrency limit that
/// <see cref="P:FSharp.Control.R3.ProcessingOptions.MaxConcurrent"/> reports for it.
/// </summary>
module private ProcessingOptionsTestCases =

    let all = [|
        // R3 SelectAwait ignores the concurrency limit of the modes that do not run in parallel, so they report no limit
        struct (AwaitSequential, AwaitOperation.Sequential, -1)
        struct (AwaitDrop, AwaitOperation.Drop, -1)
        struct (AwaitSwitch, AwaitOperation.Switch, -1)
        // -1 is the value R3 SelectAwait uses for no concurrency limit
        struct (AwaitParallel -1, AwaitOperation.Parallel, -1)
        struct (AwaitParallel 1, AwaitOperation.Parallel, 1)
        struct (AwaitParallel 3, AwaitOperation.Parallel, 3)
        struct (AwaitSequentialParallel -1, AwaitOperation.SequentialParallel, -1)
        struct (AwaitSequentialParallel 4, AwaitOperation.SequentialParallel, 4)
        struct (AwaitThrottleFirstLast, AwaitOperation.ThrottleFirstLast, -1)
    |]

[<TestClass; ProcessingOptionsTestCategory>]
type ProcessingOptionsTests () =

    [<TestMethod>]
    member _.``Default is AwaitSequential with ConfigureAwait and without CancelOnCompleted`` () =
        let options = ProcessingOptions.Default

        let expected = {
            AwaitOperationConfiguration = AwaitSequential
            ConfigureAwait = true
            CancelOnCompleted = false
        }

        Assert.AreEqual (expected, options, "Default must run one invocation at a time, capture the context and let invocations finish")
        Assert.AreEqual (-1, options.MaxConcurrent, "A sequential configuration has no concurrency limit to report")
        Assert.AreEqual (AwaitOperation.Sequential, options.AwaitOperation, "Default must select the sequential R3 operation")

    [<TestMethod>]
    member _.``Parallel is AwaitParallel without a limit, with ConfigureAwait and without CancelOnCompleted`` () =
        let options = ProcessingOptions.Parallel

        let expected = {
            AwaitOperationConfiguration = AwaitParallel -1
            ConfigureAwait = true
            CancelOnCompleted = false
        }

        Assert.AreEqual (expected, options, "Parallel must run every invocation at once, capture the context and let invocations finish")
        Assert.AreEqual (-1, options.MaxConcurrent, "Parallel must report -1, the R3 value for no concurrency limit")
        Assert.AreEqual (AwaitOperation.Parallel, options.AwaitOperation, "Parallel must select the parallel R3 operation")

    [<TestMethod>]
    member _.``AwaitOperation maps every AwaitOperationConfiguration case to the matching R3 AwaitOperation`` () =
        // The table is checked against the union itself, so a case added later cannot stay untested
        let caseName (configuration : AwaitOperationConfiguration) =
            (fst (FSharpValue.GetUnionFields (configuration, typeof<AwaitOperationConfiguration>))).Name

        let testedCases =
            ProcessingOptionsTestCases.all
            |> Seq.map (fun struct (configuration, _, _) -> caseName configuration)
            |> Seq.distinct
            |> Seq.toArray

        let allCases =
            FSharpType.GetUnionCases typeof<AwaitOperationConfiguration>
            |> Array.map _.Name

        CollectionAssert.AreEquivalent (allCases, testedCases, "The table must contain every AwaitOperationConfiguration case")

        for struct (configuration, expected, _) in ProcessingOptionsTestCases.all do
            let options = { ProcessingOptions.Default with AwaitOperationConfiguration = configuration }
            Assert.AreEqual (expected, options.AwaitOperation, $"%A{configuration} must select AwaitOperation.%A{expected}")

    [<TestMethod>]
    member _.``MaxConcurrent reports the limit of AwaitParallel and AwaitSequentialParallel and -1 for every other case`` () =
        for struct (configuration, _, expected) in ProcessingOptionsTestCases.all do
            let options = { ProcessingOptions.Default with AwaitOperationConfiguration = configuration }
            Assert.AreEqual (expected, options.MaxConcurrent, $"%A{configuration} must report a MaxConcurrent of %d{expected}")
