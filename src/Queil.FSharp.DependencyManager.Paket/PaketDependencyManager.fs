namespace Queil.FSharp.DependencyManager.Paket

open Queil.FSharp.Hashing
open System
open System.IO
open Paket
open System.Collections.Concurrent
open System.Text.Json
open Paket.Domain

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

type Configuration =
    { Verbose: bool
      Logger: (string -> unit) option
      ResultCache: ConcurrentDictionary<string, ResolveDependenciesResult>
      OutputRootDir: string
      ScriptOutputRootDir: string option
      ScriptOutputVersionDir: string option }

    member x.PaketTmpDir() =
        Path.Combine(x.ScriptOutputRootDir |> Option.defaultValue x.OutputRootDir, "paket")

    member x.ResultCacheDir() =
        Path.Combine(x.PaketTmpDir(), "results-cache")

module Configure =
    let mutable private data =

        let outputRootDir = Path.Combine(Path.GetTempPath(), ".fsch")

        { Verbose = false
          Logger = None
          ResultCache = ConcurrentDictionary<string, ResolveDependenciesResult>()
          OutputRootDir = outputRootDir
          ScriptOutputRootDir = None
          ScriptOutputVersionDir = None }


    let private lockObj = obj ()

    let update f = lock lockObj (fun () -> data <- f data)

    let render =
        lazy
            (Directory.CreateDirectory(data.ResultCacheDir()) |> ignore

             Directory.EnumerateFiles(data.ResultCacheDir())
             |> Seq.map (fun f -> Path.GetFileNameWithoutExtension f, File.ReadAllText f)
             |> Seq.iter (fun (key, content) ->

                 let entry = JsonSerializer.Deserialize<ResolveDependenciesResult> content

                 match entry with
                 | null -> ()
                 | validEntry -> data.ResultCache.TryAdd(key, validEntry) |> ignore)

             data)


[<RequireQualifiedAccess>]
module PaketPaths =

    let internal mainGroupFile (tfm: string) (ext: string) = $"%s{tfm}/main.group.%s{ext}"

    let internal loadingScriptsDir (dir: string) (tfm: string) (ext: string) =
        Path.Combine(dir, Constants.PaketFolderName, "load", mainGroupFile tfm ext)

// outputDirectory not really useful as it comes empty on GetProjectOptionsFromScript
[<DependencyManager>]
type PaketDependencyManager(outputDirectory: string option, useResultsCache: bool) =

    let config = Configure.render.Value
    let log = config.Logger |> Option.defaultValue ignore

    let mutable logObservable: IDisposable =
        { new IDisposable with
            member _.Dispose() = () }

    do
        Logging.verbose <- config.Verbose
        Logging.verboseWarnings <- config.Verbose

        logObservable <-
            Paket.Logging.event.Publish
            |> Observable.subscribe (fun (e: Logging.Trace) -> log e.Text)

    interface IDisposable with
        member _.Dispose() : unit = logObservable.Dispose()

    member _.Name = "paket"
    member _.Key = "paket"

    member _.HelpMessages: string list = []

    member _.ClearResultsCache = fun () -> config.ResultCache.Clear()

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

        let getCacheKey (packageManagerTextLines: (string * string) seq) (tfm: string) (rid: string) =
            let content =
                String.concat
                    "|"
                    [| yield! packageManagerTextLines |> Seq.map (fun (a, b) -> $"{a.Trim()}{b.Trim()}")
                       tfm
                       rid |]

            Hash.sha256 content

        let workDir =
            config.ScriptOutputRootDir
            |> Option.defaultWith (fun () -> Path.Combine(config.PaketTmpDir(), Hash.sha256 scriptDir |> Hash.short))

        let mutable isCached = true
        let cacheKey = getCacheKey packageManagerTextLines tfm runtimeIdentifier

        let resolve () =
            isCached <- false
            log $"Resolving dependencies (cache key: {cacheKey})"
            let scriptExt = scriptExt[1..]

            Directory.CreateDirectory workDir |> ignore

            log $"SCRIPT NAME: {scriptName}"
            log $"SCRIPT DIR: {scriptDir}"
            log $"WORK DIR: {workDir}"

            match Dependencies.TryLocate workDir with
            | Some df -> File.Delete df.DependenciesFile
            | None -> ()

            let deps =
                let sources = [ PackageSources.DefaultNuGetV3Source ]
                let additionalLines = [ "storage: none"; $"framework: {tfm}"; "" ]
                Dependencies.Init(workDir, sources, additionalLines, (fun () -> ()))
                Dependencies.Locate workDir

            let preProcessGithub (line: string) =
                let parsed = DependenciesFileParser.parseDependencyLine line |> Seq.toList

                let processed =
                    match parsed with
                    | "github" :: path :: tail when not <| path.Contains ":" -> "github" :: $"{path}:main" :: tail
                    | s -> s

                let isolatedWithGroups =
                    match processed with
                    | [ "github"; path ] ->
                        let repo, ref = path.Split ":" |> fun x -> x[0].Replace("/", "__"), x[1]
                        $"group gh_{repo}_{ref}\n  " :: processed @ [ "\n\ngroup Main" ]
                    | s -> s

                isolatedWithGroups |> String.concat " "

            try
                let df = deps.GetDependenciesFile()

                let newLines =
                    packageManagerTextLines
                    |> Seq.map (fun (_, s) -> s.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                    |> Seq.collect id
                    |> Seq.map _.Trim()
                    |> Seq.map preProcessGithub
                    |> Seq.distinct
                    |> Seq.filter (fun s -> df.Lines |> Seq.contains s |> not)
                    |> Seq.toArray

                DependenciesFileParser.parseDependenciesFile "tmp" true newLines |> ignore
                File.AppendAllLines(deps.DependenciesFile, newLines)
            with _ ->
                File.Delete deps.DependenciesFile
                log "Deleted invalid deps file"
                reraise ()

            deps.Install false

            let expectedPartialPath = PaketPaths.mainGroupFile tfm scriptExt

            let data =
                deps.GenerateLoadScriptData deps.DependenciesFile [] [ tfm ] [ scriptExt ]
                |> Seq.filter (fun d -> d.PartialPath = expectedPartialPath)
                |> Seq.head

            data.Save(DirectoryInfo workDir)

            let loadingScriptsFilePath = PaketPaths.loadingScriptsDir workDir tfm scriptExt

            let paketFilesDir = Path.Combine(workDir, Constants.PaketFilesFolderName)

            let roots =
                [ paketFilesDir
                  yield!
                      deps.GetDependenciesFile().Groups.Keys
                      |> Seq.filter ((<>) (GroupName "Main"))
                      |> Seq.map (fun g -> Path.Combine(paketFilesDir, g.Name)) ]

            ResolveDependenciesResult(true, [||], [||], [], [ loadingScriptsFilePath ], roots)

        try

            let resolveResult =
                if not useResultsCache then
                    resolve ()
                else
                    config.ResultCache.GetOrAdd(
                        cacheKey,
                        fun _ ->
                            let result = resolve ()

                            if result.Success then
                                let serialized = System.Text.Json.JsonSerializer.Serialize result
                                let resultCachePath = Path.Combine(config.ResultCacheDir(), $"{cacheKey}.json")
                                File.WriteAllText(resultCachePath, serialized)
                                log $"Saving resolve result to: {resultCachePath}"

                            result
                    )

            if isCached then
                log $"Resolve results cache hit: {cacheKey}"

            resolveResult
        with e ->
            log $"{e.ToString()}"
            ResolveDependenciesResult(false, [||], [| "Paket: " + e.Message |], [], [], [])
