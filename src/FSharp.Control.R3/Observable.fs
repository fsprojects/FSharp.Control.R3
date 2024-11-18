module FSharp.Control.R3.Observable

open R3

let inline map (f : 't -> 'r) source = ObservableExtensions.Select (source, f)
