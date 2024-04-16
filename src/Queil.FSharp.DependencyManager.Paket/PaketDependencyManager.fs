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
    /// This method gets called by fsch twice. First for GetProjectOptionsFromScript, then for the actual compile
    member _.ResolveDependencies
        (
            // DO NOT USE. It is only correct for GetProjectOptionsFromScript. Incorrect for Compile (points to the current working dir)
            // Use the directory from scriptName
            scriptDir: string,
            scriptName: string,
            scriptExt: string,
            packageManagerTextLines: (string * string) seq,
            targetFrameworkMoniker: string,
            runtimeIdentifier: string,
            timeout: int
        ) : obj =

        try
            let scriptExt = scriptExt[1..]
            let fschPaketDir = Path.Combine(Path.GetDirectoryName scriptName, ".paket-fsch")
            Dependencies.Init(fschPaketDir)

            let depsFile = Dependencies.Locate(fschPaketDir)

            let newLines =
                packageManagerTextLines
                |> Seq.map (fun (x, s) -> printfn "%s" x; s.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                |> Seq.collect (id)
                |> Seq.map (fun s -> s.Trim())
                |> Seq.filter (fun line -> depsFile.GetDependenciesFile().Lines |> Array.contains(line) |> not)

            File.AppendAllLines(depsFile.DependenciesFile, newLines)
            depsFile.Install(false)

            let data =
                depsFile.GenerateLoadScriptData
                    depsFile.DependenciesFile
                    [ Domain.MainGroup ]
                    [ targetFrameworkMoniker ]
                    [ scriptExt ]
                |> Seq.filter (fun d -> d.PartialPath = $"{targetFrameworkMoniker}/main.group.{scriptExt}")
                |> Seq.head

            let loadScriptsPath = Path.Combine(depsFile.RootPath)
            Directory.CreateDirectory(loadScriptsPath) |> ignore
            data.Save(DirectoryInfo(loadScriptsPath))

            ResolveDependenciesResult(
                true,
                [||],
                [||],
                [],
                [ Path.Combine(loadScriptsPath, Constants.PaketFolderName, "load", data.PartialPath) ],
                []
            )
        with e ->
            printfn "exception while resolving dependencies: %s" (string e)
            ResolveDependenciesResult(false, [||], [||], [], [], [])
