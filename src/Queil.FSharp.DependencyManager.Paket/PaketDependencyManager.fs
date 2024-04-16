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
  let internal scriptRootPaketDir (scriptFilePath:string) = Path.Combine(Path.GetTempPath(), scriptFilePath |> Path.GetDirectoryName , ".fsch")
  let internal mainGroupFile (tfm:string) (ext:string) = $"%s{tfm}/main.group.%s{ext}"
  let internal loadingScriptsDir scriptFilePath (tfm:string) (ext:string)  = 
    Path.Combine(scriptFilePath |> scriptRootPaketDir, Constants.PaketFolderName, "load", mainGroupFile tfm ext)
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
            // DO NOT USE. It is only correct for GetProjectOptionsFromScript. Incorrect for Compile (points to the current working dir)
            // Use the directory from scriptName
            scriptDir: string,
            scriptName: string,
            scriptExt: string,
            packageManagerTextLines: (string * string) seq,
            tfm: string,
            runtimeIdentifier: string,
            timeout: int
        ) : obj =

        try
            let scriptExt = scriptExt[1..]
            let fschPaketDir = scriptName |> PaketPaths.scriptRootPaketDir
            Directory.CreateDirectory fschPaketDir |> ignore
            Dependencies.Init(fschPaketDir)

            let depsFile = Dependencies.Locate(fschPaketDir)
            let existingLines = depsFile.GetDependenciesFile().Lines

            let preProcess (line: string) = 
                let parsed = 
                  DependenciesFileParser.parseDependencyLine line
                  |> Seq.toList
                let processed =
                    match parsed with
                    | "github"::path::tail when not <| path.Contains(":") ->
                    "github"::$"{path}:main"::tail
                    | s -> s
                processed |> String.concat " "

            let newLines =
                packageManagerTextLines
                |> Seq.map (fun (_,s) -> s.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
                |> Seq.collect (id)
                |> Seq.map (fun s -> s.Trim())
                |> Seq.map (preProcess)
                |> Seq.filter (fun line ->  existingLines |> Seq.contains(line) |> not)
                |> Seq.toList

            File.AppendAllLines(depsFile.DependenciesFile, newLines)
            depsFile.Install(false)

            let expectedPartialPath = PaketPaths.mainGroupFile tfm scriptExt

            let data =
                depsFile.GenerateLoadScriptData
                    depsFile.DependenciesFile
                    [ Domain.MainGroup ]
                    [ tfm ]
                    [ scriptExt ]
                |> Seq.filter (fun d -> d.PartialPath = expectedPartialPath)
                |> Seq.head

            data.Save(DirectoryInfo(fschPaketDir))

            ResolveDependenciesResult(
                true,
                [||],
                [||],
                [],
                [ PaketPaths.loadingScriptsDir scriptName tfm scriptExt ],
                []
            )
        with e ->
            failwithf "Paket: %s" (string e)
