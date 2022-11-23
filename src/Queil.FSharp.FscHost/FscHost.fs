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
        $"--targetprofile:%s{opts.TargetProfile}"
        $"--target:%s{opts.Target}"
        $"--warn:%i{opts.WarningLevel}"
        yield! refs
        match opts.IncludeHostEntryAssembly with | true -> $"-r:%s{Assembly.GetEntryAssembly().GetName().Name}" |_ -> ()
        match opts.LangVersion with | Some ver -> $"--langversion:%s{ver}" | _ -> ()
        for s in opts.Symbols do
          $"--define:%s{s}"
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
      CachePath = Path.Combine(".fsc-host", "cache")
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
          $"%s{Path.GetTempPath()}%s{Path.GetFileNameWithoutExtension path}.fsx"

      let createInlineScriptFile (filePath:string) =
        function
        | Inline body -> File.WriteAllText(filePath, body)
        | _ -> ()

      let path = script |> getScriptFilePath
      script |> createInlineScriptFile path
      path

    let compileScript (entryFilePath:string) (projOptions:FSharpProjectOptions) (options: Options) (resolveNugets:FSharpProjectOptions -> Async<string seq>) : Async<Assembly> =

      let log = options.Logger
      let loadNuGetAssemblies nugetPaths =
        nugetPaths |> Seq.iter (fun path -> 
          log $"Loading assembly: %s{path}"
          path |> Assembly.LoadFrom |> ignore
        )

      async {

        if options.UseCache then
          Directory.CreateDirectory options.CachePath |> ignore

        let maybeCachedFileName =
          if options.UseCache then
            use sha256 = SHA256.Create()
            let computeHash (s:string) = s |> Encoding.UTF8.GetBytes |> sha256.ComputeHash |> BitConverter.ToString |> fun s -> s.Replace("-", "")
            let fileHash filePath = File.ReadAllText filePath |> computeHash
            let combinedHash = projOptions.SourceFiles |> Seq.map fileHash |> Seq.reduce (fun a b -> a + b) |> computeHash
            Some (Path.Combine(options.CachePath.TrimEnd('\\','/'), $"{combinedHash}.dll"))
          else None
        
        match maybeCachedFileName with
        | Some path when File.Exists path ->
          log $"Found and loading cached assembly: %s{path}"
          let nuGetFile = Path.ChangeExtension (path, "nuget")
          log $"Loading cached NuGet resolutions file: %s{nuGetFile}"
          nuGetFile |> File.ReadAllLines |> loadNuGetAssemblies
          return path |> Path.GetFullPath |> Assembly.LoadFile
          
        | maybePath ->
          let! nuGetsPaths = resolveNugets projOptions
          let refs = nuGetsPaths |> Seq.map (sprintf "-r:%s") |> Seq.toList
          nuGetsPaths |> loadNuGetAssemblies
          maybePath |> Option.iter (fun path -> 
            let nuGetFile = Path.ChangeExtension (path, "nuget")
            log $"Caching resolved NuGets to: %s{nuGetFile}"
            (nuGetFile, nuGetsPaths) |> File.WriteAllLines
          )

          let compilerArgs =
            [
              yield! options.Compiler.Args entryFilePath refs options.Compiler
              match maybePath with
              | Some path -> $"--out:{path}"
              | None -> ()
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

  open Internals

  let getAssembly (options: Options) (script:Script) : Async<Assembly> =
    let filePath = script |> ensureScriptFile
    async {
      let source = File.ReadAllText filePath |> SourceText.ofString
      let! projOptions, errors = checker.GetProjectOptionsFromScript(filePath, source)
      match errors with
      | [] -> return! compileScript filePath projOptions options resolveNugets
      | _ -> return raise (ScriptParseError (errors |> Seq.map string) )
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
