namespace Queil.FSharp.FscHost

open System.Runtime.Loader
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text
open System
open System.IO
open System.Reflection
open System.Text
open System.Security.Cryptography

[<RequireQualifiedAccess>]
module private Const =
    [<Literal>]
    let FschDir = ".fsch"

    [<Literal>]
    let FschDeps = "fsch.deps"

    [<Literal>]
    let InlineFsx = "inline.fsx"

type Script =
    | File of path: string
    | Inline of body: string

type Member<'a> = Path of string

type CompilerOptions =
    { Args: string -> string list -> CompilerOptions -> string list
      IncludeHostEntryAssembly: bool
      LangVersion: string option
      Target: string
      TargetProfile: string
      WarningLevel: int
      Symbols: string list
      Standalone: bool }

    static member Default =
        { Args =
            fun scriptPath refs opts ->
                [ "-a"
                  scriptPath
                  $"--targetprofile:%s{opts.TargetProfile}"
                  $"--target:%s{opts.Target}"
                  $"--warn:%i{opts.WarningLevel}"
                  yield! refs
                  match opts.IncludeHostEntryAssembly with
                  | true -> $"-r:%s{Assembly.GetEntryAssembly().GetName().Name}"
                  | _ -> ()
                  match opts.LangVersion with
                  | Some ver -> $"--langversion:%s{ver}"
                  | _ -> ()
                  for s in opts.Symbols do
                      $"--define:%s{s}"
                  match opts.Standalone with
                  | true -> "--standalone"
                  | _ -> () ]
          IncludeHostEntryAssembly = true
          LangVersion = None
          Target = "library"
          TargetProfile = "netcore"
          WarningLevel = 3
          Symbols = []
          Standalone = false }

type CompileOutput =
    { AssemblyFilePath: string
      Assembly: Lazy<Assembly> }

type ScriptCache =
    { NuGets: string list
      SourceFiles: string list
      FilePath: string }

    static member Default =
        { NuGets = []
          SourceFiles = []
          FilePath = "" }

    static member Load(path: string) =
        path
        |> File.ReadAllLines
        |> Seq.fold
            (fun x s ->
                match s.Split("#") |> Seq.toList with
                | [ "n"; v ] -> { x with NuGets = v :: x.NuGets }
                | [ "s"; v ] ->
                    { x with
                        SourceFiles = v :: x.SourceFiles }
                | _ -> failwith $"Could not parse line: %s{s} in file: %s{path}")
            ScriptCache.Default
        |> fun x -> { x with FilePath = path }

    member cache.Save() =
        [ yield! cache.SourceFiles |> Seq.map (fun v -> $"s#{v}")
          yield! cache.NuGets |> Seq.map (fun v -> $"n#{v}") ]
        |> fun lines -> File.WriteAllLines(cache.FilePath, lines)

type Options =
    { Compiler: CompilerOptions
      UseCache: bool
      OutputDir: string
      Logger: string -> unit
      LogListTypes: bool
      AutoLoadNugetReferences: bool }

    static member Default =
        { Compiler = CompilerOptions.Default
          UseCache = false
          OutputDir = Path.Combine(Path.GetTempPath(), Const.FschDir)
          Logger = ignore
          LogListTypes = false
          AutoLoadNugetReferences = true }

[<RequireQualifiedAccess>]
module CompilerHost =
    open Errors

    [<RequireQualifiedAccess>]
    module private Hash =
        let sha256 (s: string) =
            use sha256 = SHA256.Create()

            s
            |> Encoding.UTF8.GetBytes
            |> sha256.ComputeHash
            |> BitConverter.ToString
            |> _.Replace("-", "")

        let short (s: string) = s[0..10].ToLowerInvariant()

        let deepSourceHash sourceFiles =

            let fileHash filePath = File.ReadAllText filePath |> sha256

            let combinedHash =
                sourceFiles
                |> Seq.map fileHash
                |> Seq.sort
                |> Seq.reduce (fun a b -> a + b)
                |> sha256

            short combinedHash

    module private Internals =
        let checker = FSharpChecker.Create()

        let ensureScriptFile (outputRootDir: string) (script: Script) =
            let getScriptFilePath =
                function
                | File path ->
                    let shallowHash = path |> File.ReadAllText |> Hash.sha256 |> Hash.short
                    let scriptDir = Path.GetDirectoryName path
                    (path, scriptDir, Path.Combine(outputRootDir, shallowHash))
                | Inline body ->
                    let shallowHash = body |> Hash.sha256 |> Hash.short
                    let scriptDir = Path.Combine(Path.GetTempPath(), Const.FschDir, shallowHash)
                    let filePath = Path.Combine(scriptDir, Const.InlineFsx)
                    (filePath, scriptDir, Path.Combine(outputRootDir, shallowHash))

            let createInlineScriptFile (filePath: string) =
                function
                | Inline body ->
                    filePath |> Path.GetDirectoryName |> Directory.CreateDirectory |> ignore
                    File.WriteAllText(filePath, body)
                | _ -> ()

            let scriptFilePath, scriptDir, cacheDir = script |> getScriptFilePath

            script |> createInlineScriptFile scriptFilePath

            (scriptFilePath, scriptDir, cacheDir)

        let compileScript (rootFilePath: string) (metadata: ScriptCache) (options: Options) : Async<CompileOutput> =

            let log = options.Logger
            let asmLoadContext = AssemblyLoadContext("script", true)

            let getCompileOutput dllPath =
                { AssemblyFilePath = dllPath
                  Assembly =
                    Lazy<Assembly>(fun () ->
                        if options.AutoLoadNugetReferences then
                            metadata.NuGets
                            |> Seq.iter (fun path ->
                                log $"Loading assembly: %s{path}"
                                path |> asmLoadContext.LoadFromAssemblyPath |> ignore)

                        dllPath |> Path.GetFullPath |> asmLoadContext.LoadFromAssemblyPath) }

            async {

                let outputDllName =
                    if options.UseCache then
                        let hash = Hash.deepSourceHash metadata.SourceFiles
                        Path.Combine(options.OutputDir.TrimEnd('\\', '/'), $"{hash}.dll")
                    else
                        $"{Path.GetTempFileName()}.dll"

                match outputDllName with
                | path when File.Exists path ->
                    log $"Found cached assembly: %s{path}"
                    log $"Cached deps file: %s{metadata.FilePath}"
                    return getCompileOutput path

                | path ->

                    let refs = metadata.NuGets |> Seq.map (sprintf "-r:%s") |> Seq.toList

                    let absoluteRootFilePath =
                        if Path.IsPathRooted rootFilePath then
                            rootFilePath
                        else
                            Path.GetFullPath rootFilePath

                    let compilerArgs =
                        [ yield! options.Compiler.Args absoluteRootFilePath refs options.Compiler
                          $"--out:{path}" ]

                    log (sprintf "Compiling with args: %s" (compilerArgs |> String.concat " "))
                    let! errors, _ = checker.Compile(compilerArgs |> List.toArray, "fsch-getAssembly")

                    match errors with
                    | xs when xs |> Array.exists (fun x -> x.Severity = FSharpDiagnosticSeverity.Error) ->
                        raise (ScriptCompileError(errors |> Seq.map string))
                    | xs -> xs |> Seq.iter (string >> log)

                    let compileOutput = getCompileOutput outputDllName

                    if options.LogListTypes then
                        compileOutput.Assembly.Value.GetTypes() |> Seq.iter (fun t -> log t.FullName)

                    return compileOutput
            }

    open Internals

    let getAssembly (options: Options) (script: Script) : Async<CompileOutput> =

        let log = options.Logger
        async {
            let rootFilePath, scriptDir, outputDir =
                script |> ensureScriptFile options.OutputDir
            
            log $"Root file path: %s{rootFilePath}"
            log $"Script dir: %s{scriptDir}"
            log $"Output dir: %s{outputDir}"
                
            match script with
            | Inline _ -> Directory.CreateDirectory scriptDir |> ignore
            | _ -> ()

            Directory.CreateDirectory outputDir |> ignore

            let cacheDepsFilePath = Path.Combine(outputDir, Const.FschDeps)

            let! metadataResult =
                async {

                    let buildMetadata () =
                        async {
                            let source = File.ReadAllText rootFilePath |> SourceText.ofString

                            let! projOptions, errors =
                                checker.GetProjectOptionsFromScript(rootFilePath, source, previewEnabled = true)

                            match errors with
                            | [] ->
                                let metadata =
                                    { ScriptCache.Default with
                                        FilePath = cacheDepsFilePath
                                        SourceFiles = projOptions.SourceFiles |> Seq.toList }
                                log "Source files:"
                                for sf in metadata.SourceFiles do
                                    log $"  %s{sf}"

                                if options.Compiler.Standalone then
                                    return Ok metadata
                                else
                                    return
                                        Ok(
                                            { metadata with
                                                NuGets =
                                                    metadata.SourceFiles
                                                    |> Seq.filter (fun p ->
                                                        p.Contains("/.packagemanagement/nuget/")
                                                        || p.Contains("/.paket/load/"))
                                                    |> Seq.collect File.ReadAllLines
                                                    |> Seq.choose (function
                                                        | Utils.ParseRegex """^#r @?"(.*\.dll)"\s?$""" [ dllPath ] ->
                                                            Some(dllPath)
                                                        | _ -> None)
                                                    |> Seq.toList }
                                        )
                            | errors -> return Error(errors)
                        }

                    if File.Exists cacheDepsFilePath then
                        return Ok (ScriptCache.Load cacheDepsFilePath)
                    else
                        return! buildMetadata ()
                }

            let originalDir = Directory.GetCurrentDirectory()

            try
                match metadataResult with
                | Ok metadata ->
                    Directory.SetCurrentDirectory scriptDir
                    metadata.Save()

                    return! compileScript rootFilePath metadata { options with OutputDir = outputDir }
                | Error errors -> return raise (ScriptParseError(errors |> Seq.map string))
            finally
                Directory.SetCurrentDirectory originalDir
        }

    let getMember<'a> (options: Options) (Path pathA: Member<'a>) (script: Script) : Async<'a> =
        async {
            let! output = script |> getAssembly options
            return output.Assembly.Value |> Member.get pathA
        }

    let getMember2<'a, 'b>
        (options: Options)
        (Path pathA: Member<'a>)
        (Path pathB: Member<'b>)
        (script: Script)
        : Async<'a * 'b> =
        async {
            let! output = script |> getAssembly options
            return output.Assembly.Value |> Member.get<'a> pathA, output.Assembly.Value |> Member.get<'b> pathB
        }

    let getMember3<'a, 'b, 'c>
        (options: Options)
        (Path pathA: Member<'a>)
        (Path pathB: Member<'b>)
        (Path pathC: Member<'c>)
        (script: Script)
        : Async<'a * 'b * 'c> =
        async {
            let! output = script |> getAssembly options

            return
                output.Assembly.Value |> Member.get<'a> pathA,
                output.Assembly.Value |> Member.get<'b> pathB,
                output.Assembly.Value |> Member.get<'c> pathC
        }

    let getMember4<'a, 'b, 'c, 'd>
        (options: Options)
        (Path pathA: Member<'a>)
        (Path pathB: Member<'b>)
        (Path pathC: Member<'c>)
        (Path pathD: Member<'d>)
        (script: Script)
        : Async<'a * 'b * 'c * 'd> =
        async {
            let! output = script |> getAssembly options

            return
                output.Assembly.Value |> Member.get<'a> pathA,
                output.Assembly.Value |> Member.get<'b> pathB,
                output.Assembly.Value |> Member.get<'c> pathC,
                output.Assembly.Value |> Member.get<'d> pathD
        }
