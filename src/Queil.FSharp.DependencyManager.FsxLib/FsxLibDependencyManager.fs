namespace Queil.FSharp.DependencyManager.FsxLib

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

            printfn $"FSXLIB: %A{config}"

            printfn $"FSXLIB: script name - %s{scriptName}"
            printfn $"FSXLIB: script dir - %s{scriptDir}"
            printfn $"FSXLIB: %A{packageManagerTextLines}"

            let resolutions =
                packageManagerTextLines
                |> Seq.map (fun (_, v) ->
                    let scriptPath = if Path.IsPathRooted v then v else Path.Combine(scriptDir, v)
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


        with e ->
            eprintfn $"{e.ToString()}"
            ResolveDependenciesResult(false, [||], [| "FSXLIB: " + e.Message |], [], [], [])
