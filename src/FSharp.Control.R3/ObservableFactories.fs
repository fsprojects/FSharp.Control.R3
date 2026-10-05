namespace FSharp.Control.R3

open System.Threading
open R3

/// <summary>
/// Factories of observable sequences that are reachable through the type name <c>Observable</c>
/// as soon as <see cref="N:FSharp.Control.R3"/> is opened.
/// <para>
/// This module sits directly in the namespace, so opening the namespace opens it too.
/// Name resolution of <see cref="M:FSharp.Control.R3.ObservableFactories.Observable.ofSeq``1(System.Collections.Generic.IEnumerable{``0})"/>
/// finds no such function in the modules named <c>Observable</c> and falls through to the type
/// <see cref="T:FSharp.Control.R3.ObservableFactories.Observable"/>, the same way
/// <see cref="M:FSharp.Control.R3.Task.Extensions.Observable.ofTask``1(Microsoft.FSharp.Core.FSharpFunc{System.Threading.CancellationToken,System.Threading.Tasks.ValueTask{``0}},System.Boolean)"/>
/// resolves after opening <see cref="T:FSharp.Control.R3.Task"/>. R3's own factories, such as
/// <see cref="M:R3.Observable.Range(System.Int32,System.Int32)"/>, still resolve through the same name.
/// </para>
/// <para>
/// Where the bare type name matters, it now refers to this type: write <c>open type R3.Observable</c> rather than
/// <c>open type Observable</c>, and extend <see cref="T:R3.Observable"/> by its full name.
/// </para>
/// </summary>
[<AutoOpen>]
module ObservableFactories =

    /// Factories of observable sequences.
    [<AbstractClass; Sealed>]
    type Observable private () =

        /// <summary>
        /// Creates an observable sequence that emits the elements of <paramref name="items"/> synchronously on every subscription
        /// and then completes.
        /// <para>The sequence is enumerated to the end even when the subscriber unsubscribes earlier, so it must be finite.</para>
        /// </summary>
        static member ofSeq (items : 'T seq) : Observable<'T> = R3.Observable.ToObservable items

        /// <summary>
        /// Creates an observable sequence that emits the elements of <paramref name="items"/> synchronously on every subscription
        /// and then completes.
        /// <para>
        /// Once <paramref name="cancellationToken"/> is cancelled, no further element is emitted and the sequence completes successfully.
        /// The token is checked after the enumerator moved to the next element, so the side effects of producing that element still run.
        /// </para>
        /// </summary>
        static member ofSeq (items : 'T seq, cancellationToken : CancellationToken) : Observable<'T> =
            R3.Observable.ToObservable (items, cancellationToken)
