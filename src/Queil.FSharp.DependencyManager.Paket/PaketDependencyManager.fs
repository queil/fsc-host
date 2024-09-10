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

    let internal mainGroupFile (tfm: string) (ext: string) = $"%s{tfm}/main.group.%s{ext}"

    let internal loadingScriptsDir (dir: string) (tfm: string) (ext: string) =
        Path.Combine(dir, Constants.PaketFolderName, "load", mainGroupFile tfm ext)

[<RequireQualifiedAccess>]
module private Hash =
    open System.Security.Cryptography
    open System.Text

    let sha256 (s: string) =
        use sha256 = SHA256.Create()

        s
        |> Encoding.UTF8.GetBytes
        |> sha256.ComputeHash
        |> BitConverter.ToString
        |> _.Replace("-", "")

    let short (s: string) = s[0..10].ToLowerInvariant()

// outputDirectory not really useful as it comes empty on GetProjectOptionsFromScript
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

        let workDirRoot = "/tmp/.fsch"

        let workDir =
            Path.Combine(workDirRoot, "paket", Hash.sha256 (scriptName) |> Hash.short)

        let logPath = $"{workDir}/paket.log"
        let log (line) = File.AppendAllLines(logPath, [ line ])

        try
            let scriptExt = scriptExt[1..]

            Directory.CreateDirectory workDir |> ignore

            log $"------- NEW SCRIPT ----------"
            log $"SCRIPT NAME: {scriptName}"
            log $"SCRIPT DIR: {scriptDir}"
            log $"WORK DIR: {workDir}"

            match Dependencies.TryLocate(workDir) with
            | Some df -> File.Delete df.DependenciesFile
            | None -> ()

            let deps =
                let sources = [ PackageSources.DefaultNuGetV3Source ]
                let additionalLines = [ "storage: none"; $"framework: {tfm}"; "" ]
                Dependencies.Init(workDir, sources, additionalLines, (fun () -> ()))
                log "init OK"
                Dependencies.Locate(workDir)

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

            data.Save(DirectoryInfo(workDir))

            let loadingScriptsFilePath = PaketPaths.loadingScriptsDir workDir tfm scriptExt

            log $"LOADING SCRIPTS: {loadingScriptsFilePath}"
            log (File.ReadAllText(loadingScriptsFilePath))

            let roots = [ Path.Combine(workDir, Constants.PaketFilesFolderName) ]

            ResolveDependenciesResult(true, [||], [||], [], [ loadingScriptsFilePath ], roots)
        with e ->
            log $"{e.ToString()}"
            ResolveDependenciesResult(false, [||], [| "Paket: " + e.Message |], [], [], [])
