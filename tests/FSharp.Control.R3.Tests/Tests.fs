namespace FSharp.Control.R3.Tests

open System
open FSharp.Control.R3
open FSharp.Control.R3.Say
open Microsoft.VisualStudio.TestTools.UnitTesting
open Swensen.Unquote

[<TestClass>]
type SayTests () =

    member _.``Add two integers`` () =
        let subject = Say.add 1 2
        test <@ subject = 3 @>
    //Assert.AreEqual (subject, 3, message = "Addition works")

    member _.``Say nothing`` () =
        let subject = Say.nothing ()
        Assert.AreEqual (subject, (), "Not an absolute unit")

    member _.``Say hello all`` () =
        let person = {
            Name = "Jean-Luc Picard"
            FavoriteNumber = 4
            FavoriteColor = Red
            DateOfBirth = DateTimeOffset.Parse ("July 13, 2305")
        }

        let subject = Say.helloPerson person

        test <@ subject = "Hello Jean-Luc Picard. You were born on 2305/07/13 and your favorite number is 4. You like Red." @>

//Assert.AreEqual<String> (
//    subject,
//    "Hello Jean-Luc Picard. You were born on 2305/07/13 and your favorite number is 4. You like Red.",
//    message = "You didn't say hello"
//)
