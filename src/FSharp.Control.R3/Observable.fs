module FSharp.Control.R3.Observable

open R3

/// Maps the given observable with the given function
let inline map (f : 't -> 'r) source = ObservableExtensions.Select (source, f)

/// Maps the given observable with the given function and the index of the element
let inline mapi (f : int -> 't -> 'r) source = ObservableExtensions.Select (source, (fun i x -> f x i))

/// Filters the observable elements of a sequence based on a predicate
let inline filter (f : 't -> bool) source = ObservableExtensions.Where (source, f)

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

/// Bypasses a specified number of elements in an observable sequence and then returns the remaining elements.
let inline skip (count : int) (source) = ObservableExtensions.Skip (source, count)

/// Takes n elements (from the beginning of an observable sequence? )
let inline take (count : int) (source) = ObservableExtensions.Take (source, count)
