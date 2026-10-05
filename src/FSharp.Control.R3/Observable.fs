module FSharp.Control.R3.Observable

open System
open R3

/// Hides the identy of an observable sequence
let inline asObservable source : Observable<'Source> = ObservableExtensions.AsObservable source

/// Binds an observable to generate a subsequent observable.
let inline bind ([<InlineIfLambda>] f : 'T -> Observable<'TNext>) source = ObservableExtensions.SelectMany (source, f)

/// Converts the elements of the sequence to the specified type
let inline cast<'T, 'CastType> (source) = ObservableExtensions.Cast<'T, 'CastType>(source)

/// <summary>
/// Adds an error handler to an observable sequence.
/// </summary>
/// <remarks>Exception does not stop further processing</remarks>
let inline catch ([<InlineIfLambda>] f : 'Exn -> Observable<'T>) o = ObservableExtensions.Catch (o, f)

/// Concatenates the second observable sequence to the first observable sequence
/// upn the successful termination of the first
let inline concat first second = ObservableExtensions.Concat (first, second)

///<summary>Divides the input observable sequence into chunks of size at most <c>chunkSize</c>.</summary>
///<param name="chunkSize">The maximum size of each chunk.</param>
///<param name="source">The input observable sequence.</param>
///<returns>The observable sequence divided into chunks.</returns>
///<exception cref="T:System.ArgumentNullException">Thrown when the input sequence is null.</exception>
///<exception cref="T:System.ArgumentException">Thrown when <c>chunkSize</c> is not positive.</exception>
let inline chunkBySize (chunkSize : int) (source) = ObservableExtensions.Chunk (source, chunkSize)

let inline chunkBy (configuration : ChunkConfiguration<'T>) (source) =
    match configuration with
    | ChunkCount count -> ObservableExtensions.Chunk (source, count)
    | ChunkTimeSpan (timeSpan, timeProvider) -> ObservableExtensions.Chunk (source, timeSpan, timeProvider)
    | ChunkTimeSpanCount (timeSpan, count, timeProvider) -> ObservableExtensions.Chunk (source, timeSpan, count, timeProvider)
    | ChunkMilliseconds (milliseconds, timeProvider) ->
        ObservableExtensions.Chunk (source, TimeSpan.FromMilliseconds (float milliseconds), timeProvider)
    | ChunkMillisecondsCount (milliseconds, count, timeProvider) ->
        ObservableExtensions.Chunk (source, TimeSpan.FromMilliseconds (float milliseconds), count, timeProvider)
    | ChunkAsyncWindow (asyncWindow, configureAwait) -> ObservableExtensions.Chunk (source, asyncWindow, configureAwait)
    | ChunkWindowBoundaries windowBoundaries -> ObservableExtensions.Chunk (source, windowBoundaries = windowBoundaries)

/// Returns an observable sequence that only contains distinct elements
let inline distinct source = ObservableExtensions.Distinct source

/// Returns an observable sequence that contains no elements
let inline empty () = Observable.Empty ()

/// Filters the observable elements of a sequence based on a predicate
let inline filter ([<InlineIfLambda>] f : 'T -> bool) source = ObservableExtensions.Where (source, f)

/// Maps the given observable with the given function
let inline map ([<InlineIfLambda>] f : 'T -> 'R) source = ObservableExtensions.Select (source, f)

/// Maps the given observable with the given function and the index of the element
let inline mapi ([<InlineIfLambda>] f : int -> 'T -> 'R) source = ObservableExtensions.Select (source, (fun i x -> f x i))

/// Merges two observable sequences into one observable sequence
let inline merge (source1, source2) = ObservableExtensions.Merge (source1, source2)

let inline ofType<'T, 'R> (source) = ObservableExtensions.OfType<'T, 'R>(source)

/// Returns an observable sequence that contains only a single element
let inline singleton item = Observable.Return<'T> item

/// Bypasses a specified number of elements in an observable sequence and then returns the remaining elements
let inline skip (count : int) (source) = ObservableExtensions.Skip (source, count)

/// Takes n elements (from the beginning of an observable sequence?)
let inline take (count : int) (source) = ObservableExtensions.Take (source, count)

/// Filters the observable elements of a sequence based on a predicate
let inline where ([<InlineIfLambda>] f : 'T -> bool) source = ObservableExtensions.Where (source, f)

// choose is a module function rather than a static member of an extension type: a module function here shadows
// FSharp.Core's Observable.choose, which would otherwise win the name resolution of Observable.choose.
// Like every function of the library that works with optional values it takes value options, which do not allocate;
// the variants that take options live in the ObservableOption module.

/// <summary>
/// Applies the chooser to each element and emits the values of the <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1"/> results
/// that hold one.
/// <para>
/// The variant that takes an <see cref="T:Microsoft.FSharp.Core.FSharpOption`1"/> chooser is
/// <see cref="M:FSharp.Control.R3.ObservableOption.choose``2(Microsoft.FSharp.Core.FSharpFunc{``0,Microsoft.FSharp.Core.FSharpOption{``1}},R3.Observable{``0})"/>.
/// </para>
/// </summary>
let inline choose ([<InlineIfLambda>] chooser : 'T -> 'R voption) (source : Observable<'T>) =
    source
    |> map chooser
    |> filter ValueOption.isSome
    |> map ValueOption.get

/// The <see cref="T:FSharp.Control.R3.Observable.BuildersModule.RxQueryBuilder"/> query builder and its instances.
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Builders =

    open System.Threading

    /// <summary>
    /// A reactive query builder.
    /// <para>
    /// The query operators that return an observable sequence are lazy. The ones that return a task subscribe at once,
    /// complete when the deciding element arrives or the source terminates, and observe
    /// <see cref="P:FSharp.Control.R3.Observable.BuildersModule.RxQueryBuilder.CancellationToken"/>:
    /// cancelling it disposes the subscription and cancels the task.
    /// </para>
    /// <para>See http://mnajder.blogspot.com/2011/09/when-reactive-framework-meets-f-30.html</para>
    /// </summary>
    type RxQueryBuilder
        /// Creates a builder whose query operators that return a task observe the token.
        (cancellationToken : CancellationToken)
        =

        /// Creates a builder whose query operators that return a task cannot be cancelled.
        new () = RxQueryBuilder CancellationToken.None

        /// The token observed by the query operators that return a task.
        member _.CancellationToken = cancellationToken

        /// Projects every element of the source to an observable sequence and merges the results.
        member _.For (s : Observable<_>, body : _ -> Observable<_>) = s.SelectMany (body)

        /// Projects every element of the query.
        [<CustomOperation("select", AllowIntoPattern = true)>]
        member _.Select (s : Observable<_>, [<ProjectionParameter>] selector : _ -> _) = s.Select (selector)

        /// Keeps the elements of the query that satisfy the predicate.
        [<CustomOperation("where", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.Where (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.Where (predicate)

        /// Keeps the elements of the query while they satisfy the predicate and completes at the first one that does not.
        [<CustomOperation("takeWhile", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.TakeWhile (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.TakeWhile (predicate)

        /// Keeps the first count elements of the query and then completes.
        [<CustomOperation("take", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.Take (s : Observable<_>, count : int) = s.Take (count)

        /// Bypasses the leading elements of the query that satisfy the predicate.
        [<CustomOperation("skipWhile", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.SkipWhile (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.SkipWhile (predicate)

        /// Bypasses the first count elements of the query.
        [<CustomOperation("skip", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.Skip (s : Observable<_>, count : int) = s.Skip (count)

        // Zero and Yield emit synchronously on subscription: scheduling them on TimeProvider.System hopped every element of
        // a query to the thread pool, so SelectMany merged them in arbitrary order and the results arrived late

        /// An empty sequence, used for the elements skipped by an if-then expression without else.
        member _.Zero () : Observable<'T> = Observable.Empty<'T>()

        /// A sequence of the single yielded element.
        member _.Yield (value : 'T) = Observable.Return<'T> value

        /// Counts the elements of the query.
        [<CustomOperation("count")>]
        member _.Count (s : Observable<_>) = ObservableExtensions.CountAsync (s, cancellationToken)

        /// Determines whether every element of the query satisfies the predicate; completes at the first one that does not.
        [<CustomOperation("all")>]
        member _.All (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) =
            s.AllAsync (new Func<_, bool> (predicate), cancellationToken)

        /// Determines whether the query contains the element; completes at the first equal one.
        [<CustomOperation("contains")>]
        member _.Contains (s : Observable<_>, key) = s.ContainsAsync (key, cancellationToken)

        /// Removes the repeated elements of the query.
        [<CustomOperation("distinct", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.Distinct (s : Observable<_>) = s.Distinct ()

        /// Returns the only element of the query; fails when the query has no element or more than one.
        [<CustomOperation("exactlyOne")>]
        member _.ExactlyOne (s : Observable<_>) = s.SingleAsync (cancellationToken)

        /// <summary>
        /// Returns the only element of the query, or the default value when the query has no element; fails on more than one.
        /// <para>The default value of a reference type is <see langword="null"/>.</para>
        /// </summary>
        [<CustomOperation("exactlyOneOrDefault")>]
        member _.ExactlyOneOrDefault (s : Observable<_>) = s.SingleOrDefaultAsync (cancellationToken = cancellationToken)

        /// Returns the first element of the query that satisfies the predicate; fails when there is none.
        [<CustomOperation("find")>]
        member _.Find (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) =
            s.FirstAsync (new Func<_, bool> (predicate), cancellationToken)

        /// Returns the first element of the query; fails when the query has no element.
        [<CustomOperation("head")>]
        member _.Head (s : Observable<_>) = s.FirstAsync (cancellationToken)

        /// <summary>
        /// Returns the first element of the query, or the default value when the query has no element.
        /// <para>The default value of a reference type is <see langword="null"/>.</para>
        /// </summary>
        [<CustomOperation("headOrDefault")>]
        member _.HeadOrDefault (s : Observable<_>) = s.FirstOrDefaultAsync (cancellationToken = cancellationToken)

        /// Returns the last element of the query; fails when the query has no element.
        [<CustomOperation("last")>]
        member _.Last (s : Observable<_>) = s.LastAsync (cancellationToken)

        /// <summary>
        /// Returns the last element of the query, or the default value when the query has no element.
        /// <para>The default value of a reference type is <see langword="null"/>.</para>
        /// </summary>
        [<CustomOperation("lastOrDefault")>]
        member _.LastOrDefault (s : Observable<_>) = s.LastOrDefaultAsync (cancellationToken = cancellationToken)

        /// Returns the element of the query with the largest projected value, the first one on ties; fails when the query has no element.
        [<CustomOperation("maxBy")>]
        member _.MaxBy (s : Observable<'a>, [<ProjectionParameter>] valueSelector : 'a -> 'b) =
            s.MaxByAsync (new Func<'a, 'b> (valueSelector), cancellationToken)

        /// Returns the element of the query with the smallest projected value, the first one on ties; fails when the query has no element.
        [<CustomOperation("minBy")>]
        member _.MinBy (s : Observable<'a>, [<ProjectionParameter>] valueSelector : 'a -> 'b) =
            s.MinByAsync (new Func<'a, 'b> (valueSelector), cancellationToken)

        /// Adds up the projected values of the query; returns the default value of the projected type when the query has no element.
        [<CustomOperation("sumBy")>]
        member inline this.SumBy (s : Observable<_>, [<ProjectionParameter>] valueSelector : _ -> 'Value) =
            // The first element seeds the sum: seeding with Unchecked.defaultof passed null to the (+) of reference types,
            // while requiring a Zero member would reject types such as TimeSpan whose zero is a field
            s
            |> _.Select(valueSelector)
            |> _.AggregateAsync(
                ValueNone,
                new Func<_, _, _> (fun sum value ->
                    match sum with
                    | ValueNone -> ValueSome value
                    | ValueSome sum -> ValueSome (sum + value)
                ),
                new Func<_, _> (ValueOption.defaultValue Unchecked.defaultof<'Value>),
                this.CancellationToken
            )

        /// Pairs the elements of the query with the elements of another sequence by position; completes with the shorter one.
        [<CustomOperation("zip", IsLikeZip = true)>]
        member _.Zip (s1 : Observable<_>, s2 : Observable<_>, [<ProjectionParameter>] resultSelector : _ -> _) =
            s1.Zip (s2, new Func<_, _, _> (resultSelector))

        /// Invokes the action for every element of the query; completes when the query completes.
        [<CustomOperation("iter")>]
        member _.Iter (s : Observable<_>, [<ProjectionParameter>] selector : _ -> _) = s.ForEachAsync (new Action<_> (selector), cancellationToken)

    /// A reactive query builder whose query operators that return a task cannot be cancelled.
    let rxquery = RxQueryBuilder ()

    /// <summary>
    /// A reactive query builder whose query operators that return a task observe <paramref name="cancellationToken"/>.
    /// <para>Parenthesize the application in front of the query: <c>(rxqueryWith cancellationToken) { for x in source do head }</c>.</para>
    /// </summary>
    let rxqueryWith (cancellationToken : CancellationToken) = RxQueryBuilder cancellationToken
