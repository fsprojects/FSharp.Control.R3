module FSharp.Control.R3.Observable

open R3

/// Applies an accumulator function over an observable sequence, returning the
/// result of the aggregation as a single element in the result sequence
let inline aggregateAsync cancellationToken seed (f : 'r -> 't -> 'r) source =
    ObservableExtensions.AggregateAsync (source, seed, f, cancellationToken)

/// Determines whether all elements of an observable satisfy a predicate
let inline allAsync cancellationToken (f : 't -> bool) source = ObservableExtensions.AllAsync (source, f, cancellationToken)

/// Hides the identy of an observable sequence
let inline asObservable source : Observable<'Source> = ObservableExtensions.AsObservable source

/// Binds an observable to generate a subsequent observable.
let inline bind (f : 'T -> Observable<'TNext>) source = ObservableExtensions.SelectMany (source, f)

/// Converts the elements of the sequence to the specified type
let inline cast<'T, 'CastType> (source) = ObservableExtensions.Cast<'T, 'CastType> (source)

/// Concatenates the second observable sequence to the first observable sequence
/// upn the successful termination of the first
let inline concat source = ObservableExtensions.Concat source

/// Returns an observable sequence that only contains distinct elements
let inline distinct source = ObservableExtensions.Distinct source

/// Determines whether an observable sequence contains a specified value
/// which satisfies the given predicate
let inline existsAsync source = ObservableExtensions.AnyAsync source

/// Returns the first element of an observable sequence
let inline firstAsync source = ObservableExtensions.FirstAsync source

/// Filters the observable elements of a sequence based on a predicate
let inline filter (f : 't -> bool) source = ObservableExtensions.Where (source, f)

/// Maps the given observable with the given function
let inline map (f : 't -> 'r) source = ObservableExtensions.Select (source, f)

/// Maps the given observable with the given function and the index of the element
let inline mapi (f : int -> 't -> 'r) source = ObservableExtensions.Select (source, (fun i x -> f x i))

/// Bypasses a specified number of elements in an observable sequence and then returns the remaining elements.
let inline skip (count : int) (source) = ObservableExtensions.Skip (source, count)

/// Takes n elements (from the beginning of an observable sequence? )
let inline take (count : int) (source) = ObservableExtensions.Take (source, count)


[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Builders =
    open System

    /// A reactive query builder.
    /// See http://mnajder.blogspot.com/2011/09/when-reactive-framework-meets-f-30.html
    type RxQueryBuilder () =
        member __.For (s : Observable<_>, body : _ -> Observable<_>) = s.SelectMany (body)
        [<CustomOperation("select", AllowIntoPattern = true)>]
        member __.Select (s : Observable<_>, [<ProjectionParameter>] selector : _ -> _) = s.Select (selector)
        [<CustomOperation("where", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member __.Where (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.Where (predicate)
        [<CustomOperation("takeWhile", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member __.TakeWhile (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.TakeWhile (predicate)
        [<CustomOperation("take", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member __.Take (s : Observable<_>, count : int) = s.Take (count)
        [<CustomOperation("skipWhile", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member __.SkipWhile (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.SkipWhile (predicate)
        [<CustomOperation("skip", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member __.Skip (s : Observable<_>, count : int) = s.Skip (count)
        member __.Zero () = Observable.Empty (TimeProvider.System)
        member __.Yield (value) = Observable.Return (value, TimeProvider.System)
        [<CustomOperation("count")>]
        member __.Count (s : Observable<_>) = ObservableExtensions.CountAsync (s)
        [<CustomOperation("all")>]
        member __.All (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.AllAsync (new Func<_, bool> (predicate))
        [<CustomOperation("contains")>]
        member __.Contains (s : Observable<_>, key) = s.ContainsAsync (key)
        [<CustomOperation("distinct", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member __.Distinct (s : Observable<_>) = s.Distinct ()
        [<CustomOperation("exactlyOne")>]
        member __.ExactlyOne (s : Observable<_>) = s.SingleAsync ()
        [<CustomOperation("exactlyOneOrDefault")>]
        member __.ExactlyOneOrDefault (s : Observable<_>) = s.SingleOrDefaultAsync ()
        [<CustomOperation("find")>]
        member __.Find (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.FirstAsync (new Func<_, bool> (predicate))
        [<CustomOperation("head")>]
        member __.Head (s : Observable<_>) = s.FirstAsync ()
        [<CustomOperation("headOrDefault")>]
        member __.HeadOrDefault (s : Observable<_>) = s.FirstOrDefaultAsync ()
        [<CustomOperation("last")>]
        member __.Last (s : Observable<_>) = s.LastAsync ()
        [<CustomOperation("lastOrDefault")>]
        member __.LastOrDefault (s : Observable<_>) = s.LastOrDefaultAsync ()
        [<CustomOperation("maxBy")>]
        member __.MaxBy (s : Observable<'a>, [<ProjectionParameter>] valueSelector : 'a -> 'b) = s.MaxByAsync (new Func<'a, 'b> (valueSelector))
        [<CustomOperation("minBy")>]
        member __.MinBy (s : Observable<'a>, [<ProjectionParameter>] valueSelector : 'a -> 'b) = s.MinByAsync (new Func<'a, 'b> (valueSelector))

        [<CustomOperation("sumBy")>]
        member inline __.SumBy (s : Observable<_>, [<ProjectionParameter>] valueSelector : _ -> _) =
            s
                .Select(valueSelector)
                .AggregateAsync (Unchecked.defaultof<_>, new Func<_, _, _> (fun a b -> a + b))

        [<CustomOperation("zip", IsLikeZip = true)>]
        member __.Zip (s1 : Observable<_>, s2 : Observable<_>, [<ProjectionParameter>] resultSelector : _ -> _) =
            s1.Zip (s2, new Func<_, _, _> (resultSelector))

        [<CustomOperation("iter")>]
        member __.Iter (s : Observable<_>, [<ProjectionParameter>] selector : _ -> _) = s.ForEachAsync (selector)

    let rxquery = RxQueryBuilder ()
