namespace FSharp.Control.R3.Tests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.VisualStudio.TestTools.UnitTesting
open R3
open FSharp.Control.R3.Observable.Builders
open FSharp.Control.R3.Task
open FSharp.Control.R3.Tests.TestHelpers

// Public, because F# solves the (+) constraint of the inline sumBy only with public members
/// Types that the builder tests aggregate.
[<AutoOpen>]
module BuilderTestTypes =

    /// <summary>
    /// An amount of money: a reference type whose addition reads both operands,
    /// so adding an amount to a <see langword="null"/> seed throws a <see cref="T:System.NullReferenceException"/>.
    /// </summary>
    type Money = {
        Cents : int64
    } with

        /// Adds two amounts.
        static member (+) (left : Money, right : Money) = { Cents = left.Cents + right.Cents }

/// <summary>
/// Integration tests of the <see cref="T:FSharp.Control.R3.Observable.BuildersModule.RxQueryBuilder"/> query expressions over R3.
/// <para>
/// <see cref="M:FSharp.Control.R3.Observable.BuildersModule.RxQueryBuilder.Yield``1(``0)"/> and
/// <see cref="M:FSharp.Control.R3.Observable.BuildersModule.RxQueryBuilder.Zero``1"/> emit synchronously, so a query over
/// synchronous sources keeps the order of the elements and completes during the subscription: the tests assert right after
/// building or collecting such a query, without waiting.
/// </para>
/// </summary>
[<TestClass; BuilderTestCategory>]
type BuilderTests (testContext : TestContext) =

    // Collects a query over synchronous sources. Such a query completes during the subscription, so the task is already
    // completed here; a faulted task rethrows its original exception when the test awaits it.
    let collect (query : Observable<'T>) =
        let valuesTask = query |> Observable.toArray testContext.CancellationToken
        Assert.IsTrue (valuesTask.IsCompleted, "A query over synchronous sources must complete during the subscription")
        valuesTask

    [<TestMethod>]
    member _.``rxquery for and select project every element in order`` () : Task = task {
        let query = rxquery {
            for x in Sources.values [| 1..6 |] do
                select (x * 10)
        }

        let! values = collect query
        CollectionAssert.AreEqual ([| 10; 20; 30; 40; 50; 60 |], values, "select must project every element in the order of the source")
    }

    [<TestMethod>]
    member _.``rxquery over a synchronous source completes synchronously`` () : Task = task {
        let countTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                count
        }

        // Yield is Observable.Return, which emits during the subscription; when it was scheduled on TimeProvider.System,
        // every element hopped to the thread pool and the task completed only later
        Assert.IsTrue (countTask.IsCompletedSuccessfully, "count over a synchronous source must complete before the query returns")
        let! elementCount = countTask
        Assert.AreEqual (6, elementCount, "count must count every element of the source")
    }

    [<TestMethod>]
    member _.``rxquery where keeps only the elements that satisfy the predicate`` () : Task = task {
        let query = rxquery {
            for x in Sources.values [| 1..6 |] do
                where (x % 2 = 0)
                select x
        }

        let! values = collect query
        CollectionAssert.AreEqual ([| 2; 4; 6 |], values, "where must keep the even elements in order")
    }

    [<TestMethod>]
    member _.``rxquery if-then yield skips the other elements through Zero`` () : Task = task {
        let query = rxquery {
            for x in Sources.values [| 1..6 |] do
                if x % 2 = 0 then
                    yield x
        }

        let! values = collect query
        CollectionAssert.AreEqual ([| 2; 4; 6 |], values, "An if-then without else must yield the even elements and skip the odd ones")
    }

    [<TestMethod>]
    member _.``rxquery nested for produces the cartesian product in order`` () : Task = task {
        let query = rxquery {
            for x in Sources.values [| 1; 2 |] do
                for y in Sources.values [| "a"; "b" |] do
                    yield struct (x, y)
        }

        // Every inner query emits and completes during its subscription, so SelectMany emits it whole before the next outer element
        let! pairs = collect query
        CollectionAssert.AreEqual (
            [| struct (1, "a"); struct (1, "b"); struct (2, "a"); struct (2, "b") |],
            pairs,
            "Nested for must pair every outer element with every inner element, outer element first"
        )
    }

    [<TestMethod>]
    member _.``rxquery takeWhile completes at the first element that fails the predicate`` () : Task = task {
        let query = rxquery {
            for x in Sources.values [| 1; 2; 3; 4; 1 |] do
                takeWhile (x < 4)
        }

        // The trailing 1 satisfies the predicate again, but TakeWhile has completed at 4
        let! values = collect query
        CollectionAssert.AreEqual ([| 1; 2; 3 |], values, "takeWhile must stop at the first element that fails the predicate")
    }

    [<TestMethod>]
    member _.``rxquery take emits the first elements then completes and unsubscribes from a hot source`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()

        let valuesTask =
            rxquery {
                for x in probe.Watch subject do
                    take 2
            }
            |> Observable.toArray testContext.CancellationToken

        subject.OnNext 1
        Assert.IsFalse (valuesTask.IsCompleted, "take must wait for its second element")
        subject.OnNext 2
        // Take completes at its last element and the completed terminal disposes the query down to the subject;
        // the subject never completes by itself, so the probe proves the unsubscription
        Assert.IsTrue (valuesTask.IsCompletedSuccessfully, "take must complete at its second element")
        Assert.AreEqual (1, probe.Disposed, "Completing take must dispose the subscription to the subject")
        let! values = valuesTask.WaitAsync testContext.CancellationToken
        CollectionAssert.AreEqual ([| 1; 2 |], values, "take must emit the first two elements in order")
    }

    [<TestMethod>]
    member _.``rxquery skipWhile bypasses only the leading elements that satisfy the predicate`` () : Task = task {
        let query = rxquery {
            for x in Sources.values [| 1; 2; 3; 1 |] do
                skipWhile (x < 3)
        }

        let! values = collect query
        CollectionAssert.AreEqual ([| 3; 1 |], values, "skipWhile must emit every element from the first one that fails the predicate")
    }

    [<TestMethod>]
    member _.``rxquery skip bypasses the first elements`` () : Task = task {
        let query = rxquery {
            for x in Sources.values [| 1..6 |] do
                skip 4
        }

        let! values = collect query
        CollectionAssert.AreEqual ([| 5; 6 |], values, "skip must bypass the first four elements")
    }

    [<TestMethod>]
    member _.``rxquery distinct removes repeated elements and keeps the first occurrences in order`` () : Task = task {
        let query = rxquery {
            for x in Sources.values [| 1; 2; 1; 3; 2 |] do
                distinct
        }

        let! values = collect query
        CollectionAssert.AreEqual ([| 1; 2; 3 |], values, "distinct must emit every element once, at its first occurrence")
    }

    [<TestMethod>]
    member _.``rxquery count counts the elements that pass the query`` () : Task = task {
        let matchingTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                where (x > 2)
                count
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                count
        }

        let! matching = matchingTask.WaitAsync testContext.CancellationToken
        let! emptyCount = emptyTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (4, matching, "count must count the elements that pass where")
        Assert.AreEqual (0, emptyCount, "count over an empty source must be 0")
    }

    [<TestMethod>]
    member _.``rxquery all returns false at the first failing element and unsubscribes from a hot source`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()

        let allTask = rxquery {
            for x in probe.Watch subject do
                all (x < 3)
        }

        subject.OnNext 1
        subject.OnNext 2
        Assert.IsFalse (allTask.IsCompleted, "all must wait while every element satisfies the predicate")
        subject.OnNext 3
        // R3 AllAsync decides at the first failing element and disposes its subscription, which unsubscribes from the subject;
        // the subject never completes by itself, so the probe proves the short-circuit
        Assert.IsTrue (allTask.IsCompletedSuccessfully, "all must complete at the first failing element, before the source completes")
        Assert.AreEqual (1, probe.Disposed, "all must dispose the subscription to the source at the first failing element")
        let! result = allTask.WaitAsync testContext.CancellationToken
        Assert.IsFalse (result, "all must be false when an element fails the predicate")
    }

    [<TestMethod>]
    member _.``rxquery all returns true when every element satisfies the predicate or the source is empty`` () : Task = task {
        let everyTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                all (x > 0)
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                all (x > 0)
        }

        let! every = everyTask.WaitAsync testContext.CancellationToken
        let! overEmpty = emptyTask.WaitAsync testContext.CancellationToken
        Assert.IsTrue (every, "all must be true when every element satisfies the predicate")
        // R3 AllAsync completes with true when the source completes without a failing element
        Assert.IsTrue (overEmpty, "all over an empty source must be true")
    }

    [<TestMethod>]
    member _.``rxquery contains finds an element and returns false otherwise`` () : Task = task {
        let presentTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                contains 3
        }

        let missingTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                contains 42
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                contains 3
        }

        let! present = presentTask.WaitAsync testContext.CancellationToken
        let! missing = missingTask.WaitAsync testContext.CancellationToken
        let! inEmpty = emptyTask.WaitAsync testContext.CancellationToken
        Assert.IsTrue (present, "contains must find an element of the source")
        Assert.IsFalse (missing, "contains must be false for an element that is not in the source")
        Assert.IsFalse (inEmpty, "contains over an empty source must be false")
    }

    [<TestMethod>]
    member _.``rxquery exactlyOne returns the only element and fails for an empty source`` () : Task = task {
        let singleTask = rxquery {
            for x in Sources.values [| 7 |] do
                exactlyOne
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                exactlyOne
        }

        // R3 SingleAsync fails with an InvalidOperationException when the source completes without an element
        let! _ =
            Assert.ThrowsExactlyAsync<InvalidOperationException>(
                (fun () -> emptyTask.WaitAsync testContext.CancellationToken :> Task),
                "exactlyOne over an empty source must fail"
            )
        let! single = singleTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (7, single, "exactlyOne must return the only element")
    }

    [<TestMethod>]
    member _.``rxquery exactlyOne fails at the second element of a hot source without waiting for completion`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()

        let singleTask = rxquery {
            for x in probe.Watch subject do
                exactlyOne
        }

        subject.OnNext 7
        Assert.IsFalse (singleTask.IsCompleted, "exactlyOne must wait for the source to complete after the first element")
        subject.OnNext 8
        // R3 SingleAsync fails as soon as a second element arrives and disposes its subscription;
        // the subject never completes by itself, so the probe proves the unsubscription
        Assert.IsTrue (singleTask.IsFaulted, "exactlyOne must fail at the second element, before the source completes")
        Assert.AreEqual (1, probe.Disposed, "exactlyOne must dispose the subscription to the source at the second element")
        let! _ =
            Assert.ThrowsExactlyAsync<InvalidOperationException>(
                (fun () -> singleTask.WaitAsync testContext.CancellationToken :> Task),
                "exactlyOne over two elements must fail"
            )
        ()
    }

    [<TestMethod>]
    member _.``rxquery exactlyOneOrDefault returns the default only for an empty source`` () : Task = task {
        let singleTask = rxquery {
            for x in Sources.values [| 7 |] do
                exactlyOneOrDefault
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                exactlyOneOrDefault
        }

        let manyTask = rxquery {
            for x in Sources.values [| 1; 2; 3 |] do
                exactlyOneOrDefault
        }

        // R3 SingleOrDefaultAsync applies the default value to an empty source only; a second element still fails
        let! _ =
            Assert.ThrowsExactlyAsync<InvalidOperationException>(
                (fun () -> manyTask.WaitAsync testContext.CancellationToken :> Task),
                "exactlyOneOrDefault over several elements must fail"
            )

        let! single = singleTask.WaitAsync testContext.CancellationToken
        let! orDefault = emptyTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (7, single, "exactlyOneOrDefault must return the only element")
        Assert.AreEqual (0, orDefault, "exactlyOneOrDefault over an empty source must return the default value")
    }

    [<TestMethod>]
    member _.``rxquery find returns the first matching element and fails when none matches`` () : Task = task {
        let foundTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                find (x > 2)
        }

        let missingTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                find (x > 9)
        }

        // R3 FirstAsync with a predicate fails with an InvalidOperationException when the source completes without a match
        let! _ =
            Assert.ThrowsExactlyAsync<InvalidOperationException>(
                (fun () -> missingTask.WaitAsync testContext.CancellationToken :> Task),
                "find without a matching element must fail"
            )
        let! found = foundTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (3, found, "find must return the first element that satisfies the predicate")
    }

    [<TestMethod>]
    member _.``rxquery head returns the first element and fails for an empty source`` () : Task = task {
        let headTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                head
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                head
        }

        // R3 FirstAsync fails with an InvalidOperationException when the source completes without an element
        let! _ =
            Assert.ThrowsExactlyAsync<InvalidOperationException>(
                (fun () -> emptyTask.WaitAsync testContext.CancellationToken :> Task),
                "head over an empty source must fail"
            )
        let! first = headTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (1, first, "head must return the first element")
    }

    [<TestMethod>]
    member _.``rxquery head completes at the first element of a hot source and unsubscribes`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()

        let headTask = rxquery {
            for x in probe.Watch subject do
                head
        }

        Assert.IsFalse (headTask.IsCompleted, "head must wait for the first element")
        subject.OnNext 5
        // R3 FirstAsync completes at the first element and disposes its subscription;
        // the subject never completes by itself, so the probe proves the unsubscription
        Assert.IsTrue (headTask.IsCompletedSuccessfully, "head must complete at the first element, before the source completes")
        Assert.AreEqual (1, probe.Disposed, "head must dispose the subscription to the source at the first element")
        let! first = headTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (5, first, "head must return the first element pushed after the subscription")
    }

    [<TestMethod>]
    member _.``rxquery headOrDefault returns the first element or the default for an empty source`` () : Task = task {
        let headTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                headOrDefault
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                headOrDefault
        }

        let! first = headTask.WaitAsync testContext.CancellationToken
        let! orDefault = emptyTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (1, first, "headOrDefault must return the first element")
        Assert.AreEqual (0, orDefault, "headOrDefault over an empty source must return the default value")
    }

    [<TestMethod>]
    member _.``rxquery last returns the last element and fails for an empty source`` () : Task = task {
        let lastTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                last
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                last
        }

        // R3 LastAsync fails with an InvalidOperationException when the source completes without an element
        let! _ =
            Assert.ThrowsExactlyAsync<InvalidOperationException>(
                (fun () -> emptyTask.WaitAsync testContext.CancellationToken :> Task),
                "last over an empty source must fail"
            )
        let! final = lastTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (6, final, "last must return the last element")
    }

    [<TestMethod>]
    member _.``rxquery lastOrDefault returns the last element or the default for an empty source`` () : Task = task {
        let lastTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                lastOrDefault
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                lastOrDefault
        }

        let! final = lastTask.WaitAsync testContext.CancellationToken
        let! orDefault = emptyTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (6, final, "lastOrDefault must return the last element")
        Assert.AreEqual (0, orDefault, "lastOrDefault over an empty source must return the default value")
    }

    [<TestMethod>]
    member _.``rxquery maxBy and minBy return the first element with the extreme projected value`` () : Task = task {
        // "ccc" and "eee" share the largest length, "a" and "f" the smallest one
        let words = Sources.values [| "bb"; "a"; "ccc"; "dd"; "eee"; "f" |]

        let longestTask = rxquery {
            for word in words do
                maxBy word.Length
        }

        let shortestTask = rxquery {
            for word in words do
                minBy word.Length
        }

        let! longest = longestTask.WaitAsync testContext.CancellationToken
        let! shortest = shortestTask.WaitAsync testContext.CancellationToken
        // R3 MaxByAsync and MinByAsync replace the current element only on a strictly larger or smaller key, so the first one wins ties
        Assert.AreEqual ("ccc", longest, "maxBy must return the first of the longest words")
        Assert.AreEqual ("a", shortest, "minBy must return the first of the shortest words")
    }

    [<TestMethod>]
    member _.``rxquery maxBy and minBy fail for an empty source`` () : Task = task {
        let words = Sources.values Array.empty<string>

        let longestTask = rxquery {
            for word in words do
                maxBy word.Length
        }

        let shortestTask = rxquery {
            for word in words do
                minBy word.Length
        }

        // R3 MaxByAsync and MinByAsync fail with an InvalidOperationException when the source has no element
        let! _ =
            Assert.ThrowsExactlyAsync<InvalidOperationException>(
                (fun () -> longestTask.WaitAsync testContext.CancellationToken :> Task),
                "maxBy over an empty source must fail"
            )
        let! _ =
            Assert.ThrowsExactlyAsync<InvalidOperationException>(
                (fun () -> shortestTask.WaitAsync testContext.CancellationToken :> Task),
                "minBy over an empty source must fail"
            )
        ()
    }

    [<TestMethod>]
    member _.``rxquery sumBy adds integer float and TimeSpan values`` () : Task = task {
        let numbers = Sources.values [| 1; 2; 3; 4 |]

        let integersTask = rxquery {
            for x in numbers do
                sumBy x
        }

        let halvesTask = rxquery {
            for x in numbers do
                sumBy (float x / 2.0)
        }

        let durationsTask = rxquery {
            for x in numbers do
                sumBy (TimeSpan.FromSeconds (float x))
        }

        let! integers = integersTask.WaitAsync testContext.CancellationToken
        let! halves = halvesTask.WaitAsync testContext.CancellationToken
        let! durations = durationsTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (10, integers, "sumBy must add the integers")
        Assert.AreEqual (5.0, halves, "sumBy must add the floats")
        // TimeSpan has an addition operator but no Zero member, which the first-element seed does not need
        Assert.AreEqual (TimeSpan.FromSeconds 10.0, durations, "sumBy must add the TimeSpan values")
    }

    [<TestMethod>]
    member _.``rxquery sumBy returns the default value for an empty source`` () : Task = task {
        let integersTask = rxquery {
            for x in Sources.values Array.empty<int> do
                sumBy x
        }

        let floatsTask = rxquery {
            for x in Sources.values Array.empty<float> do
                sumBy x
        }

        let durationsTask = rxquery {
            for x in Sources.values Array.empty<TimeSpan> do
                sumBy x
        }

        let! integers = integersTask.WaitAsync testContext.CancellationToken
        let! floats = floatsTask.WaitAsync testContext.CancellationToken
        let! durations = durationsTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual (0, integers, "sumBy of no integer must be 0")
        Assert.AreEqual (0.0, floats, "sumBy of no float must be 0.0")
        Assert.AreEqual (TimeSpan.Zero, durations, "sumBy of no TimeSpan must be TimeSpan.Zero")
    }

    [<TestMethod>]
    member _.``rxquery sumBy adds a record type that defines the addition operator`` () : Task = task {
        let amounts = Sources.values [| { Cents = 150L }; { Cents = 250L }; { Cents = 100L } |]

        let totalTask = rxquery {
            for amount in amounts do
                sumBy amount
        }

        // Regression: the sum was seeded with Unchecked.defaultof, a null Money, so the first addition threw a
        // NullReferenceException and faulted the task; the first element seeds the sum now
        let! total = totalTask.WaitAsync testContext.CancellationToken
        Assert.AreEqual ({ Cents = 500L }, total, "sumBy must add the amounts with their addition operator")
    }

    [<TestMethod>]
    member _.``rxquery zip pairs elements by position and completes with the shorter source`` () : Task = task {
        let shorterQuery = rxquery {
            for x in Sources.values [| 1; 2; 3 |] do
                zip letter in Sources.values [| "a"; "b"; "c"; "d" |]
                select (struct (x, letter))
        }

        let shorterOther = rxquery {
            for x in Sources.values [| 1..6 |] do
                zip letter in Sources.values [| "a"; "b" |]
                select (struct (x, letter))
        }

        // R3 Zip pairs the queued elements of both sources in arrival order and completes once a completed source has no
        // queued element left, so the extra elements of the longer source are dropped and collect sees the completion
        let! pairs = collect shorterQuery
        let! otherPairs = collect shorterOther
        CollectionAssert.AreEqual (
            [| struct (1, "a"); struct (2, "b"); struct (3, "c") |],
            pairs,
            "zip must pair by position and complete with the shorter query"
        )

        CollectionAssert.AreEqual (
            [| struct (1, "a"); struct (2, "b") |],
            otherPairs,
            "zip must pair by position and complete with the shorter other source"
        )
    }

    [<TestMethod>]
    member _.``rxquery zip subscribes to the query before the zipped source`` () =
        let subscriptions = ResizeArray<string>()
        // Every source is synchronous, so the subscriptions are recorded on the test thread in the order they happen
        let recordSubscription name (source : Observable<'T>) = source.Do (onSubscribe = (fun () -> subscriptions.Add name))

        use recorder =
            rxquery {
                for x in Sources.values [| 1; 2 |] |> recordSubscription "query" do
                    zip letter in Sources.values [| "a"; "b" |] |> recordSubscription "zipped"
                    select (struct (x, letter))
            }
            |> Recorder.Attach

        // R3 Zip subscribes its first source before its second one, so this order shows that the builder passes the query
        // as the first source; the pairs cannot show it, because Zip queues the elements of each source and pairs them by
        // position whichever source emits first
        CollectionAssert.AreEqual ([| "query"; "zipped" |], subscriptions.ToArray (), "zip must subscribe to the query before the zipped source")
        CollectionAssert.AreEqual ([| struct (1, "a"); struct (2, "b") |], recorder.Values, "zip must pair the elements by position")

    [<TestMethod>]
    member _.``rxquery zip fails with the failure of the query when both sources fail on subscription`` () =
        let queryFailure : exn = InvalidOperationException "query"
        let zippedFailure : exn = InvalidOperationException "zipped"

        use recorder =
            rxquery {
                for x in Sources.failingAfter Array.empty<int> queryFailure do
                    zip letter in Sources.failingAfter Array.empty<string> zippedFailure
                    select (struct (x, letter))
            }
            |> Recorder.Attach

        // R3 Zip subscribes the query first and fails at the first failure of either source; the zipped source, subscribed
        // after that, fails an observer that Zip has already disposed
        assertFailedWith queryFailure recorder "zip must fail with the failure of the query, which it subscribes first"

    [<TestMethod>]
    member _.``rxquery iter runs the action for every element in order`` () : Task = task {
        let seen = ResizeArray<int>()
        let untouched = ResizeArray<int>()

        let iterTask = rxquery {
            for x in Sources.values [| 1..6 |] do
                iter (seen.Add x)
        }

        let emptyTask = rxquery {
            for x in Sources.values Array.empty<int> do
                iter (untouched.Add x)
        }

        do! iterTask.WaitAsync testContext.CancellationToken
        do! emptyTask.WaitAsync testContext.CancellationToken
        CollectionAssert.AreEqual ([| 1..6 |], seen.ToArray (), "iter must run the action for every element in order")
        Assert.IsEmpty (untouched, "iter over an empty source must never run the action")
    }

    [<TestMethod>]
    member _.``rxquery iter faults with the exception of the action and stops processing`` () : Task = task {
        let boom : exn = InvalidOperationException "boom"
        let seen = ResizeArray<int>()

        let iterTask = rxquery {
            for x in Sources.values [| 1; 2; 3 |] do
                iter (if x = 2 then raise boom else seen.Add x)
        }

        // R3 reports the exception of the action through OnErrorResume, which faults ForEachAsync and disposes it,
        // so the elements after it never reach the action
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> iterTask.WaitAsync testContext.CancellationToken),
                "iter must fault when the action throws"
            )

        Assert.AreSame (boom, error, "iter must fault with the exception of the action")
        CollectionAssert.AreEqual ([| 1 |], seen.ToArray (), "iter must stop processing at the element whose action throws")
    }

    [<TestMethod>]
    member _.``rxquery over a hot subject only sees elements pushed after subscription`` () : Task = task {
        use subject = new Subject<int> ()

        let query = rxquery {
            for i in subject do
                where (i % 2 = 0)
                select i
        }

        // Building the query does not subscribe, and a subject drops the elements nobody listens to, so 2 is lost
        subject.OnNext 2
        let valuesTask = query |> Observable.toArray testContext.CancellationToken
        subject.OnNext 3
        subject.OnNext 4
        subject.OnNext 5
        Assert.IsFalse (valuesTask.IsCompleted, "The query must stay open while the subject is open")
        subject.OnCompleted (Result.Success)
        Assert.IsTrue (valuesTask.IsCompletedSuccessfully, "Completing the subject must complete the query synchronously")
        let! values = valuesTask.WaitAsync testContext.CancellationToken
        CollectionAssert.AreEqual ([| 4 |], values, "Only the even element pushed after the subscription must arrive")
    }

    [<TestMethod>]
    member _.``a failing source faults a task operator of rxquery with the same exception`` () : Task = task {
        let boom : exn = InvalidOperationException "boom"

        let countTask = rxquery {
            for x in Sources.failingAfter [| 1; 2 |] boom do
                count
        }

        // SelectMany forwards the failure of the source at once, and an R3 task operator faults with the exception of a failure
        Assert.IsTrue (countTask.IsFaulted, "count must fault as soon as the synchronous source fails")
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> countTask.WaitAsync testContext.CancellationToken :> Task),
                "Awaiting count over a failing source must throw"
            )
        Assert.AreSame (boom, error, "count must fault with the exception of the source failure")
    }

    [<TestMethod>]
    member _.``a failing source fails an observable rxquery after its earlier elements`` () =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            rxquery {
                for x in Sources.failingAfter [| 1; 2 |] boom do
                    select (x * 10)
            }
            |> Recorder.Attach

        CollectionAssert.AreEqual ([| 10; 20 |], recorder.Values, "The elements emitted before the failure must arrive")
        Assert.IsEmpty (recorder.Errors, "A terminal failure must not be reported through OnErrorResume")
        assertFailedWith boom recorder "The query must fail with the exception of the source failure"

    [<TestMethod>]
    member _.``an error resumed by the source faults a task operator of rxquery`` () : Task = task {
        let boom : exn = InvalidOperationException "boom"

        let countTask = rxquery {
            for x in Sources.resumingError boom do
                count
        }

        // An R3 task operator turns the first OnErrorResume into a fault and disposes its subscription
        Assert.IsTrue (countTask.IsFaulted, "count must fault at the first resumed error")
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> countTask.WaitAsync testContext.CancellationToken :> Task),
                "Awaiting count after a resumed error must throw"
            )
        Assert.AreSame (boom, error, "count must fault with the resumed exception")
    }

    [<TestMethod>]
    member _.``an error resumed by the source passes through an observable rxquery`` () =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            rxquery {
                for x in Sources.resumingError boom do
                    select (x * 10)
            }
            |> Recorder.Attach

        // SelectMany and Select forward OnErrorResume without stopping, so the element after the error still arrives
        CollectionAssert.AreEqual ([| 10; 20 |], recorder.Values, "The elements around the resumed error must arrive")
        let error = Assert.ContainsSingle (recorder.Errors, "The resumed error must be forwarded once")
        Assert.AreSame (boom, error, "The forwarded error must be the resumed exception")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "A resumed error must not terminate the query")

    [<TestMethod>]
    member _.``a throwing select faults toArray of the rxquery with the same exception`` () : Task = task {
        let boom : exn = InvalidOperationException "boom"

        let query = rxquery {
            for x in Sources.values [| 1; 2; 3 |] do
                select (if x = 2 then raise boom else x * 10)
        }

        let valuesTask = query |> Observable.toArray testContext.CancellationToken
        // R3 reports the exception of the selector through OnErrorResume, which ToArrayAsync turns into a fault
        Assert.IsTrue (valuesTask.IsFaulted, "toArray must fault at the element whose projection throws")
        let! error =
            Assert.ThrowsAsync<InvalidOperationException>(
                (fun () -> valuesTask.WaitAsync testContext.CancellationToken :> Task),
                "Awaiting toArray of the query must throw"
            )
        Assert.AreSame (boom, error, "toArray must fault with the exception of the selector")
    }

    [<TestMethod>]
    member _.``a throwing select is resumed by an observable rxquery and the later elements still arrive`` () =
        let boom : exn = InvalidOperationException "boom"

        use recorder =
            rxquery {
                for x in Sources.values [| 1; 2; 3 |] do
                    select (if x = 2 then raise boom else x * 10)
            }
            |> Recorder.Attach

        // R3 Select turns the exception of the selector into OnErrorResume and keeps projecting
        CollectionAssert.AreEqual ([| 10; 30 |], recorder.Values, "The elements whose projection succeeds must arrive")
        let error =
            Assert.ContainsSingle (recorder.Errors, "The exception of the selector must be resumed once")
        Assert.AreSame (boom, error, "The resumed error must be the exception of the selector")
        Assert.IsTrue (recorder.IsCompletedSuccessfully, "An exception of the selector must not terminate the query")

    [<TestMethod>]
    member _.``rxqueryWith cancels head and unsubscribes from the source when its token is cancelled`` () : Task = task {
        use subject = new Subject<int> ()
        let probe = SubscriptionProbe ()
        use cancellation = CancellationTokenSource.CreateLinkedTokenSource testContext.CancellationToken

        // The documented form: the application of rxqueryWith must be parenthesized in front of the query
        let headTask =
            (rxqueryWith cancellation.Token)
                { for x in probe.Watch subject do
                    head }

        Assert.AreEqual (1, probe.Active, "head must subscribe to the source when the query is built")
        Assert.IsFalse (headTask.IsCompleted, "head must wait for the first element")
        // R3 registers on the token before subscribing; the callback runs inside Cancel,
        // disposes the subscription and then cancels the task
        cancellation.Cancel ()
        Assert.AreEqual (TaskStatus.Canceled, headTask.Status, "Cancel must cancel the task before it returns")
        Assert.AreEqual (1, probe.Disposed, "Cancel must dispose the subscription to the subject, which never completes by itself")

        // Awaited directly rather than through WaitAsync: the task is already cancelled and must carry its own token
        let! error =
            Assert.ThrowsAsync<OperationCanceledException>((fun () -> headTask :> Task), "Awaiting the cancelled head must throw")

        Assert.AreEqual (cancellation.Token, error.CancellationToken, "The exception must carry the token of rxqueryWith")
    }

    [<TestMethod>]
    member _.``rxqueryWith passes its token to every query operator that returns a task`` () =
        use cancellation = new CancellationTokenSource ()
        cancellation.Cancel ()
        let cancelledQuery = rxqueryWith cancellation.Token
        let source = Sources.values [| 3; 1; 2 |]
        let processed = ResizeArray<int>()

        // R3 registers on the token before it subscribes, and registering on a cancelled token runs the callback at once,
        // so every operator that observes the token returns a cancelled task even over a synchronous source
        let assertCancelled name (operatorTask : #Task) =
            Assert.AreEqual (TaskStatus.Canceled, operatorTask.Status, $"%s{name} must be cancelled by the token of rxqueryWith")

        cancelledQuery {
            for x in source do
                count
        }
        |> assertCancelled "count"

        cancelledQuery {
            for x in source do
                all (x > 0)
        }
        |> assertCancelled "all"

        cancelledQuery {
            for x in source do
                contains 1
        }
        |> assertCancelled "contains"

        cancelledQuery {
            for x in source do
                exactlyOne
        }
        |> assertCancelled "exactlyOne"

        cancelledQuery {
            for x in source do
                exactlyOneOrDefault
        }
        |> assertCancelled "exactlyOneOrDefault"

        cancelledQuery {
            for x in source do
                find (x > 0)
        }
        |> assertCancelled "find"

        cancelledQuery {
            for x in source do
                head
        }
        |> assertCancelled "head"

        cancelledQuery {
            for x in source do
                headOrDefault
        }
        |> assertCancelled "headOrDefault"

        cancelledQuery {
            for x in source do
                last
        }
        |> assertCancelled "last"

        cancelledQuery {
            for x in source do
                lastOrDefault
        }
        |> assertCancelled "lastOrDefault"

        cancelledQuery {
            for x in source do
                maxBy x
        }
        |> assertCancelled "maxBy"

        cancelledQuery {
            for x in source do
                minBy x
        }
        |> assertCancelled "minBy"

        cancelledQuery {
            for x in source do
                sumBy x
        }
        |> assertCancelled "sumBy"

        cancelledQuery {
            for x in source do
                iter (processed.Add x)
        }
        |> assertCancelled "iter"

        // The observer is disposed before the subscription, so the synchronous elements never reach the action
        Assert.IsEmpty (processed, "No element may reach iter once its token is cancelled")

    [<TestMethod>]
    member _.``RxQueryBuilder exposes its token while rxquery uses an uncancellable token`` () =
        use cancellation = new CancellationTokenSource ()
        let builder = RxQueryBuilder cancellation.Token
        let withToken = rxqueryWith cancellation.Token
        let withoutToken = RxQueryBuilder ()

        Assert.AreEqual (cancellation.Token, builder.CancellationToken, "RxQueryBuilder must expose the token it was created with")
        Assert.AreEqual (cancellation.Token, withToken.CancellationToken, "rxqueryWith must create a builder with its token")
        Assert.AreEqual (CancellationToken.None, rxquery.CancellationToken, "rxquery must not observe any token")
        Assert.AreEqual (CancellationToken.None, withoutToken.CancellationToken, "The parameterless RxQueryBuilder must not observe any token")
