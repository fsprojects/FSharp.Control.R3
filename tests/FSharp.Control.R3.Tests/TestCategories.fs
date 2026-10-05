// Every TestCategoryBaseAttribute descendant used to categorize the tests of this project, one per tested component.
// MSTest picks up their TestCategories through --filter TestCategory=... exactly like [<TestCategory>]'s,
// but each attribute is self-sufficient: no separate string constant is needed to know what to pass it.
namespace FSharp.Control.R3.Tests

open System.Collections.Generic
open Microsoft.VisualStudio.TestTools.UnitTesting

/// <summary>
/// Categorizes the tests of <see cref="T:FSharp.Control.R3.ProcessingOptions"/> and
/// <see cref="T:FSharp.Control.R3.AwaitOperationConfiguration"/>.
/// </summary>
type ProcessingOptionsTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "ProcessingOptions" |] :> IList<string>

/// <summary>
/// Categorizes the tests of the functions of the <see cref="T:FSharp.Control.R3.Observable"/> module,
/// of <see cref="T:FSharp.Control.R3.ObservableOption"/> and of the <see cref="T:FSharp.Control.R3.ObservableFactories.Observable"/> factories.
/// </summary>
type ObservableTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Observable" |] :> IList<string>

/// <summary>
/// Categorizes the tests of the chunking functions and of <see cref="T:FSharp.Control.R3.ChunkConfiguration`1"/>.
/// </summary>
type ChunkTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Chunk" |] :> IList<string>

/// <summary>
/// Categorizes the tests of the <see cref="T:FSharp.Control.R3.Observable.BuildersModule.RxQueryBuilder"/> query builder.
/// </summary>
type BuilderTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Builder" |] :> IList<string>

/// <summary>
/// Categorizes the tests of the Async flavour, <see cref="T:FSharp.Control.R3.Async"/>.
/// </summary>
type AsyncTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Async" |] :> IList<string>

/// <summary>
/// Categorizes the tests of the Task flavour, <see cref="T:FSharp.Control.R3.Task"/>.
/// </summary>
type TaskTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Task" |] :> IList<string>

/// <summary>
/// Categorizes the tests of mapAsync, which run every test against both flavours, so they also belong to the Async and Task categories.
/// </summary>
type MapAsyncTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "MapAsync"; "Async"; "Task" |] :> IList<string>

/// <summary>
/// Categorizes the end-to-end scenarios that combine several areas of the library.
/// </summary>
type IntegrationTestCategoryAttribute () =
    inherit TestCategoryBaseAttribute ()
    override _.TestCategories = [| "Integration" |] :> IList<string>
