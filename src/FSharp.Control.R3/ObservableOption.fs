/// <summary>
/// Variants of the functions of <see cref="T:FSharp.Control.R3.Observable"/> that work with
/// <see cref="T:Microsoft.FSharp.Core.FSharpOption`1"/> instead of <see cref="T:Microsoft.FSharp.Core.FSharpValueOption`1"/>.
/// <para>
/// The functions of <see cref="T:FSharp.Control.R3.Observable"/> that work with optional values take value options,
/// which do not allocate; use this module where options are already at hand.
/// </para>
/// </summary>
[<RequireQualifiedAccess>]
module FSharp.Control.R3.ObservableOption

open R3
open FSharp.Control.R3

/// <summary>
/// Applies the chooser to each element and emits the values of the <see cref="T:Microsoft.FSharp.Core.FSharpOption`1"/> results
/// that hold one.
/// </summary>
let inline choose ([<InlineIfLambda>] chooser : 'T -> 'R option) (source : Observable<'T>) =
    source
    |> Observable.map chooser
    |> Observable.filter Option.isSome
    |> Observable.map Option.get
