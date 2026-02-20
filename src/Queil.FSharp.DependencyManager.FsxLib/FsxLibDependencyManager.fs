namespace Queil.FSharp.DependencyManager.FsxLib

open Queil.FSharp.FscHost
open Queil.FSharp.FscHost.Configuration
open Queil.FSharp.Hashing
open System
open System.IO
open System.Collections.Concurrent

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

            printfn $"FSXLIB: %s{scriptName}"
            printfn $"FSXLIB: %A{packageManagerTextLines}"

            let resolutions =
                packageManagerTextLines
                |> Seq.map (fun (_, v) ->
                    let asm =
                        CompilerHost.getAssembly
                            { Options.Default with
                                UseCache = true
                                Verbose = config.Verbose }
                            (Queil.FSharp.FscHost.File(v))
                        |> Async.RunSynchronously

                    asm.AssemblyFilePath)
                |> Seq.toList


            printfn $"FSXLIB: %A{resolutions}"

            ResolveDependenciesResult(true, [||], [||], resolutions, [], [])


        with e ->
            eprintfn $"{e.ToString()}"
            ResolveDependenciesResult(false, [||], [| "FsxLib: " + e.Message |], [], [], [])
