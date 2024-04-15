namespace Queil.FSharp.DependencyManager.Paket

open System

[<AttributeUsage(AttributeTargets.Assembly ||| AttributeTargets.Class, AllowMultiple = false)>]
type DependencyManagerAttribute() =
    inherit Attribute()


module Attributes =
    [<assembly: DependencyManager()>]
    do ()

type ResolveDependenciesResult
    (success: bool, stdOut: string array, stdError: string array, resolutions: string seq, sourceFiles: string seq, roots: string seq) =
    member _.Success = success
    member _.StdOut = stdOut
    member _.StdError = stdError
    member _.Resolutions = resolutions
    member _.SourceFiles = sourceFiles
    member _.Roots = roots


[<DependencyManager()>]
type PaketDependencyManager(outputDirectory: string option, useResultsCache: bool) =
    do 
        printfn "INSTANCE CREATED"
    
    member _.Name = "paget"
    member _.Key = "paket"

    member _.HelpMessages : string list = []

    member _.ClearResultsCache = fun () -> ()

    member x.ResolveDependencies(scriptDir: string, mainScriptName: string, scriptName: string, packageManagerTextLines: string seq, targetFramework: string) : ResolveDependenciesResult =

        printfn "been here"
        let scriptDir =
            if scriptDir = System.String.Empty then
                System.Environment.CurrentDirectory
            else
                scriptDir

        try
            let loadScript, additionalIncludeDirs =
                ReferenceLoading.PaketHandler.ResolveDependencies(
                    targetFramework,
                    scriptDir,
                    scriptName,
                    packageManagerTextLines
                )

            let resolutions =
                // https://github.com/dotnet/fsharp/pull/10224#issue-498147879
                // if load script causes problem
                // consider changing this to be the list of all assemblies to load rather than passing through a load script
                []

            ResolveDependenciesResult(true, [||], [||], resolutions, [ loadScript ], additionalIncludeDirs)
        with e ->
            printfn "exception while resolving dependencies: %s" (string e)
            ResolveDependenciesResult(false, [||], [||], [||], [||], [||])
