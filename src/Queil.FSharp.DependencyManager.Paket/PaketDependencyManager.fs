namespace Queil.FSharp.DependencyManager.Paket

open System
open System.IO
open Paket

[<AttributeUsage(AttributeTargets.Assembly ||| AttributeTargets.Class, AllowMultiple = false)>]
type DependencyManagerAttribute() =
    inherit Attribute()

module Attributes =
    [<assembly: DependencyManager>]
    do ()

type ResolveDependenciesResult
    (
        success: bool,
        stdOut: string array,
        stdError: string array,
        resolutions: string seq,
        sourceFiles: string seq,
        roots: string seq
    ) =
    member _.Success = success
    member _.StdOut = stdOut
    member _.StdError = stdError
    member _.Resolutions = resolutions
    member _.SourceFiles = sourceFiles
    member _.Roots = roots

[<DependencyManager>]
type PaketDependencyManager(outputDirectory: string option, useResultsCache: bool) =
    member _.Name = "paket"
    member _.Key = "paket"

    member _.HelpMessages: string list = []

    member _.ClearResultsCache = fun () -> ()

    member _.ResolveDependencies
        (
            scriptDir: string,
            scriptName: string,
            scriptExt: string,
            packageManagerTextLines: (string * string) seq,
            targetFrameworkMoniker: string,
            runtimeIdentifier: string,
            timeout: int
        ) : obj =

        try
            let scriptExt = scriptExt[1..];
            let paketDir = Path.Combine(scriptDir, ".paket")
            Directory.Delete(paketDir, true)
            Dependencies.Init(scriptDir)

            let depLines =
                packageManagerTextLines
                |> Seq.map (fun (_, s) -> s.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                |> Seq.collect (id)
                |> Seq.map (fun s -> s.Trim())

            let depsFile = Dependencies.Locate(scriptDir)
            File.AppendAllLines(depsFile.DependenciesFile, depLines)
            depsFile.Install(false)
            depsFile.GenerateLoadScripts [] [ targetFrameworkMoniker ] [ scriptExt ]

            let loadScriptsPath =
                Path.Combine(depsFile.RootPath, ".paket/load", targetFrameworkMoniker, $"main.group.{scriptExt}")

            ResolveDependenciesResult(true, [||], [||], [], [ loadScriptsPath ], [])
        with e ->
            printfn "exception while resolving dependencies: %s" (string e)
            ResolveDependenciesResult(false, [||], [||], [], [], [])
