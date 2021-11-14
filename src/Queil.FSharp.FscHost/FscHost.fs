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
}
with
  static member Default =
    {
      Args = fun scriptPath refs opts ->
        [
        "-a"; scriptPath
        sprintf "--targetprofile:%s" opts.TargetProfile
        sprintf "--target:%s" opts.Target
        sprintf "--warn:%i" opts.WarningLevel
        yield! refs
        match opts.IncludeHostEntryAssembly with | true -> sprintf "-r:%s" (Assembly.GetEntryAssembly().GetName().Name) |_ -> ()
        match opts.LangVersion with | Some ver -> sprintf "--langversion:%s" ver | _ -> ()
        for s in opts.Symbols do
          sprintf "--define:%s" s
        ]
      IncludeHostEntryAssembly = true
      LangVersion = None
      Target = "library"
      TargetProfile = "netcore"
      WarningLevel = 3
      Symbols = []
    }

type Options = 
  {
    Compiler: CompilerOptions
    UseCache: bool
    CachePath: string
    Logger: string -> unit
  }
with 
  static member Default =
    {
      Compiler = CompilerOptions.Default
      UseCache = false
      CachePath = ".fsc-host/cache"
      Logger = ignore
    }

[<RequireQualifiedAccess>]
module CompilerHost =
  open Errors

  module private Internals =
    let checker = FSharpChecker.Create()
  
    let ensureScriptFile (script:Script) =
      let getScriptFilePath =
        function
        | File path -> path
        | Inline _ -> 
          let path = Path.GetTempFileName()
          sprintf "%s%s.fsx" (Path.GetTempPath()) (Path.GetFileNameWithoutExtension path)

      let createInlineScriptFile (filePath:string) =
        function
        | Inline body -> File.WriteAllText(filePath, body)
        | _ -> ()

      let path = script |> getScriptFilePath
      script |> createInlineScriptFile path
      path
    
    let compileScript (filePath:string) (options: Options) (resolveNugets:string -> Async<string seq>) : Async<Assembly> =

      let log = options.Logger
      let loadNuGetAssemblies nugetPaths =
        nugetPaths |> Seq.iter (fun path -> 
          sprintf "Loading assembly: %s" path |> log
          path |> Assembly.LoadFrom |> ignore
        )

      async {

        if options.UseCache then
          Directory.CreateDirectory options.CachePath |> ignore

        let maybeCachedFileName =
          if options.UseCache then
            use sha256 = SHA256.Create()
            let checksum = File.ReadAllText filePath |> Encoding.UTF8.GetBytes |> sha256.ComputeHash |> BitConverter.ToString |> fun s -> s.Replace("-", "")
            Some (options.CachePath.TrimEnd('\\','/') + "/" + $"{checksum}.dll")
          else None
        
        match maybeCachedFileName with
        | Some path when File.Exists path ->
          sprintf "Found and loading cached assembly: %s" path |> log
          let nuGetFile = Path.ChangeExtension (path, "nuget")
          sprintf "Loading cached NuGet resolutions file: %s" nuGetFile |> log
          nuGetFile |> File.ReadAllLines |> loadNuGetAssemblies
          return path |> Path.GetFullPath |> Assembly.LoadFile
          
        | maybePath ->
          let! nuGetsPaths = resolveNugets filePath
          let refs = nuGetsPaths |> Seq.map (sprintf "-r:%s") |> Seq.toList
          nuGetsPaths |> loadNuGetAssemblies
          maybePath |> Option.iter (fun path -> 
            let nuGetFile = Path.ChangeExtension (path, "nuget")
            sprintf "Caching resolved NuGets to: %s" nuGetFile |> log
            (nuGetFile, nuGetsPaths) |> File.WriteAllLines
          )

          let compilerArgs =
            [
              yield! options.Compiler.Args filePath refs options.Compiler
              match maybePath with
              | Some path -> $"--out:{path}"
              | None -> ()
            ]

          sprintf "Compiling with args: %s" (compilerArgs |> String.concat " ") |> log
          
          let getAssemblyOrThrow (errors: FSharpDiagnostic array) (getAssembly: unit -> Assembly) =
            match errors with
            | xs when xs |> Array.exists (fun x -> x.Severity = FSharpDiagnosticSeverity.Error) ->
              raise (ScriptCompileError (errors |> Seq.map string))
            | xs ->
              xs |> Seq.iter (string >> log)
              getAssembly ()
          
          let getAssembly () =
            async {
                match maybePath with
                | Some path -> 
                  let! errors, _ = checker.Compile(compilerArgs |> List.toArray, "None")
                  return getAssemblyOrThrow errors (fun () -> path |> Path.GetFullPath |> Assembly.LoadFile)
                | None ->
                  let! errors, _, maybeAssembly = checker.CompileToDynamicAssembly(compilerArgs |> List.toArray, None)
                  return getAssemblyOrThrow errors (fun () -> maybeAssembly.Value)
            }
          let! assembly = getAssembly ()

          assembly.GetTypes() |> Seq.iter (fun t -> log t.FullName)
          
          return assembly
      }

    let resolveNugets (filePath:string) =
      async {
        let source = File.ReadAllText filePath |> SourceText.ofString
        let! projOptions, errors = checker.GetProjectOptionsFromScript(filePath, source)

        match errors with
        | [] -> 
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
                |> Seq.map (fun (path, _) -> path)
            | _ -> raise (ScriptParseError (projResults.Diagnostics |> Seq.map string))
        | _ -> return raise (ScriptParseError (errors |> Seq.map string) )
      }
  
  open Internals

  let getAssembly (options: Options) (script:Script) : Async<Assembly> =
    let filePath = script |> ensureScriptFile
    async {
      return! compileScript filePath options resolveNugets
    }

  let getMember<'a> (options: Options) (Path pathA: Member<'a>) (script:Script) : Async<'a> =
    async {
      let! assembly = script |> getAssembly options
      return assembly |> Member.get pathA
    }

  let getMember2<'a,'b> (options: Options) (Path pathA: Member<'a>) (Path pathB: Member<'b>) (script:Script) : Async<'a * 'b> =
    async {
      let! assembly = script |> getAssembly options
      return
        assembly |> Member.get<'a> pathA,
        assembly |> Member.get<'b> pathB
    }

  let getMember3<'a,'b,'c> (options: Options) (Path pathA: Member<'a>) (Path pathB: Member<'b>) (Path pathC: Member<'c>) (script:Script) : Async<'a * 'b * 'c> =
    async {
      let! assembly = script |> getAssembly options
      return
        assembly |> Member.get<'a> pathA,
        assembly |> Member.get<'b> pathB,
        assembly |> Member.get<'c> pathC
    }

  let getMember4<'a,'b,'c,'d> (options: Options) (Path pathA: Member<'a>) (Path pathB: Member<'b>) (Path pathC: Member<'c>) (Path pathD: Member<'d>) (script:Script) : Async<'a * 'b * 'c * 'd> =
    async {
      let! assembly = script |> getAssembly options
      return
        assembly |> Member.get<'a> pathA,
        assembly |> Member.get<'b> pathB,
        assembly |> Member.get<'c> pathC,
        assembly |> Member.get<'d> pathD
    }
