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
    let internal scriptRootPaketDir (scriptDir: string) = Path.Combine(scriptDir, ".fsch")

    let internal mainGroupFile (tfm: string) (ext: string) = $"%s{tfm}/main.group.%s{ext}"

    let internal loadingScriptsDir (scriptDir: string) (tfm: string) (ext: string) =
        Path.Combine(scriptDir |> scriptRootPaketDir, Constants.PaketFolderName, "load", mainGroupFile tfm ext)

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
            scriptDir: string,
            scriptName: string,
            scriptExt: string,
            packageManagerTextLines: (string * string) seq,
            tfm: string,
            runtimeIdentifier: string,
            timeout: int
        ) : obj =

        let logPath = "/tmp/paket.log"
        let log (line) = () //File.AppendAllLines(logPath, [ line ])

        try
            let scriptExt = scriptExt[1..]
            let fschPaketDir = Directory.GetCurrentDirectory() |> PaketPaths.scriptRootPaketDir
            Directory.CreateDirectory fschPaketDir |> ignore

            log $"WORK DIR: {fschPaketDir}"
            log $"------- SCRIPT: {scriptName} {scriptDir} ----------"

            match Dependencies.TryLocate(fschPaketDir) with
            | Some df -> File.Delete df.DependenciesFile
            | None -> ()

            let deps =
                let sources = [ PackageSources.DefaultNuGetV3Source ]
                let additionalLines = [ "storage: none"; $"framework: {tfm}"; "" ]
                Dependencies.Init(fschPaketDir, sources, additionalLines, (fun () -> ()))
                log "init OK"
                Dependencies.Locate(fschPaketDir)

            log $"DEPS: {deps.DependenciesFile}"

            let preProcess (line: string) =
                let parsed = DependenciesFileParser.parseDependencyLine line |> Seq.toList

                let processed =
                    match parsed with
                    | "github" :: path :: tail when not <| path.Contains(":") -> "github" :: $"{path}:main" :: tail
                    | s -> s

                processed |> String.concat " "

            try
                let df = deps.GetDependenciesFile()

                let newLines =
                    packageManagerTextLines
                    |> Seq.map (fun (_, s) -> s.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                    |> Seq.collect id
                    |> Seq.map _.Trim()
                    |> Seq.map preProcess
                    |> Seq.distinct
                    |> Seq.filter (fun s -> df.Lines |> Seq.contains s |> not)
                    |> Seq.toArray

                DependenciesFileParser.parseDependenciesFile "tmp" true newLines |> ignore
                File.AppendAllLines(deps.DependenciesFile, newLines)
                log (File.ReadAllText(deps.DependenciesFile))
            with _ ->
                File.Delete deps.DependenciesFile
                log "Deleted invalid deps file"
                reraise ()

            deps.Install(false)
            let expectedPartialPath = PaketPaths.mainGroupFile tfm scriptExt

            let data =
                deps.GenerateLoadScriptData deps.DependenciesFile [ Domain.MainGroup ] [ tfm ] [ scriptExt ]
                |> Seq.filter (fun d -> d.PartialPath = expectedPartialPath)
                |> Seq.head

            data.Save(DirectoryInfo(fschPaketDir))

            let loadingScriptsFilePath = PaketPaths.loadingScriptsDir (Directory.GetCurrentDirectory()) tfm scriptExt

            log $"LOADING SCRIPTS: {loadingScriptsFilePath}"
            log (File.ReadAllText(loadingScriptsFilePath))

            let roots = [ Path.Combine(fschPaketDir, Constants.PaketFilesFolderName) ]

            ResolveDependenciesResult(true, [||], [||], [], [ loadingScriptsFilePath ], roots)
        with e ->
            log $"{e.ToString()}"
            ResolveDependenciesResult(false, [||], [| "Paket: " + e.Message |], [], [], [])
