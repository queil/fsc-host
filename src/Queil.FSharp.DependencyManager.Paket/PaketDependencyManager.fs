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
      RootScriptFilePath: string option
      OutputRootDir: string
      ScriptOutputRootDir: string option
      ScriptOutputVersionDir: string option }

    static member Default =
        { IsDefault = true
          RootScriptFilePath = None
          Verbose = false
          OutputRootDir = Path.Combine(Path.GetTempPath(), ".fsch")
          ScriptOutputRootDir = None
          ScriptOutputVersionDir = None }

module Configure =

    let internal render key =
        if File.Exists key then

            use fs = new FileStream(key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            use sr = new StreamReader(fs)

            sr.ReadToEnd()
            |> JsonSerializer.Deserialize<Configuration>
            |> Option.ofObj
            |> _.Value
        else
            Configuration.Default

[<RequireQualifiedAccess>]
module PaketPaths =

    let internal mainGroupFile (tfm: string) (ext: string) =
        $"%s{tfm}%c{Path.DirectorySeparatorChar}main.group.%s{ext}"

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

    /// This method gets called by fsch multiple times. First for GetProjectOptionsFromScript (for each referenced script), then for the actual compile
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

        try
            let dirHash = Hash.shortHash scriptDir

            let lockFilePath =
                Path.Combine(Path.GetTempPath(), ".fsch", "lock", dirHash + ".lock")

            let config = Configure.render lockFilePath
            let log = if config.Verbose then printfn "%s" else ignore
            log $"Maybe config at {lockFilePath}"

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
                    if
                        scriptName = "stdin.fsx"
                        && not (File.Exists(Path.Combine(scriptDir, scriptName)))
                    then
                        // Fallback for stdin/interactive mode. FSI passes stdin.fsx as script name (and the file obviously won't exist)
                        let hash =
                            Hash.shortHash (
                                scriptDir + "|" + String.concat "|" (packageManagerTextLines |> Seq.map snd)
                            )

                        Path.Combine(config.OutputRootDir, hash)
                    else
                        let hashes = Hash.fileHash scriptName None
                        hashes.HashedScriptDir config.OutputRootDir)

            let resultCacheDir = Path.Combine(workDir, "resolve-cache")

            if Directory.Exists resultCacheDir then
                Directory.EnumerateFiles resultCacheDir
                |> Seq.map (fun f -> Path.GetFileNameWithoutExtension f |> Option.ofObj |> _.Value, File.ReadAllText f)
                |> Seq.iter (fun (key: string, content) ->
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

                            $"group gh_{repo}_{ref}{Environment.NewLine}  " :: processed
                            @ [ $"{Environment.NewLine}{Environment.NewLine}group Main" ]
                        | s -> s

                    isolatedWithGroups |> String.concat " "

                let df =
                    try
                        deps.GetDependenciesFile()
                    with _ ->
                        File.Delete deps.DependenciesFile
                        log $"Deleted invalid file: %s{deps.DependenciesFile}"
                        reraise ()

                let mutable newLines = [||]

                try
                    newLines <-
                        packageManagerTextLines
                        |> Seq.map (fun (_, s) -> s.Split([| "\r\n"; "\n" |], StringSplitOptions.RemoveEmptyEntries))
                        |> Seq.collect id
                        |> Seq.map _.Trim()
                        |> Seq.map preProcessGithub
                        |> Seq.distinct
                        |> Seq.filter (fun s -> df.Lines |> Seq.contains s |> not)
                        |> Seq.toArray

                    DependenciesFileParser.parseDependenciesFile "tmp" true newLines |> ignore
                    File.AppendAllLines(deps.DependenciesFile, newLines)
                with _ ->

                    log $"Failed to parse new lines: %A{newLines}"
                    reraise ()

                deps.Install false

                let expectedPartialPath = PaketPaths.mainGroupFile tfm scriptExt

                let data =
                    deps.GenerateLoadScriptData deps.DependenciesFile [] [ tfm ] [ scriptExt ]
                    |> Seq.filter (fun d -> d.PartialPath = expectedPartialPath)
                    |> Seq.head

                data.Save(DirectoryInfo workDir)

                let rewriteRuntimeRefs (scriptPath: string) (rid: string) (log: string -> unit) =
                    // RID fallback chain: linux-x64 -> linux -> unix; osx-arm64 -> osx -> unix; win-x64 -> win
                    let ridChain =
                        let rec expand (r: string) =
                            seq {
                                yield r
                                let i = r.LastIndexOf('-')

                                if i > 0 then
                                    yield! expand (r.Substring(0, i))
                            }

                        seq {
                            yield! expand rid

                            if rid.StartsWith("linux") || rid.StartsWith("osx") || rid.StartsWith("freebsd") then
                                yield "unix"
                        }
                        |> Seq.distinct
                        |> Seq.toList

                    // matches:  #r @"<path>/lib/<tfm>/<dll>"
                    // also handles plain (non-verbatim) #r "..."  and Windows backslashes
                    let sep = @"[/\\]"

                    let pattern =
                        $@"^(\s*#r\s+@?"")(?<root>.+?){sep}lib{sep}(?<tfm>[^/\\]+){sep}(?<dll>[^/\\""]+\.dll)(""\s*)$"

                    let rx =
                        System.Text.RegularExpressions.Regex(
                            pattern,
                            System.Text.RegularExpressions.RegexOptions.Compiled
                        )

                    let tryPickTfm (ridDir: string) (preferredTfm: string) =
                        if not (Directory.Exists ridDir) then
                            None
                        else
                            // prefer exact TFM match, then highest net*, then netstandard2.1, then netstandard2.0
                            let tfms =
                                Directory.GetDirectories ridDir
                                |> Array.map (Path.GetFileName >> Option.ofObj)
                                |> Array.choose id

                            let rank (t: string) =
                                if t = preferredTfm then
                                    1000
                                elif
                                    t.StartsWith "net"
                                    && not (t.StartsWith "netstandard")
                                    && not (t.StartsWith "netcoreapp")
                                then
                                    // net9.0 -> 900, net10.0 -> 1000... good enough
                                    match Double.TryParse(t.Substring 3) with
                                    | true, v -> int (v * 100.0)
                                    | _ -> 0
                                elif t = "netstandard2.1" then
                                    10
                                elif t = "netstandard2.0" then
                                    5
                                else
                                    0

                            tfms |> Array.sortByDescending rank |> Array.tryHead

                    let rewriteLine (line: string) =
                        let m = rx.Match line

                        if not m.Success then
                            line
                        else
                            let root = m.Groups["root"].Value
                            let tfm = m.Groups["tfm"].Value
                            let dll = m.Groups["dll"].Value
                            
                            let candidate =
                                ridChain
                                |> List.tryPick (fun r ->
                                    let ridLibRoot = Path.Combine(root, "runtimes", r, "lib")

                                    tryPickTfm ridLibRoot tfm
                                    |> Option.map (fun chosenTfm -> Path.Combine(ridLibRoot, chosenTfm, dll))
                                    |> Option.filter File.Exists)

                            match candidate with
                            | Some p ->
                                log $"rewrite: {dll} -> runtimes/.../{Path.GetFileName(Path.GetDirectoryName p)}"
                                $"#r \"{p}\""
                            | None -> line

                    let lines = File.ReadAllLines scriptPath
                    let rewritten = lines |> Array.map rewriteLine

                    if rewritten <> lines then
                        File.WriteAllLines(scriptPath, rewritten)


                let loadingScriptsFilePath = PaketPaths.loadingScriptsDir workDir tfm scriptExt

                rewriteRuntimeRefs loadingScriptsFilePath runtimeIdentifier log

                let paketFilesDir = Path.Combine(workDir, Constants.PaketFilesFolderName)

                let roots =
                    [ paketFilesDir
                      yield!
                          deps.GetDependenciesFile().Groups.Keys
                          |> Seq.filter ((<>) (GroupName "Main"))
                          |> Seq.map (fun g -> Path.Combine(paketFilesDir, g.Name)) ]

                ResolveDependenciesResult(true, [||], [||], [], [ loadingScriptsFilePath ], roots)

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
                                Directory.CreateDirectory resultCacheDir |> ignore
                                let resultCachePath = Path.Combine(resultCacheDir, $"{cacheKey}.json")
                                File.WriteAllText(resultCachePath, serialized)
                                log $"Saving resolve result to: {resultCachePath}"

                            result
                    )

            if isCached then
                log $"Resolve results cache hit: {cacheKey}"

            resolveResult
        with e ->
            eprintfn $"{e.ToString()}"
            ResolveDependenciesResult(false, [||], [| "Paket: " + e.Message |], [], [], [])
