namespace FSharp.Control.R3.Tests

open System
open System.Threading
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3
open FSharp.Control.R3.Tests.TestHelpers

// Every source in this class delivers synchronously on the calling thread: subjects, ToObservable, Return, Range and
// Empty push inside OnNext or Subscribe, and the operators under test forward inline. So every test asserts right after
// the call that triggers a notification, without waiting, and the class needs no TestContext token.

/// <summary>
/// Operators of the <see cref="T:FSharp.Control.R3.Observable"/> module under the names of their data rows, each one
/// configured so that it passes the elements of an int source through unchanged.
/// </summary>
module private PassThrough =

    // The tests of completion, failure and OnErrorResume forwarding apply the same expectations to every operator. This
    // table is the only list of them: it feeds both the data rows and the lookup by name, so an operator added here is
    // covered by every one of those tests
    let all : struct (string * (Observable<int> -> Observable<int>)) array = [|
        struct ("asObservable", Observable.asObservable)
        struct ("bind", Observable.bind Observable.singleton)
        struct ("cast", Observable.cast<int, int>)
        // The handler type matches none of the failures in these tests, so catch forwards them
        struct ("catch", Observable.catch (fun (_ : ArgumentException) -> Observable.empty ()))
        struct ("choose", Observable.choose ValueSome)
        struct ("ObservableOption.choose", ObservableOption.choose Some)
        // An empty first sequence makes concat emit the source unchanged
        struct ("concat", Observable.concat (Observable.empty ()))
        struct ("distinct", Observable.distinct)
        struct ("filter", Observable.filter (fun _ -> true))
        struct ("map", Observable.map id)
        struct ("mapi", Observable.mapi (fun _ value -> value))
        // An empty second source completes at once, so merge completes together with the source
        struct ("merge", fun source -> Observable.merge (source, Observable.empty ()))
        struct ("ofType", Observable.ofType<int, int>)
        struct ("skip", Observable.skip 0)
        struct ("take", Observable.take Int32.MaxValue)
        struct ("where", Observable.where (fun _ -> true))
    |]

    /// Applies the operator of the data row with the name to the source.
    let apply (name : string) (source : Observable<int>) : Observable<int> =
        let struct (_, operator) =
            all
            |> Array.find (fun struct (candidate, _) -> String.Equals (candidate, name, StringComparison.Ordinal))

        operator source

/// <summary>
/// Integration tests of the functions of the <see cref="T:FSharp.Control.R3.Observable"/> module and of the
/// <see cref="T:FSharp.Control.R3.ObservableFactories.Observable"/> factories against R3.
/// <para>
/// The chunking functions are covered by <see cref="T:FSharp.Control.R3.Tests.ChunkTests"/>, and the
/// <see cref="P:FSharp.Control.R3.Observable.BuildersModule.rxquery"/> query expressions by
/// <see cref="T:FSharp.Control.R3.Tests.BuilderTests"/>.
/// </para>
/// </summary>
[<TestClass; ObservableTestCategory>]
type ObservableTests () =

    /// The names of the operators that the forwarding tests check, as MSTest dynamic data.
    static member PassThroughOperators : obj array seq =
        PassThrough.all
        |> Seq.map (fun struct (name, _) -> name)
        |> dataRows

    [<TestMethod; DynamicData(nameof ObservableTests.PassThroughOperators)>]
    member _.``operators complete successfully without values over an empty source`` (operator : string) =
        use recorder =
            (Observable.empty () : Observable<int>)
            |> PassThrough.apply operator
            |> Recorder.Attach

        Assert.IsEmpty (recorder.Values, $"%s{operator} must not emit over an empty source")
        Assert.IsEmpty (recorder.Errors, $"%s{operator} must not report an error over an empty source")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, $"%s{operator} must complete successfully when the source completes empty")

    [<TestMethod; DynamicData(nameof ObservableTests.PassThroughOperators)>]
    member _.``operators forward the terminal failure of the source`` (operator : string) =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            Sources.failingAfter [| 1; 2 |] boom
            |> PassThrough.apply operator
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1; 2 |], recorder.Values, $"%s{operator} must emit the elements that precede the failure")
        Assert.IsEmpty (recorder.Errors, $"%s{operator} must not turn the failure into an error reported through OnErrorResume")
        assertFailedWith boom recorder $"%s{operator} must complete with the failure of the source"

    [<TestMethod; DynamicData(nameof ObservableTests.PassThroughOperators)>]
    member _.``operators forward the errors that the source reports through OnErrorResume`` (operator : string) =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            Sources.resumingError boom
            |> PassThrough.apply operator
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1; 2 |], recorder.Values, $"%s{operator} must keep emitting after an error reported through OnErrorResume")
        CollectionAssert.AreEqual ([| boom |], recorder.Errors, $"%s{operator} must forward the error unchanged")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, $"%s{operator} must not turn an error reported through OnErrorResume into a failure")

    [<TestMethod>]
    member _.``asObservable hides the subject and forwards every notification`` () =
        let boom : exn = InvalidOperationException "boom"
        let failure : exn = TimeoutException "failure"
        use subject = new Subject<int> ()
        let hidden = subject |> Observable.asObservable
        use recorder = Recorder.Attach hidden

        subject.OnNext 1
        subject.OnErrorResume boom
        subject.OnCompleted (Result.Failure failure)

        // R3 AsObservable wraps the source in another observable, so a consumer cannot cast it back and push values
        Assert.IsNotInstanceOfType<ISubject<int>>(hidden, "asObservable must not expose the subject")
        CollectionAssert.AreEqual ([| 1 |], recorder.Values, "asObservable must forward the elements")
        CollectionAssert.AreEqual ([| boom |], recorder.Errors, "asObservable must forward the errors reported through OnErrorResume")
        assertFailedWith failure recorder "asObservable must forward the failure"

    [<TestMethod>]
    member _.``bind flattens synchronous inner sequences in source order`` () =
        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.bind (fun x -> Observable.Range (x * 10, 2))
            |> Recorder.Attach

        // Every Range completes inside its own Subscribe, before SelectMany receives the next element of the source
        CollectionAssert.AreEqual ([| 10; 11; 20; 21; 30; 31 |], recorder.Values, "bind must emit the inner elements in source order")
        Assert.IsEmpty (recorder.Errors, "bind must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "bind must complete when the source and every inner sequence have completed")

    [<TestMethod>]
    member _.``bind interleaves hot inner sequences and completes only after the source and every inner sequence complete`` () =
        use source = new Subject<int> ()
        use first = new Subject<string> ()
        use second = new Subject<string> ()

        use recorder =
            source
            |> Observable.bind (fun x ->
                if x = 1 then
                    first :> Observable<string>
                else
                    second :> Observable<string>
            )
            |> Recorder.Attach

        source.OnNext 1
        source.OnNext 2
        second.OnNext "b1"
        first.OnNext "a1"
        source.OnCompleted (Result.Success)
        first.OnCompleted (Result.Success)
        // R3 SelectMany completes only once the source is stopped and its last running inner sequence completes
        Assert.IsTrue (recorder.Completion.IsNone, "bind must wait for the inner sequence that is still running")

        second.OnNext "b2"
        second.OnCompleted (Result.Success)

        CollectionAssert.AreEqual ([| "b1"; "a1"; "b2" |], recorder.Values, "bind must emit the inner elements in arrival order")
        Assert.IsEmpty (recorder.Errors, "bind must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "bind must complete when the last inner sequence completes")

    [<TestMethod>]
    member _.``bind fails as soon as an inner sequence fails and unsubscribes from the source and the other inner sequences`` () =
        let boom : exn = InvalidOperationException "boom"
        use source = new Subject<int> ()
        use first = new Subject<string> ()
        use second = new Subject<string> ()
        let sourceProbe = SubscriptionProbe ()
        let secondProbe = SubscriptionProbe ()

        use recorder =
            source
            |> sourceProbe.Watch
            |> Observable.bind (fun x ->
                if x = 1 then
                    first :> Observable<string>
                else
                    secondProbe.Watch second
            )
            |> Recorder.Attach

        source.OnNext 1
        source.OnNext 2
        first.OnNext "a1"
        // The source is still open here: R3 SelectMany drops an inner failure that arrives after the source has completed
        first.OnCompleted (Result.Failure boom)

        CollectionAssert.AreEqual ([| "a1" |], recorder.Values, "bind must forward only the inner element that arrived before the failure")
        Assert.IsEmpty (recorder.Errors, "bind must not turn the failure into an error reported through OnErrorResume")
        assertFailedWith boom recorder "bind must fail with the failure of the inner sequence"
        // The completed Recorder ignores every later notification, so it cannot show that bind stopped forwarding; the source
        // and the second inner sequence are open subjects, so these disposals prove the unsubscription
        Assert.AreEqual (1, sourceProbe.Disposed, "bind must unsubscribe from the source when an inner sequence fails")
        Assert.AreEqual (1, secondProbe.Disposed, "bind must unsubscribe from the other inner sequences when one fails")

    [<TestMethod>]
    member _.``disposing a bind subscription unsubscribes from the source and every inner sequence`` () =
        use source = new Subject<int> ()
        use inner = new Subject<string> ()
        let sourceProbe = SubscriptionProbe ()
        let innerProbe = SubscriptionProbe ()

        use recorder =
            source
            |> sourceProbe.Watch
            |> Observable.bind (fun _ -> innerProbe.Watch inner)
            |> Recorder.Attach

        source.OnNext 1
        inner.OnNext "a"
        CollectionAssert.AreEqual ([| "a" |], recorder.Values, "bind must forward the inner element while subscribed")
        recorder.Dispose ()
        source.OnNext 2

        // The disposed Recorder ignores every notification, so only the probes can show the unsubscription; both subjects
        // stay open, so these disposals prove it
        Assert.AreEqual (1, sourceProbe.Disposed, "Disposing the subscription must unsubscribe from the source")
        Assert.AreEqual (1, innerProbe.Subscribed, "A source element pushed after the disposal must not subscribe to an inner sequence")
        Assert.AreEqual (1, innerProbe.Disposed, "Disposing the subscription must unsubscribe from the running inner sequence")

    [<TestMethod>]
    member _.``cast converts matching elements and reports the others as InvalidCastException without stopping`` () =
        use recorder =
            Sources.values [| "a" :> obj; 1 :> obj; "b" :> obj |]
            |> Observable.cast<obj, string>
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| "a"; "b" |], recorder.Values, "cast must emit the elements of the target type")
        // R3 Cast throws inside OnNextCore, and Observer.OnNext reports the exception through OnErrorResume
        let error =
            Assert.ContainsSingle (recorder.Errors, "cast must report exactly the one element that cannot be cast")
        Assert.IsInstanceOfType<InvalidCastException>(error, "cast must report an InvalidCastException")
        |> ignore
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "cast must continue after an invalid element and complete with the source")

    [<TestMethod>]
    member _.``catch switches to the handler sequence on a matching failure and keeps the earlier values`` () =
        let boom : exn = InvalidOperationException "boom"
        let handled = ResizeArray<exn>()

        use recorder =
            Sources.failingAfter [| 1; 2 |] boom
            |> Observable.catch (fun (error : InvalidOperationException) ->
                handled.Add error
                Sources.values [| 9 |]
            )
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1; 2; 9 |], recorder.Values, "catch must keep the elements before the failure and append the handler sequence")
        Assert.IsEmpty (recorder.Errors, "catch must not report the handled failure as an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "catch must complete with the handler sequence")
        CollectionAssert.AreEqual ([| boom |], handled.ToArray (), "The handler must be called once with the failure of the source")

    [<TestMethod>]
    member _.``catch forwards a failure whose type does not match the handler`` () =
        let boom : exn = InvalidOperationException "boom"
        let handlerCalls = ref 0

        use recorder =
            Sources.failingAfter [| 1 |] boom
            |> Observable.catch (fun (_ : ArgumentException) ->
                handlerCalls.Value <- handlerCalls.Value + 1
                Sources.values [| 9 |]
            )
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1 |], recorder.Values, "catch must keep the elements before the failure")
        Assert.IsEmpty (recorder.Errors, "catch must not turn the failure into an error reported through OnErrorResume")
        assertFailedWith boom recorder "catch must forward a failure of another type unchanged"
        Assert.AreEqual (0, handlerCalls.Value, "The handler must not be called for a failure of another type")

    [<TestMethod>]
    member _.``catch with an unannotated handler catches a failure of any type`` () =
        let failure : exn = TimeoutException "failure"
        // box returns objnull, the nullable obj of F# nullness checking
        let handled = ResizeArray<objnull>()

        use recorder =
            Sources.failingAfter [| 1 |] failure
            // Without an annotation F# infers the handler argument as obj, and R3 Catch puts no Exception constraint on it,
            // so its type test accepts every failure
            |> Observable.catch (fun error ->
                handled.Add (box error)
                Sources.values [| 0 |]
            )
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1; 0 |], recorder.Values, "catch must switch to the handler sequence")
        Assert.IsEmpty (recorder.Errors, "catch must not report the handled failure as an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "catch must complete with the handler sequence")
        CollectionAssert.AreEqual ([| box failure |], handled.ToArray (), "The handler must be called once with the failure of the source")

    [<TestMethod>]
    member _.``catch does not intercept errors reported through OnErrorResume`` () =
        let boom : exn = InvalidOperationException "boom"
        let handlerCalls = ref 0

        use recorder =
            Sources.resumingError boom
            |> Observable.catch (fun (_ : exn) ->
                handlerCalls.Value <- handlerCalls.Value + 1
                Sources.values [| 99 |]
            )
            |> Recorder.Attach

        // R3 Catch forwards OnErrorResume unchanged and handles only a failed completion
        CollectionAssert.AreEqual ([| 1; 2 |], recorder.Values, "catch must keep emitting the source after an error reported through OnErrorResume")
        CollectionAssert.AreEqual ([| boom |], recorder.Errors, "catch must forward the error unchanged")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "catch must complete with the source")
        Assert.AreEqual (0, handlerCalls.Value, "The handler must not be called for an error reported through OnErrorResume")

    [<TestMethod>]
    member _.``catch fails with the failure of the handler sequence`` () =
        let boom : exn = InvalidOperationException "boom"
        let handlerFailure : exn = InvalidOperationException "handler failure"
        let handlerCalls = ref 0

        use recorder =
            Sources.failingAfter [| 1 |] boom
            |> Observable.catch (fun (_ : InvalidOperationException) ->
                handlerCalls.Value <- handlerCalls.Value + 1
                Sources.failingAfter [| 2 |] handlerFailure
            )
            |> Recorder.Attach

        // R3 forwards the completion of the handler sequence as it is, so a failure of a matching type is not handled again
        CollectionAssert.AreEqual ([| 1; 2 |], recorder.Values, "catch must emit the source and then the handler sequence")
        Assert.IsEmpty (recorder.Errors, "catch must not report either failure as an error")
        assertFailedWith handlerFailure recorder "catch must fail with the failure of the handler sequence"
        Assert.AreEqual (1, handlerCalls.Value, "The handler must be called only for the failure of the source")

    [<TestMethod>]
    member _.``concat subscribes to the second sequence only after the first completes`` () =
        use first = new Subject<int> ()
        use second = new Subject<int> ()
        let secondProbe = SubscriptionProbe ()

        use recorder =
            Observable.concat first (secondProbe.Watch second)
            |> Recorder.Attach

        // Nobody listens to the second subject yet, and a subject drops the elements pushed while nobody is subscribed
        second.OnNext 0
        first.OnNext 1
        Assert.AreEqual (0, secondProbe.Subscribed, "concat must not subscribe to the second sequence while the first is running")

        first.OnCompleted (Result.Success)
        Assert.AreEqual (1, secondProbe.Subscribed, "concat must subscribe to the second sequence when the first completes")
        Assert.IsTrue (recorder.Completion.IsNone, "concat must not complete before the second sequence completes")

        second.OnNext 2
        second.OnCompleted (Result.Success)

        CollectionAssert.AreEqual ([| 1; 2 |], recorder.Values, "concat must emit the first sequence and then the second")
        Assert.IsEmpty (recorder.Errors, "concat must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "concat must complete when the second sequence completes")

    [<TestMethod>]
    member _.``concat in a pipeline emits its argument first, like R3 Concat called on the argument`` () =
        let earlier = Sources.values [| 1; 2 |]
        let later = Sources.values [| 3; 4 |]

        use recorder = later |> Observable.concat earlier |> Recorder.Attach
        use reference = earlier |> _.Concat(later) |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1; 2; 3; 4 |], recorder.Values, "a |> concat b must emit b before a")
        CollectionAssert.AreEqual (reference.Values, recorder.Values, "a |> concat b must emit what b.Concat(a) emits")
        Assert.IsEmpty (recorder.Errors, "concat must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "concat must complete when both sequences have completed")

    [<TestMethod>]
    member _.``concat stops at a failure of the first sequence without subscribing to the second`` () =
        let boom : exn = InvalidOperationException "boom"
        use second = new Subject<int> ()
        let secondProbe = SubscriptionProbe ()

        use recorder =
            Observable.concat (Sources.failingAfter [| 1 |] boom) (secondProbe.Watch second)
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1 |], recorder.Values, "concat must emit the elements before the failure")
        Assert.IsEmpty (recorder.Errors, "concat must not turn the failure into an error reported through OnErrorResume")
        assertFailedWith boom recorder "concat must fail with the failure of the first sequence"
        Assert.AreEqual (0, secondProbe.Subscribed, "concat must not subscribe to the second sequence after a failure")

    [<TestMethod>]
    member _.``disposing a concat subscription unsubscribes from the first sequence and never subscribes to the second`` () =
        use first = new Subject<int> ()
        use second = new Subject<int> ()
        let firstProbe = SubscriptionProbe ()
        let secondProbe = SubscriptionProbe ()

        use recorder =
            Observable.concat (firstProbe.Watch first) (secondProbe.Watch second)
            |> Recorder.Attach

        first.OnNext 1
        CollectionAssert.AreEqual ([| 1 |], recorder.Values, "concat must forward the first sequence while subscribed")
        recorder.Dispose ()
        // The disposed Recorder ignores every notification, so only the probes can show the unsubscription. R3 also
        // disposes the probe's Do wrapper when its source completes, so the disposal is checked while the first subject
        // is still open
        Assert.AreEqual (1, firstProbe.Disposed, "Disposing the subscription must unsubscribe from the first sequence")

        first.OnCompleted (Result.Success)
        Assert.AreEqual (0, secondProbe.Subscribed, "The first sequence completing after the disposal must not subscribe to the second")

    [<TestMethod>]
    member _.``distinct drops repeated elements and keeps the first occurrences in order`` () =
        use recorder =
            Sources.values [| 1; 2; 1; 3; 2 |]
            |> Observable.distinct
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1; 2; 3 |], recorder.Values, "distinct must emit only the first occurrence of every element")
        Assert.IsEmpty (recorder.Errors, "distinct must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "distinct must complete with the source")

    [<TestMethod>]
    member _.``empty completes successfully on subscription without emitting`` () =
        use recorder = Recorder.Attach (Observable.empty () : Observable<int>)

        // R3 Empty completes inside Subscribe, so the outcome is known as soon as Attach returns
        Assert.IsEmpty (recorder.Values, "empty must not emit")
        Assert.IsEmpty (recorder.Errors, "empty must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "empty must complete successfully on subscription")

    [<TestMethod>]
    member _.``filter keeps the matching elements that a hot subject emits after subscription`` () =
        use subject = new Subject<int> ()
        let evens = subject |> Observable.filter (fun x -> x % 2 = 0)
        // A subject drops the elements pushed while nobody is subscribed
        subject.OnNext 2
        use recorder = Recorder.Attach evens

        subject.OnNext 3
        subject.OnNext 4
        subject.OnNext 5

        CollectionAssert.AreEqual ([| 4 |], recorder.Values, "filter must emit only the matching elements pushed after subscription")
        Assert.IsTrue (recorder.Completion.IsNone, "filter must stay open while the subject is open")
        subject.OnCompleted (Result.Success)
        Assert.IsEmpty (recorder.Errors, "filter must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "filter must complete with the subject")

    [<TestMethod>]
    member _.``filter reports a predicate exception through OnErrorResume and keeps filtering`` () =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            Sources.values [| 1; 2; 3; 4; 5 |]
            |> Observable.filter (fun x -> if x = 3 then raise boom else x % 2 = 0)
            |> Recorder.Attach

        // Observer.OnNext reports an exception of the predicate through OnErrorResume instead of failing the sequence
        CollectionAssert.AreEqual ([| 2; 4 |], recorder.Values, "filter must keep filtering after the predicate throws")
        CollectionAssert.AreEqual ([| boom |], recorder.Errors, "filter must report the exception of the predicate")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "filter must complete with the source")

    [<TestMethod>]
    member _.``where and filter emit the same elements as R3 Where`` () =
        let source = Sources.values [| 1; 2; 3; 4; 5; 6 |]

        use whereRecorder =
            source
            |> Observable.where (fun x -> x % 2 = 0)
            |> Recorder.Attach
        use filterRecorder =
            source
            |> Observable.filter (fun x -> x % 2 = 0)
            |> Recorder.Attach
        use reference = source |> _.Where(fun x -> x % 2 = 0) |> Recorder.Attach

        CollectionAssert.AreEqual ([| 2; 4; 6 |], reference.Values, "R3 Where must keep the even elements")
        CollectionAssert.AreEqual (reference.Values, whereRecorder.Values, "where must emit what R3 Where emits")
        CollectionAssert.AreEqual (reference.Values, filterRecorder.Values, "filter must emit what R3 Where emits")
        Assert.IsEmpty (whereRecorder.Errors, "where must not report an error")
        Assert.IsEmpty (filterRecorder.Errors, "filter must not report an error")
        Assert.IsTrue (whereRecorder.IsCompletedSuccessfully, "where must complete with the source")
        Assert.IsTrue (filterRecorder.IsCompletedSuccessfully, "filter must complete with the source")

    [<TestMethod>]
    member _.``map projects every element in order`` () =
        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.map (fun x -> x * 10)
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 10; 20; 30 |], recorder.Values, "map must project every element in order")
        Assert.IsEmpty (recorder.Errors, "map must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "map must complete with the source")

    [<TestMethod>]
    member _.``map reports a selector exception through OnErrorResume and keeps projecting`` () =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.map (fun x -> if x = 2 then raise boom else x * 10)
            |> Recorder.Attach

        // Observer.OnNext reports an exception of the selector through OnErrorResume instead of failing the sequence
        CollectionAssert.AreEqual ([| 10; 30 |], recorder.Values, "map must keep projecting after the selector throws")
        CollectionAssert.AreEqual ([| boom |], recorder.Errors, "map must report the exception of the selector")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "map must complete with the source")

    [<TestMethod>]
    member _.``mapi passes the zero-based index first and the element second, like R3 indexed Select`` () =
        let source = Sources.values [| "a"; "b"; "c" |]

        use recorder =
            source
            |> Observable.mapi (fun index value -> $"%d{index}:%s{value}")
            |> Recorder.Attach

        use reference =
            source
            |> _.Select(fun value index -> $"%d{index}:%s{value}")
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| "0:a"; "1:b"; "2:c" |], recorder.Values, "mapi must pass the zero-based index and then the element")
        CollectionAssert.AreEqual (reference.Values, recorder.Values, "mapi must emit what R3 indexed Select emits")
        Assert.IsEmpty (recorder.Errors, "mapi must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "mapi must complete with the source")

    [<TestMethod>]
    member _.``mapi counts the index separately for every subscription`` () =
        use subject = new Subject<string> ()
        let indexed =
            subject
            |> Observable.mapi (fun index value -> $"%d{index}:%s{value}")

        use first = Recorder.Attach indexed
        subject.OnNext "a"
        use second = Recorder.Attach indexed
        subject.OnNext "b"
        subject.OnNext "c"
        subject.OnCompleted (Result.Success)

        // R3 indexed Select keeps the index in its observer, so every subscription counts from zero
        CollectionAssert.AreEqual ([| "0:a"; "1:b"; "2:c" |], first.Values, "The first subscription must count every element it received")
        CollectionAssert.AreEqual ([| "0:b"; "1:c" |], second.Values, "A later subscription must count from zero")
        Assert.IsEmpty (first.Errors, "mapi must not report an error to the first subscription")
        Assert.IsEmpty (second.Errors, "mapi must not report an error to the later subscription")
        Assert.IsTrue (first.IsCompletedSuccessfully, "mapi must complete the first subscription with the subject")
        Assert.IsTrue (second.IsCompletedSuccessfully, "mapi must complete the later subscription with the subject")

    [<TestMethod>]
    member _.``merge forwards both sources in emission order and completes after both complete`` () =
        use left = new Subject<int> ()
        use right = new Subject<int> ()
        use recorder = Observable.merge (left, right) |> Recorder.Attach

        left.OnNext 1
        right.OnNext 2
        left.OnNext 3
        left.OnCompleted (Result.Success)
        Assert.IsTrue (recorder.Completion.IsNone, "merge must stay open while the other source is running")

        right.OnNext 4
        right.OnCompleted (Result.Success)

        CollectionAssert.AreEqual ([| 1; 2; 3; 4 |], recorder.Values, "merge must emit the elements of both sources in emission order")
        Assert.IsEmpty (recorder.Errors, "merge must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "merge must complete when both sources have completed")

    [<TestMethod>]
    member _.``merge fails as soon as either source fails and unsubscribes from the other`` () =
        let boom : exn = InvalidOperationException "boom"
        use left = new Subject<int> ()
        use right = new Subject<int> ()
        let leftProbe = SubscriptionProbe ()

        use recorder =
            Observable.merge (leftProbe.Watch left, right)
            |> Recorder.Attach

        left.OnNext 1
        right.OnCompleted (Result.Failure boom)

        CollectionAssert.AreEqual ([| 1 |], recorder.Values, "merge must forward only the element that arrived before the failure")
        Assert.IsEmpty (recorder.Errors, "merge must not turn the failure into an error reported through OnErrorResume")
        assertFailedWith boom recorder "merge must fail with the failure of either source"
        // The completed Recorder ignores every later notification, so it cannot show that merge stopped forwarding; the left
        // subject stays open, so this disposal proves that the failure tore its subscription down
        Assert.AreEqual (1, leftProbe.Disposed, "merge must unsubscribe from the other source when one fails")

    [<TestMethod>]
    member _.``disposing a merge subscription unsubscribes from both sources`` () =
        use left = new Subject<int> ()
        use right = new Subject<int> ()
        let probe = SubscriptionProbe ()

        use recorder =
            Observable.merge (probe.Watch left, probe.Watch right)
            |> Recorder.Attach

        left.OnNext 1
        CollectionAssert.AreEqual ([| 1 |], recorder.Values, "merge must forward the elements while subscribed")
        recorder.Dispose ()

        Assert.AreEqual (2, probe.Subscribed, "merge must subscribe to both sources")
        // The disposed Recorder ignores every notification, so only the probe can show the unsubscription; both subjects
        // stay open, so these disposals prove it
        Assert.AreEqual (2, probe.Disposed, "Disposing the subscription must unsubscribe from both sources")

    [<TestMethod>]
    member _.``ofType keeps the elements of the target type and silently drops the others`` () =
        use recorder =
            Sources.values [| "a" :> obj; 1 :> obj; "b" :> obj |]
            |> Observable.ofType<obj, string>
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| "a"; "b" |], recorder.Values, "ofType must emit the elements of the target type")
        // Unlike Cast, R3 OfType filters with a type test, so the other elements are not reported as errors
        Assert.IsEmpty (recorder.Errors, "ofType must drop the other elements without reporting an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "ofType must complete with the source")

    [<TestMethod>]
    member _.``singleton emits its value synchronously to every subscriber and completes`` () =
        let single = Observable.singleton 42

        // R3 Return without a time provider emits and completes inside Subscribe
        use first = Recorder.Attach single
        CollectionAssert.AreEqual ([| 42 |], first.Values, "singleton must emit its value when subscribed")
        Assert.IsEmpty (first.Errors, "singleton must not report an error")
        Assert.IsTrue (first.IsCompletedSuccessfully, "singleton must complete right after its value")

        use second = Recorder.Attach single
        CollectionAssert.AreEqual ([| 42 |], second.Values, "singleton must emit its value again to a later subscriber")
        Assert.IsEmpty (second.Errors, "singleton must not report an error to a later subscriber")
        Assert.IsTrue (second.IsCompletedSuccessfully, "singleton must complete again for a later subscriber")

    [<TestMethod>]
    member _.``skip bypasses the first elements and emits the rest`` () =
        use recorder =
            Sources.values [| 1; 2; 3; 4; 5 |]
            |> Observable.skip 2
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 3; 4; 5 |], recorder.Values, "skip must bypass the first two elements")
        Assert.IsEmpty (recorder.Errors, "skip must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "skip must complete with the source")

    [<TestMethod>]
    member _.``skip more elements than the source emits completes without values`` () =
        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.skip 10
            |> Recorder.Attach

        Assert.IsEmpty (recorder.Values, "skip must bypass every element of a shorter source")
        Assert.IsEmpty (recorder.Errors, "skip must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "skip must complete with the source")

    [<TestMethod>]
    member _.``take emits the first elements and completes`` () =
        use recorder =
            Sources.values [| 1; 2; 3; 4; 5 |]
            |> Observable.take 2
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1; 2 |], recorder.Values, "take must emit the first two elements")
        Assert.IsEmpty (recorder.Errors, "take must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "take must complete after the last taken element")

    [<TestMethod>]
    member _.``take completes and unsubscribes from a hot source after the last taken value`` () =
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()

        use recorder =
            subject
            |> probe.Watch
            |> Observable.take 2
            |> Recorder.Attach

        subject.OnNext 1
        Assert.IsTrue (recorder.Completion.IsNone, "take must stay open before the last taken element")
        Assert.AreEqual (0, probe.Disposed, "take must keep its subscription before the last taken element")

        subject.OnNext 2

        CollectionAssert.AreEqual ([| 1; 2 |], recorder.Values, "take must emit the first two elements")
        Assert.IsEmpty (recorder.Errors, "take must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "take must complete right after the last taken element")
        // Nothing is pushed after the last taken element: the completed Recorder would ignore it. The subject never
        // completes by itself, so this disposal proves that take unsubscribed
        Assert.AreEqual (1, probe.Disposed, "take must unsubscribe from the source after the last taken element")

    [<TestMethod>]
    member _.``take zero completes at once without subscribing to the source`` () =
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()

        use recorder =
            subject
            |> probe.Watch
            |> Observable.take 0
            |> Recorder.Attach

        // R3 Take returns Observable.Empty for a count of zero, so the source is never subscribed
        Assert.IsEmpty (recorder.Values, "take 0 must not emit")
        Assert.IsEmpty (recorder.Errors, "take 0 must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "take 0 must complete on subscription")
        Assert.AreEqual (0, probe.Subscribed, "take 0 must not subscribe to the source")

    [<TestMethod>]
    member _.``skip and take reject a negative count when called`` () =
        use subject = new Subject<int> ()

        // R3 validates the count when the operator is created, before anything subscribes
        Assert.Throws<ArgumentOutOfRangeException>(Action (fun () -> subject |> Observable.skip (-1) |> ignore), "skip must reject a negative count")
        |> ignore

        Assert.Throws<ArgumentOutOfRangeException>(Action (fun () -> subject |> Observable.take (-1) |> ignore), "take must reject a negative count")
        |> ignore

    [<TestMethod>]
    member _.``choose emits the values of ValueSome results and calls the chooser once per element`` () =
        let calls = ref 0

        use recorder =
            Sources.values [| 1; 2; 3; 4; 5; 6 |]
            |> Observable.choose (fun x ->
                calls.Value <- calls.Value + 1
                if x % 2 = 0 then ValueSome (x * 10) else ValueNone
            )
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 20; 40; 60 |], recorder.Values, "choose must emit the values of the ValueSome results")
        Assert.AreEqual (6, calls.Value, "choose must call the chooser exactly once per element")
        Assert.IsEmpty (recorder.Errors, "choose must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "choose must complete with the source")

    [<TestMethod>]
    member _.``ObservableOption choose emits the values of Some results and calls the chooser once per element`` () =
        let calls = ref 0

        use recorder =
            Sources.values [| 1; 2; 3; 4; 5; 6 |]
            |> ObservableOption.choose (fun x ->
                calls.Value <- calls.Value + 1
                if x % 2 = 0 then Some (x * 10) else None
            )
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 20; 40; 60 |], recorder.Values, "ObservableOption.choose must emit the values of the Some results")
        Assert.AreEqual (6, calls.Value, "ObservableOption.choose must call the chooser exactly once per element")
        Assert.IsEmpty (recorder.Errors, "ObservableOption.choose must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "ObservableOption.choose must complete with the source")

    [<TestMethod>]
    member _.``choose forwards the failure of a hot source`` () =
        let boom : exn = InvalidOperationException "boom"
        use subject = new Subject<int> ()

        use recorder =
            subject
            |> Observable.choose (fun x -> if x > 1 then ValueSome (x * 10) else ValueNone)
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        subject.OnCompleted (Result.Failure boom)

        CollectionAssert.AreEqual ([| 20 |], recorder.Values, "choose must emit the values chosen before the failure")
        Assert.IsEmpty (recorder.Errors, "choose must not turn the failure into an error reported through OnErrorResume")
        assertFailedWith boom recorder "choose must fail with the failure of the source"

    [<TestMethod>]
    member _.``ObservableOption choose forwards the failure of a hot source`` () =
        let boom : exn = InvalidOperationException "boom"
        use subject = new Subject<int> ()

        use recorder =
            subject
            |> ObservableOption.choose (fun x -> if x > 1 then Some (x * 10) else None)
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        subject.OnCompleted (Result.Failure boom)

        CollectionAssert.AreEqual ([| 20 |], recorder.Values, "ObservableOption.choose must emit the values chosen before the failure")
        Assert.IsEmpty (recorder.Errors, "ObservableOption.choose must not turn the failure into an error reported through OnErrorResume")
        assertFailedWith boom recorder "ObservableOption.choose must fail with the failure of the source"

    [<TestMethod>]
    member _.``choose reports a chooser exception through OnErrorResume and keeps choosing`` () =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> Observable.choose (fun x -> if x = 2 then raise boom else ValueSome (x * 10))
            |> Recorder.Attach

        // choose maps through R3 Select, whose observer reports an exception of the chooser through OnErrorResume
        CollectionAssert.AreEqual ([| 10; 30 |], recorder.Values, "choose must keep choosing after the chooser throws")
        CollectionAssert.AreEqual ([| boom |], recorder.Errors, "choose must report the exception of the chooser")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "choose must complete with the source")

    [<TestMethod>]
    member _.``ObservableOption choose reports a chooser exception through OnErrorResume and keeps choosing`` () =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            Sources.values [| 1; 2; 3 |]
            |> ObservableOption.choose (fun x -> if x = 2 then raise boom else Some (x * 10))
            |> Recorder.Attach

        // ObservableOption.choose maps through R3 Select as well, so an exception of the chooser is reported through OnErrorResume
        CollectionAssert.AreEqual ([| 10; 30 |], recorder.Values, "ObservableOption.choose must keep choosing after the chooser throws")
        CollectionAssert.AreEqual ([| boom |], recorder.Errors, "ObservableOption.choose must report the exception of the chooser")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "ObservableOption.choose must complete with the source")

    [<TestMethod>]
    member _.``ofSeq emits the elements of a sequence synchronously and completes`` () =
        use recorder = Recorder.Attach (Observable.ofSeq [ 1; 2; 3 ])

        // R3 ToObservable enumerates inside Subscribe, so the outcome is known as soon as Attach returns
        CollectionAssert.AreEqual ([| 1; 2; 3 |], recorder.Values, "ofSeq must emit the elements of the sequence in order")
        Assert.IsEmpty (recorder.Errors, "ofSeq must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "ofSeq must complete after the last element")

    [<TestMethod>]
    member _.``ofSeq applied in a pipeline emits the elements of the sequence`` () =
        use recorder = [ 1; 2; 3 ] |> Observable.ofSeq |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1; 2; 3 |], recorder.Values, "A piped ofSeq must emit the elements of the sequence in order")
        Assert.IsEmpty (recorder.Errors, "A piped ofSeq must not report an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "A piped ofSeq must complete after the last element")

    [<TestMethod>]
    member _.``ofSeq enumerates the sequence again for every subscription`` () =
        let enumerations = ref 0

        let items = seq {
            enumerations.Value <- enumerations.Value + 1
            yield! [ 1; 2 ]
        }

        let source = Observable.ofSeq items
        use first = Recorder.Attach source
        use second = Recorder.Attach source

        Assert.AreEqual (2, enumerations.Value, "ofSeq must enumerate the sequence once per subscription")
        CollectionAssert.AreEqual ([| 1; 2 |], first.Values, "The first subscription must receive every element")
        CollectionAssert.AreEqual ([| 1; 2 |], second.Values, "A later subscription must receive every element again")
        Assert.IsEmpty (first.Errors, "ofSeq must not report an error to the first subscription")
        Assert.IsEmpty (second.Errors, "ofSeq must not report an error to the later subscription")
        Assert.IsTrue (first.IsCompletedSuccessfully, "ofSeq must complete the first subscription")
        Assert.IsTrue (second.IsCompletedSuccessfully, "ofSeq must complete the later subscription")

    [<TestMethod>]
    member _.``ofSeq over an empty sequence completes successfully without emitting`` () =
        use recorder = Recorder.Attach (Observable.ofSeq Array.empty<int>)

        Assert.IsEmpty (recorder.Values, "ofSeq must not emit for an empty sequence")
        Assert.IsEmpty (recorder.Errors, "ofSeq must not report an error for an empty sequence")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "ofSeq must complete successfully for an empty sequence")

    [<TestMethod>]
    member _.``ofSeq with an already cancelled token completes successfully without emitting`` () =
        use cancellation = new CancellationTokenSource ()
        cancellation.Cancel ()

        use recorder =
            Observable.ofSeq ([ 1; 2; 3 ], cancellation.Token)
            |> Recorder.Attach

        // R3 ToObservable checks the token before emitting every element and then completes with Success,
        // not with a cancellation
        Assert.IsEmpty (recorder.Values, "ofSeq must not emit when its token is already cancelled")
        Assert.IsEmpty (recorder.Errors, "ofSeq must not report the cancellation as an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "ofSeq must complete successfully when its token is already cancelled")

    [<TestMethod>]
    member _.``ofSeq completes successfully when its token is cancelled during the enumeration`` () =
        use cancellation = new CancellationTokenSource ()

        let items = seq {
            yield 1
            yield 2
            cancellation.Cancel ()
            yield 3
            yield 4
        }

        use recorder =
            Observable.ofSeq (items, cancellation.Token)
            |> Recorder.Attach

        // R3 ToObservable checks the token after taking each element from the enumerator and before emitting it,
        // so the element produced after the cancellation is not emitted
        CollectionAssert.AreEqual ([| 1; 2 |], recorder.Values, "ofSeq must stop emitting once its token is cancelled")
        Assert.IsEmpty (recorder.Errors, "ofSeq must not report the cancellation as an error")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "ofSeq must complete successfully when its token is cancelled")

    [<TestMethod>]
    member _.``ofSeq lets an exception of the enumeration escape from Subscribe`` () =
        let boom : exn = InvalidOperationException "boom"

        let items = seq {
            yield 1
            raise boom
        }

        let source = Observable.ofSeq items
        use recorder = new Recorder<int> ()

        // R3 ToObservable has no try/catch around the enumeration: the exception escapes from Subscribe instead of failing
        // the sequence, and Subscribe disposes the observer before rethrowing it
        let thrown =
            Assert.Throws<InvalidOperationException>(
                Action (fun () -> source.Subscribe recorder |> ignore),
                "The exception of the enumeration must escape from Subscribe"
            )

        Assert.AreSame<exn>(boom, thrown, "Subscribe must rethrow the exception of the enumeration itself")
        CollectionAssert.AreEqual ([| 1 |], recorder.Values, "The elements before the exception must have been emitted")
        Assert.IsEmpty (recorder.Errors, "The exception must not be reported through OnErrorResume")
        Assert.IsTrue (recorder.Completion.IsNone, "The exception must not complete the sequence")

    [<TestMethod>]
    member _.``ofSeq enumerates the whole sequence even after a downstream take has completed`` () =
        let pulled = ref 0

        let items = seq {
            for i in 1..5 do
                pulled.Value <- pulled.Value + 1
                yield i
        }

        use recorder =
            Observable.ofSeq items
            |> Observable.take 2
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 1; 2 |], recorder.Values, "take must emit only the first two elements")
        Assert.IsEmpty (recorder.Errors, "No error may be reported")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "take must complete after the last taken element")
        // R3 ToObservable checks only its own token, never whether the observer was disposed, so it keeps pulling the
        // remaining elements after take has completed; an infinite sequence would never return from Subscribe
        Assert.AreEqual (5, pulled.Value, "ofSeq must enumerate the sequence to the end")

    [<TestMethod>]
    member _.``filter, map and take compose on a hot subject and unsubscribe after the last taken value`` () =
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()

        use recorder =
            subject
            |> probe.Watch
            |> Observable.filter (fun x -> x % 2 = 0)
            |> Observable.map (fun x -> x * 10)
            |> Observable.take 2
            |> Recorder.Attach

        subject.OnNext 1
        subject.OnNext 2
        Assert.IsTrue (recorder.Completion.IsNone, "The pipeline must stay open until two values have passed the filter")
        subject.OnNext 3
        subject.OnNext 4

        CollectionAssert.AreEqual ([| 20; 40 |], recorder.Values, "Only the even values, multiplied by 10, must arrive")
        Assert.IsEmpty (recorder.Errors, "No error may be reported")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "take must complete successfully after its second value")
        // Nothing is pushed after the second value: the completed Recorder would ignore it. The subject never completes by
        // itself, so this disposal proves that take unsubscribed from the whole chain
        Assert.AreEqual (1, probe.Disposed, "take must dispose its upstream subscription once satisfied")
