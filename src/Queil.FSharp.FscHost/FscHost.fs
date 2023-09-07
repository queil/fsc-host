namespace Queil.FSharp.FscHost

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text
open System
open System.IO
open System.Reflection
open System.Text
open System.Security.Cryptography

type Script = | File of path: string | Inline of body: string
type Member<'a> = | Path of string
type CompilerOptions = {
  Args: string -> string list -> CompilerOptions -> string list
  IncludeHostEntryAssembly: bool
  LangVersion: string option
  Target: string
  TargetProfile: string
  WarningLevel: int
  Symbols: string list
  Standalone: bool
}
with
  static member Default =
    {
      Args = fun scriptPath refs opts ->
        [
        "-a"; scriptPath
        $"--targetprofile:%s{opts.TargetProfile}"
        $"--target:%s{opts.Target}"
        $"--warn:%i{opts.WarningLevel}"
        yield! refs
        match opts.IncludeHostEntryAssembly with | true -> $"-r:%s{Assembly.GetEntryAssembly().GetName().Name}" |_ -> ()
        match opts.LangVersion with | Some ver -> $"--langversion:%s{ver}" | _ -> ()
        for s in opts.Symbols do
          $"--define:%s{s}"
        match opts.Standalone with true -> "--standalone" |_ -> ()
        ]
      IncludeHostEntryAssembly = true
      LangVersion = None
      Target = "library"
      TargetProfile = "netcore"
      WarningLevel = 3
      Symbols = []
      Standalone = false
    }

type CompileOutput = {
  AssemblyFilePath: string
  Assembly: Lazy<Assembly>
}

type ScriptCache = {
  NuGets: string list
  SourceFiles: string list
  FilePath: string
}
with 
  static member Default = { NuGets = []; SourceFiles = []; FilePath = ""}
  static member Load(path:string) =
    path
    |> File.ReadAllLines
    |> Seq.fold(
        fun x s -> 
          match s.Split("#") |> Seq.toList with
          | ["n"; v] -> { x with NuGets = v::x.NuGets }
          | ["s"; v] -> { x with SourceFiles = v::x.SourceFiles }
          | _ -> failwith $"Could not parse line: %s{s} in file: %s{path}") (ScriptCache.Default)
    |> fun x -> {x with FilePath = path}
  member cache.Save() =
    [
      yield! cache.SourceFiles |> Seq.map(fun v -> $"s#{v}")
      yield! cache.NuGets |> Seq.map(fun v -> $"n#{v}")
    ] 
    |> fun lines -> File.WriteAllLines(cache.FilePath, lines)
 
type Options =
  {
    Compiler: CompilerOptions
    UseCache: bool
    CacheShortHash: bool
    CacheDir: string
    Logger: string -> unit
    LogListTypes: bool
    AutoLoadNugetReferences: bool
  }
with 
  static member Default =
    {
      Compiler = CompilerOptions.Default
      UseCache = false
      CacheShortHash = true
      CacheDir = Path.Combine(".fsc-host", "cache")
      Logger = ignore
      LogListTypes = false
      AutoLoadNugetReferences = true
    }

[<RequireQualifiedAccess>]
module CompilerHost =
  open Errors

  module private Hash =
    let sha256 (s:string) =
      use sha256 = SHA256.Create()
      s |> Encoding.UTF8.GetBytes |> sha256.ComputeHash |> BitConverter.ToString |> fun s -> s.Replace("-", "")

    let short (s:string) = s[0..10]

  module private Internals =
    let checker = FSharpChecker.Create()

    let ensureScriptFile (script:Script) =
      let getScriptFilePath =
        function
        | File path -> path
        | Inline _ -> 
          let path = Path.GetTempFileName()
          $"%s{Path.GetTempPath()}%s{Path.GetFileNameWithoutExtension path}.fsx"

      let createInlineScriptFile (filePath:string) =
        function
        | Inline body -> File.WriteAllText(filePath, body)
        | _ -> ()

      let path = script |> getScriptFilePath
      script |> createInlineScriptFile path
      path

    let compileScript (entryFilePath:string) (metadata:ScriptCache) (options: Options) : Async<CompileOutput> =

      let log = options.Logger
      let loadNuGetAssemblies nugetPaths =
        nugetPaths |> Seq.iter (fun path -> 
          log $"Loading assembly: %s{path}"
          path |> Assembly.LoadFrom |> ignore
        )

      async {

        if options.UseCache then
          Directory.CreateDirectory options.CacheDir |> ignore

        let outputDllName =
          if options.UseCache then
            let fileHash filePath = File.ReadAllText filePath |> Hash.sha256
            let combinedHash = 
              metadata.SourceFiles
              |> Seq.sort
              |> Seq.map fileHash 
              |> Seq.reduce (fun a b -> a + b) |> Hash.sha256
            
            let maybeShort = if options.CacheShortHash then Hash.short else id
              
            Path.Combine(options.CacheDir.TrimEnd('\\','/'), $"{combinedHash |> maybeShort}.dll")
          else
            $"{Path.GetTempFileName()}.dll"
       
        match outputDllName with
        | path when File.Exists path ->
          log $"Found and loading cached assembly: %s{path}"

          if options.AutoLoadNugetReferences then 
            log $"Loading cached NuGet resolutions file: %s{metadata.FilePath}"
            metadata.NuGets |> loadNuGetAssemblies
          return {
            AssemblyFilePath = path
            Assembly = Lazy<Assembly>(fun () -> path |> Path.GetFullPath |> Assembly.LoadFile)
          }
          
        | path ->
         
          let refs =  metadata.NuGets  |> Seq.map (sprintf "-r:%s") |> Seq.toList
          if options.AutoLoadNugetReferences then  metadata.NuGets  |> loadNuGetAssemblies
          
          let compilerArgs =
            [
              yield! options.Compiler.Args entryFilePath refs options.Compiler
              $"--out:{path}"
            ]

          log (sprintf "Compiling with args: %s" (compilerArgs |> String.concat " "))
          
          let getAssemblyOrThrow (errors: FSharpDiagnostic array) (getAssembly: unit -> Assembly) =
            match errors with
            | xs when xs |> Array.exists (fun x -> x.Severity = FSharpDiagnosticSeverity.Error) ->
              raise (ScriptCompileError (errors |> Seq.map string))
            | xs ->
              xs |> Seq.iter (string >> log)
              getAssembly ()
          
          let getAssembly () =
            async {          
                let! errors, _ = checker.Compile(compilerArgs |> List.toArray, "None")
                return getAssemblyOrThrow errors (fun () -> path |> Path.GetFullPath |> Assembly.LoadFile)
            }
          let! assembly = getAssembly ()

          if options.LogListTypes then
            assembly.GetTypes() |> Seq.iter (fun t -> log t.FullName)
          
          return {
            AssemblyFilePath = outputDllName
            Assembly = Lazy<Assembly>(fun () -> path |> Path.GetFullPath |> Assembly.LoadFile)
          }
      }

  open Internals

  let resolveNugets (projOptions: FSharpProjectOptions) =
      async {
        let! projResults = checker.ParseAndCheckProject(projOptions)
        return
          match projResults.HasCriticalErrors with
          | false -> 
            projResults.DependencyFiles 
              |> Seq.choose(
                function
                | path when path.EndsWith(".dll") -> Some path
                | _ -> None)
              |> Seq.groupBy id
              |> Seq.map fst
          | _ -> raise (ScriptParseError (projResults.Diagnostics |> Seq.map string))
      }

  let getAssembly (options: Options) (script:Script) : Async<CompileOutput> =
    let filePath = script |> ensureScriptFile
    async {

      let pathHash = filePath |> Hash.sha256 |> fun x -> x[0..10]

      let workDir = Path.Combine(options.CacheDir, pathHash)

      Directory.CreateDirectory workDir |> ignore

      let cacheFilePath = Path.Combine(workDir, "cache")

      let! metadataResult =
        async { 

        let buildMetadata () =
          async {
            let source = File.ReadAllText filePath |> SourceText.ofString
            let! projOptions, errors = checker.GetProjectOptionsFromScript(filePath, source)
            
            match errors with
              | [] -> 
                  let metadata = 
                    { ScriptCache.Default 
                          with
                            FilePath = cacheFilePath
                            SourceFiles = projOptions.SourceFiles |> Seq.toList                         
                    }
                  
                  if options.Compiler.Standalone then return Ok metadata 
                  else
                    let! nugetPaths = resolveNugets projOptions
                    return Ok ({metadata with NuGets = nugetPaths |> Seq.toList}) 
              | errors -> return Error(errors)
          }

        if File.Exists cacheFilePath then
          let c = ScriptCache.Load cacheFilePath
          let allFilesExist = c.SourceFiles |> Seq.map File.Exists |> Seq.reduce (&&)
          if not <| allFilesExist then
            return! buildMetadata ()
          else
            return Ok c
        else
          return! buildMetadata ()
        }

      match metadataResult with
      | Ok metadata ->
        metadata.Save()
        return! compileScript filePath metadata {options with CacheDir = workDir}
      | Error errors -> return raise (ScriptParseError (errors |> Seq.map string) )
    }

  let getMember<'a> (options: Options) (Path pathA: Member<'a>) (script:Script) : Async<'a> =
    async {
      let! output = script |> getAssembly options
      return output.Assembly.Value |> Member.get pathA
    }

  let getMember2<'a,'b> (options: Options) (Path pathA: Member<'a>) (Path pathB: Member<'b>) (script:Script) : Async<'a * 'b> =
    async {
      let! output = script |> getAssembly options
      return
        output.Assembly.Value |> Member.get<'a> pathA,
        output.Assembly.Value |> Member.get<'b> pathB
    }

  let getMember3<'a,'b,'c> (options: Options) (Path pathA: Member<'a>) (Path pathB: Member<'b>) (Path pathC: Member<'c>) (script:Script) : Async<'a * 'b * 'c> =
    async {
      let! output = script |> getAssembly options
      return
        output.Assembly.Value |> Member.get<'a> pathA,
        output.Assembly.Value |> Member.get<'b> pathB,
        output.Assembly.Value |> Member.get<'c> pathC
    }

  let getMember4<'a,'b,'c,'d> (options: Options) (Path pathA: Member<'a>) (Path pathB: Member<'b>) (Path pathC: Member<'c>) (Path pathD: Member<'d>) (script:Script) : Async<'a * 'b * 'c * 'd> =
    async {
      let! output = script |> getAssembly options
      return
        output.Assembly.Value |> Member.get<'a> pathA,
        output.Assembly.Value |> Member.get<'b> pathB,
        output.Assembly.Value |> Member.get<'c> pathC,
        output.Assembly.Value |> Member.get<'d> pathD
    }
