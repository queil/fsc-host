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

[<RequireQualifiedAccess>]
module PaketPaths =
    let internal scriptRootPaketDir (scriptFilePath: string) =
        Path.Combine(scriptFilePath |> Path.GetDirectoryName, ".fsch")

    let internal mainGroupFile (tfm: string) (ext: string) = $"%s{tfm}/main.group.%s{ext}"

    let internal loadingScriptsDir scriptFilePath (tfm: string) (ext: string) =
        Path.Combine(scriptFilePath |> scriptRootPaketDir, Constants.PaketFolderName, "load", mainGroupFile tfm ext)

    let paketFilesDir = Path.Combine(".fsch", Constants.PaketFilesFolderName)

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
            tfm: string,
            runtimeIdentifier: string,
            timeout: int
        ) : obj =

        try
            let scriptExt = scriptExt[1..]

            let fschPaketDir = scriptName |> PaketPaths.scriptRootPaketDir
            let logPath = Path.Combine(fschPaketDir, "paket.log")
            let log (line) = File.AppendAllLines(logPath, [ line ])
            Directory.CreateDirectory fschPaketDir |> ignore

            log fschPaketDir

            // ensure there is always an empty deps file
            match Dependencies.TryLocate(fschPaketDir) with
            | None ->
                log "none located"
                ()
            | Some df ->
                log $"located: {df.DependenciesFile}. Deleting"
                File.Delete(df.DependenciesFile)
                log "Deleted"

            try
                Dependencies.Init(fschPaketDir)
            with ex ->
                log $"{ex.Message}"

            log "init OK"
            let depsFile = Dependencies.Locate(fschPaketDir)

            File.AppendAllText(logPath, depsFile.DependenciesFile)

            let preProcess (line: string) =
                let parsed = DependenciesFileParser.parseDependencyLine line |> Seq.toList

                let processed =
                    match parsed with
                    | "github" :: path :: tail when not <| path.Contains(":") -> "github" :: $"{path}:main" :: tail
                    | s -> s

                processed |> String.concat " "

            let depsLines =
                packageManagerTextLines
                |> Seq.map (fun (_, s) -> s.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                |> Seq.collect (id)
                |> Seq.map (fun s -> s.Trim())
                |> Seq.map (preProcess)
                |> Seq.toList

            File.AppendAllLines(depsFile.DependenciesFile, depsLines)

            try
                depsFile.Install(false)
            with ex ->
                log $"{ex.ToString()}"

            let expectedPartialPath = PaketPaths.mainGroupFile tfm scriptExt

            let data =
                depsFile.GenerateLoadScriptData depsFile.DependenciesFile [ Domain.MainGroup ] [ tfm ] [ scriptExt ]
                |> Seq.filter (fun d -> d.PartialPath = expectedPartialPath)
                |> Seq.head

            data.Save(DirectoryInfo(fschPaketDir))

            let loadingScriptsFilePath = PaketPaths.loadingScriptsDir scriptName tfm scriptExt

            File.AppendAllText(
                loadingScriptsFilePath,
                $"\n#I \"{Path.Combine(fschPaketDir, Constants.PaketFilesFolderName)}\""
            )

            ResolveDependenciesResult(true, [||], [||], [], [ loadingScriptsFilePath ], [])
        with e ->
            failwithf "Paket: %s" (string e)
