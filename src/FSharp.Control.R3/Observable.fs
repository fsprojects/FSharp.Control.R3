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
