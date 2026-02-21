namespace Queil.FSharp.DependencyManager.FsxLib

open System.Text.Json
open Queil.FSharp.FscHost
open Queil.FSharp.FscHost.Configuration
open Queil.FSharp.Hashing
open System.IO
open System.Collections.Concurrent

module Attributes =
    [<assembly: DependencyManager>]
    do ()

[<DependencyManager>]
type PaketDependencyManager(outputDirectory: string option, useResultsCache: bool) =

    let resultCache = ConcurrentDictionary<string, ResolveDependenciesResult>()

    member _.Name = "fsxlib"
    member _.Key = "fsxlib"

    member _.HelpMessages: string list = []

    member _.ClearResultsCache = fun () -> resultCache.Clear()

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
            //let log = if config.Verbose then printfn "%s" else ignore
            printfn $"Maybe config at {lockFilePath}"

            if config.IsDefault then
                printfn "Using default config"
            else
                printfn "Using config override"

            let getCacheKey (packageManagerTextLines: (string * string) seq) (tfm: string) (rid: string) =
                let content =
                    String.concat
                        "|"
                        [| yield! packageManagerTextLines |> Seq.map (fun (a, b) -> $"{a.Trim()}{b.Trim()}")
                           tfm
                           rid |]

                Hash.sha256 content |> Hash.short
            let mutable isCached = true
            let cacheKey = getCacheKey packageManagerTextLines tfm runtimeIdentifier
            let resolve () =

                printfn $"FSXLIB: script name - %s{scriptName}"
                printfn $"FSXLIB: script dir - %s{scriptDir}"
                printfn $"FSXLIB: %A{packageManagerTextLines}"

                let resolutions =
                    packageManagerTextLines
                    |> Seq.map (fun (_, v) ->
                        let scriptPath =
                            if Path.IsPathRooted v then
                                v
                            else
                                Path.Combine(scriptDir, v)

                        let asm =
                            CompilerHost.getAssembly
                                { Options.Default with
                                    UseCache = true
                                    Verbose = config.Verbose }
                                (Queil.FSharp.FscHost.File(scriptPath))
                            |> Async.RunSynchronously

                        asm.AssemblyFilePath)
                    |> Seq.toList


                printfn $"FSXLIB: %A{resolutions}"

                ResolveDependenciesResult(true, [||], [||], resolutions, [], [])

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
                                printfn $"Saving resolve result to: {resultCachePath}"

                            result
                    )

            if isCached then
                printfn $"Resolve results cache hit: {cacheKey}"

            resolveResult
        with e ->
            eprintfn $"{e.ToString()}"
            ResolveDependenciesResult(false, [||], [| "FSXLIB: " + e.Message |], [], [], [])
