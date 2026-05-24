module FSharp.Control.R3.Tests.TestHelpers

open System
open System.Threading
open System.Threading.Tasks
open R3

let createObservable (values : 'T array) = Observable.ToObservable values

let toArrayTask (source : Observable<'T>) = ObservableExtensions.ToArrayAsync source

let waitTask (task : Task<'T>) = task.Result

let waitAsync (work : Async<'T>) = Async.RunSynchronously work

let cancellationToken = CancellationToken.None

let stringComparer = StringComparer.OrdinalIgnoreCase
