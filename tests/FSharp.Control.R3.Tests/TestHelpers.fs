module FSharp.Control.R3.Tests.TestHelpers

open System
open System.Threading
open R3

let createObservable (values : 'T array) = Observable.ToObservable values

let toArrayTask (source : Observable<'T>) = ObservableExtensions.ToArrayAsync source

let cancellationToken = CancellationToken.None

let stringComparer = StringComparer.OrdinalIgnoreCase
