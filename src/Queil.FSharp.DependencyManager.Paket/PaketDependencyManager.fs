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
            mainScriptName: string,
            scriptName: string,
            packageManagerTextLines: string seq,
            targetFramework: string
        ) : ResolveDependenciesResult =

        let resolveDependenciesForLanguage
            (
                fileType,
                targetFramework: string,
                prioritizedSearchPaths: string seq,
                scriptDir: string,
                scriptName: string,
                packageManagerTextLinesFromScript: string seq
            ) =

            let tmpDir = scriptName.Replace(fileType, "paket")
            Directory.CreateDirectory(tmpDir) |> ignore

            let depsFile =
                match Dependencies.TryLocate(tmpDir) with
                | None ->
                    Dependencies.Init(tmpDir)

                    let depLines =
                        packageManagerTextLinesFromScript
                        |> Seq.map (fun s -> s.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                        |> Seq.collect (id)
                        |> Seq.map (fun s -> s.Trim())

                    let depsFile = Dependencies.Locate(tmpDir)
                    File.AppendAllLines(depsFile.DependenciesFile, depLines)
                    depsFile

                | Some depsFile -> depsFile

            depsFile.Install(false)
            depsFile.GenerateLoadScripts [] [ targetFramework ] [ fileType ]

            let loadScriptsPath =
                Path.Combine(depsFile.RootPath, ".paket/load", targetFramework, $"main.group.{fileType}")

            (loadScriptsPath, [])

        let resolveDependencies
            (
                targetFramework: string,
                scriptDir: string,
                scriptName: string,
                packageManagerTextLinesFromScript: string seq
            ) =
            let extension =
                if scriptName.ToLowerInvariant().EndsWith(".fsx") then
                    "fsx"
                elif scriptName.ToLowerInvariant().EndsWith(".csx") then
                    "csx"
                else
                    // default to F# in case the calling process doesn't honour giving the script name to discriminate on
                    "fsx"

            resolveDependenciesForLanguage (
                extension,
                targetFramework,
                Seq.empty,
                scriptDir,
                scriptName,
                packageManagerTextLinesFromScript
            )

        let scriptDir =
            if scriptDir = String.Empty then
                Environment.CurrentDirectory
            else
                scriptDir

        try
            let loadScript, additionalIncludeDirs =
                resolveDependencies (targetFramework, scriptDir, scriptName, packageManagerTextLines)

            let resolutions =
                // https://github.com/dotnet/fsharp/pull/10224#issue-498147879
                // if load script causes problem
                // consider changing this to be the list of all assemblies to load rather than passing through a load script
                []

            ResolveDependenciesResult(true, [||], [||], resolutions, [ loadScript ], additionalIncludeDirs)
        with e ->
            printfn "exception while resolving dependencies: %s" (string e)
            ResolveDependenciesResult(false, [||], [||], [||], [||], [||])
