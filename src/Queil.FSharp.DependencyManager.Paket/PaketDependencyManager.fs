namespace Queil.FSharp.DependencyManager.Paket

open System
open System.IO
open Paket
open System.Collections.Concurrent
open System.Text.Json

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
      PaketTmpDir: string
      ResultCacheDir: string }

module Configure =
    let mutable private data =

        let paketTmp = Path.Combine(Path.GetTempPath(), ".fsch", "paket")

        let cacheResultsDir = Path.Combine(paketTmp, "results-cache")
        Directory.CreateDirectory cacheResultsDir |> ignore
        let resultCache = ConcurrentDictionary<string, ResolveDependenciesResult>()


        Directory.EnumerateFiles cacheResultsDir
        |> Seq.map (fun f -> Path.GetFileNameWithoutExtension f, File.ReadAllText f)
        |> Seq.iter (fun (key, content) ->

            let entry = JsonSerializer.Deserialize<ResolveDependenciesResult> content 
            match entry with
            | null -> ()
            | validEntry -> resultCache.TryAdd(key, validEntry) |> ignore
           )

        { Verbose = false
          Logger = None
          PaketTmpDir = paketTmp
          ResultCacheDir = cacheResultsDir
          ResultCache = resultCache }

    let private lockObj = obj ()

    let update f = lock lockObj (fun () -> data <- f data)

    let get () = data

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

    let config = Configure.get ()
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
                    [| yield! packageManagerTextLines |> Seq.map (fun (a, b) -> $"{a}{b}")
                       tfm
                       rid |]

            use sha = System.Security.Cryptography.SHA256.Create()
            let hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content))
            Convert.ToHexString hash

        let workDir = Path.Combine(config.PaketTmpDir, Hash.sha256 scriptDir |> Hash.short)



        let resolve () =
            try
                let scriptExt = scriptExt[1..]

                Directory.CreateDirectory workDir |> ignore

                log $"Queil.FSharp.DependencyManager.Paket starting"
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

                let preProcess (line: string) =
                    let parsed = DependenciesFileParser.parseDependencyLine line |> Seq.toList

                    let processed =
                        match parsed with
                        | "github" :: path :: tail when not <| path.Contains ":" -> "github" :: $"{path}:main" :: tail
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
                with _ ->
                    File.Delete deps.DependenciesFile
                    log "Deleted invalid deps file"
                    reraise ()

                deps.Install false

                let expectedPartialPath = PaketPaths.mainGroupFile tfm scriptExt

                let data =
                    deps.GenerateLoadScriptData deps.DependenciesFile [ Domain.MainGroup ] [ tfm ] [ scriptExt ]
                    |> Seq.filter (fun d -> d.PartialPath = expectedPartialPath)
                    |> Seq.head

                data.Save(DirectoryInfo workDir)

                let loadingScriptsFilePath = PaketPaths.loadingScriptsDir workDir tfm scriptExt

                let roots = [ Path.Combine(workDir, Constants.PaketFilesFolderName) ]

                ResolveDependenciesResult(true, [||], [||], [], [ loadingScriptsFilePath ], roots)
            with e ->
                log $"{e.ToString()}"
                ResolveDependenciesResult(false, [||], [| "Paket: " + e.Message |], [], [], [])

        let cacheKey = getCacheKey packageManagerTextLines tfm runtimeIdentifier
        log cacheKey

        if not useResultsCache then
            resolve ()
        else
            config.ResultCache.GetOrAdd(
                cacheKey,
                fun _ ->
                    let result = resolve ()

                    if result.Success then
                        let serialized = System.Text.Json.JsonSerializer.Serialize result
                        File.WriteAllText(Path.Combine(config.ResultCacheDir, $"{cacheKey}.json"), serialized)

                    result
            )
