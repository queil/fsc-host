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
    { IsDefault: bool
      Verbose: bool
      Logger: (string -> unit) option
      OutputRootDir: string
      ScriptOutputRootDir: string option
      ScriptOutputVersionDir: string option }

    static member Default =
        { IsDefault = true
          Verbose = false
          Logger = None
          OutputRootDir = Path.Combine(Path.GetTempPath(), ".fsch")
          ScriptOutputRootDir = None
          ScriptOutputVersionDir = None }

module Configure =

    let private data: ConcurrentDictionary<string, Configuration> =
        ConcurrentDictionary<string, Configuration>()

    let update (key: string) (update: Configuration -> Configuration) : unit =

        data.AddOrUpdate(
            key,
            (fun _ ->
                { update Configuration.Default with
                    IsDefault = false }),
            (fun _ old -> { update old with IsDefault = false })
        )

        |> ignore

    let internal render key =
        let exists, value = data.TryGetValue key
        if exists then value else Configuration.Default


[<RequireQualifiedAccess>]
module PaketPaths =

    let internal mainGroupFile (tfm: string) (ext: string) = $"%s{tfm}/main.group.%s{ext}"

    let internal loadingScriptsDir (dir: string) (tfm: string) (ext: string) =
        Path.Combine(dir, Constants.PaketFolderName, "load", mainGroupFile tfm ext)


// outputDirectory not really useful as it comes empty on GetProjectOptionsFromScript
[<DependencyManager>]
type PaketDependencyManager(outputDirectory: string option, useResultsCache: bool) =

    let resultCache = ConcurrentDictionary<string, ResolveDependenciesResult>()

    member _.Name = "paket"
    member _.Key = "paket"

    member _.HelpMessages: string list = []

    member _.ClearResultsCache = fun () -> resultCache.Clear()

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

        let config = Configure.render scriptName
        let log = config.Logger |> Option.defaultValue ignore

        if config.IsDefault then
            log "Using default config"
        else
            log "Using config override"

        Logging.verbose <- config.Verbose
        Logging.verboseWarnings <- config.Verbose

        use _ =
            Paket.Logging.event.Publish
            |> Observable.subscribe (fun (e: Logging.Trace) -> log e.Text)

        let getCacheKey (packageManagerTextLines: (string * string) seq) (tfm: string) (rid: string) =
            let content =
                String.concat
                    "|"
                    [| yield! packageManagerTextLines |> Seq.map (fun (a, b) -> $"{a.Trim()}{b.Trim()}")
                       tfm
                       rid |]

            Hash.sha256 content |> Hash.short

        let workDir =
            config.ScriptOutputRootDir
            |> Option.defaultWith (fun () ->
                let hashes = Hash.fileHash scriptName None
                hashes.HashedScriptDir config.OutputRootDir)

        let resultCacheDir = Path.Combine(workDir, "resolve-cache")

        Directory.CreateDirectory resultCacheDir |> ignore

        Directory.EnumerateFiles resultCacheDir
        |> Seq.map (fun f -> Path.GetFileNameWithoutExtension f, File.ReadAllText f)
        |> Seq.iter (fun (key, content) ->
            let entry = JsonSerializer.Deserialize<ResolveDependenciesResult> content

            match entry with
            | null -> ()
            | validEntry -> resultCache.TryAdd(key, validEntry) |> ignore)

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
                    | [ "git"; url ] ->
                        let server, _, project, _, _, _, _ = Git.Handling.extractUrlParts url
                        $"group git_{server}_{project}\n  " :: processed @ [ "\n\ngroup Main" ]
                    | [ "git"; url; ref ] ->
                        let server, _, project, _, _, _, _ = Git.Handling.extractUrlParts url
                        $"group git_{server}_{project}_{ref}\n  " :: processed @ [ "\n\ngroup Main" ]
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
                    resultCache.GetOrAdd(
                        cacheKey,
                        fun _ ->
                            let result = resolve ()

                            if result.Success then
                                let serialized = JsonSerializer.Serialize result
                                let resultCachePath = Path.Combine(resultCacheDir, $"{cacheKey}.json")
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
