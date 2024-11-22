module Helpers

open System
open Fake.Core
open Fake.DotNet
open Fake.Tools

let releaseBranch = "main"

let environVarAsBoolOrDefault varName defaultValue =
    let truthyConsts = [ "1"; "Y"; "YES"; "T"; "TRUE" ]
    Environment.environVar varName
    |> ValueOption.ofObj
    |> ValueOption.map (fun envvar ->
        truthyConsts
        |> List.exists (fun ``const`` -> String.Equals (``const``, envvar, StringComparison.InvariantCultureIgnoreCase)))
    |> ValueOption.defaultValue defaultValue

let isRelease (targets : Target list) =
    targets
    |> Seq.map (fun t -> t.Name)
    |> Seq.exists ((=) "PublishToNuGet")

let invokeAsync f = async { f () }

let configuration (targets : Target list) =
    let defaultVal = if isRelease targets then "Release" else "Debug"

    match Environment.environVarOrDefault "CONFIGURATION" defaultVal with
    | "Debug" -> DotNet.BuildConfiguration.Debug
    | "Release" -> DotNet.BuildConfiguration.Release
    | config -> DotNet.BuildConfiguration.Custom config

let failOnBadExitAndPrint (p : ProcessResult) =
    if p.ExitCode <> 0 then
        p.Errors |> Seq.iter Trace.traceError

        failwithf "failed with exitcode %d" p.ExitCode

let isPublishToGitHub ctx = ctx.Context.FinalTarget = "PublishToGitHub"

type TargetParameter with

    member ctx.IsPublishToGitHub = isPublishToGitHub ctx

let isCI = lazy environVarAsBoolOrDefault "CI" false

// CI Servers can have bizarre failures that have nothing to do with your code
let rec retryIfInCI times fn =
    match isCI.Value with
    | true ->
        if times > 1 then
            try
                fn ()
            with _ ->
                retryIfInCI (times - 1) fn
        else
            fn ()
    | _ -> fn ()

let failOnWrongBranch () =
    if Git.Information.getBranchName "" <> releaseBranch then
        failwithf "Not on %s.  If you want to release please switch to this branch." releaseBranch
