module FSharp.Control.R3.Observable

open R3

/// Hides the identy of an observable sequence
let inline asObservable source : Observable<'Source> = ObservableExtensions.AsObservable source

/// Binds an observable to generate a subsequent observable.
let inline bind ([<InlineIfLambda>] f : 'T -> Observable<'TNext>) source = ObservableExtensions.SelectMany (source, f)

/// Converts the elements of the sequence to the specified type
let inline cast<'T, 'CastType> (source) = ObservableExtensions.Cast<'T, 'CastType> (source)

/// Concatenates the second observable sequence to the first observable sequence
/// upn the successful termination of the first
let inline concat source = ObservableExtensions.Concat source

/// Returns an observable sequence that only contains distinct elements
let inline distinct source = ObservableExtensions.Distinct source

/// Filters the observable elements of a sequence based on a predicate
let inline filter ([<InlineIfLambda>] f : 't -> bool) source = ObservableExtensions.Where (source, f)

/// Maps the given observable with the given function
let inline map ([<InlineIfLambda>] f : 't -> 'r) source = ObservableExtensions.Select (source, f)

/// Maps the given observable with the given function and the index of the element
let inline mapi ([<InlineIfLambda>] f : int -> 't -> 'r) source = ObservableExtensions.Select (source, (fun i x -> f x i))

/// Bypasses a specified number of elements in an observable sequence and then returns the remaining elements.
let inline skip (count : int) (source) = ObservableExtensions.Skip (source, count)

/// Takes n elements (from the beginning of an observable sequence?)
let inline take (count : int) (source) = ObservableExtensions.Take (source, count)

/// Filters the observable elements of a sequence based on a predicate
let inline where ([<InlineIfLambda>] f : 't -> bool) source = ObservableExtensions.Where (source, f)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Builders =
    open System

    /// A reactive query builder.
    /// See http://mnajder.blogspot.com/2011/09/when-reactive-framework-meets-f-30.html
    type RxQueryBuilder () =
        member _.For (s : Observable<_>, body : _ -> Observable<_>) = s.SelectMany (body)
        [<CustomOperation("select", AllowIntoPattern = true)>]
        member _.Select (s : Observable<_>, [<ProjectionParameter>] selector : _ -> _) = s.Select (selector)
        [<CustomOperation("where", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.Where (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.Where (predicate)
        [<CustomOperation("takeWhile", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.TakeWhile (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.TakeWhile (predicate)
        [<CustomOperation("take", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.Take (s : Observable<_>, count : int) = s.Take (count)
        [<CustomOperation("skipWhile", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.SkipWhile (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.SkipWhile (predicate)
        [<CustomOperation("skip", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.Skip (s : Observable<_>, count : int) = s.Skip (count)
        member _.Zero () = Observable.Empty (TimeProvider.System)
        member _.Yield (value) = Observable.Return (value, TimeProvider.System)
        [<CustomOperation("count")>]
        member _.Count (s : Observable<_>) = ObservableExtensions.CountAsync (s)
        [<CustomOperation("all")>]
        member _.All (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.AllAsync (new Func<_, bool> (predicate))
        [<CustomOperation("contains")>]
        member _.Contains (s : Observable<_>, key) = s.ContainsAsync (key)
        [<CustomOperation("distinct", MaintainsVariableSpace = true, AllowIntoPattern = true)>]
        member _.Distinct (s : Observable<_>) = s.Distinct ()
        [<CustomOperation("exactlyOne")>]
        member _.ExactlyOne (s : Observable<_>) = s.SingleAsync ()
        [<CustomOperation("exactlyOneOrDefault")>]
        member _.ExactlyOneOrDefault (s : Observable<_>) = s.SingleOrDefaultAsync ()
        [<CustomOperation("find")>]
        member _.Find (s : Observable<_>, [<ProjectionParameter>] predicate : _ -> bool) = s.FirstAsync (new Func<_, bool> (predicate))
        [<CustomOperation("head")>]
        member _.Head (s : Observable<_>) = s.FirstAsync ()
        [<CustomOperation("headOrDefault")>]
        member _.HeadOrDefault (s : Observable<_>) = s.FirstOrDefaultAsync ()
        [<CustomOperation("last")>]
        member _.Last (s : Observable<_>) = s.LastAsync ()
        [<CustomOperation("lastOrDefault")>]
        member _.LastOrDefault (s : Observable<_>) = s.LastOrDefaultAsync ()
        [<CustomOperation("maxBy")>]
        member _.MaxBy (s : Observable<'a>, [<ProjectionParameter>] valueSelector : 'a -> 'b) = s.MaxByAsync (new Func<'a, 'b> (valueSelector))
        [<CustomOperation("minBy")>]
        member _.MinBy (s : Observable<'a>, [<ProjectionParameter>] valueSelector : 'a -> 'b) = s.MinByAsync (new Func<'a, 'b> (valueSelector))

        [<CustomOperation("sumBy")>]
        member inline _.SumBy (s : Observable<_>, [<ProjectionParameter>] valueSelector : _ -> _) =
            s
            |> _.Select(valueSelector)
            |> _.AggregateAsync(Unchecked.defaultof<_>, new Func<_, _, _> (fun a b -> a + b))

        [<CustomOperation("zip", IsLikeZip = true)>]
        member _.Zip (s1 : Observable<_>, s2 : Observable<_>, [<ProjectionParameter>] resultSelector : _ -> _) =
            s1.Zip (s2, new Func<_, _, _> (resultSelector))

        [<CustomOperation("iter")>]
        member _.Iter (s : Observable<_>, [<ProjectionParameter>] selector : _ -> _) = s.ForEachAsync (selector)

    let rxquery = RxQueryBuilder ()
