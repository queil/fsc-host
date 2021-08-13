namespace Queil.FSharp

open FSharp.Compiler.CodeAnalysis
open System.IO
open System.Reflection
open FSharp.Compiler.Text

module FscHost =

  type Script = | File of path: string | Inline of body: string
  type Property<'a> = | Path of string

  type CompilerOptions = {
    Target: string
    TargetProfile: string
    LangVersion: string option
    Args: string -> string list -> CompilerOptions -> string array
    IncludeHostEntryAssembly: bool
  }
  with
    static member Default =
     {
       LangVersion = None
       Target = "module"
       TargetProfile = "netcore"
       IncludeHostEntryAssembly = true
       Args = fun scriptPath refs opts -> [|
        "-a"; scriptPath
        sprintf "--targetprofile:%s" opts.TargetProfile
        sprintf "--target:%s" opts.Target
        yield! refs
        match opts.IncludeHostEntryAssembly with | true -> sprintf "-r:%s" (Assembly.GetEntryAssembly().GetName().Name) |_ -> ()
        match opts.LangVersion with | Some ver -> sprintf "--langversion:%s" ver | _ -> ()
      |]
     }

  type Options = 
    {
      Compiler: CompilerOptions
      Verbose: bool
    }
  with 
    static member Default =
      {
        Compiler = CompilerOptions.Default
        Verbose = false
      }

  exception NuGetRestoreFailed of message: string
  exception ScriptParseError of errors: string seq
  exception ScriptCompileError of errors: string seq
  exception ScriptModuleNotFound of path: string * moduleName: string
  exception ScriptsPropertyHasInvalidType of propertyName: string * actualType: System.Type
  exception ScriptsPropertyNotFound of propertyName: string * foundProperties: string list
  exception ExpectedMemberParentTypeNotFound of memberPath: string
  exception MultipleMemberParentTypeCandidatesFound of memberPath: string

  [<RequireQualifiedAccess>]
  module Property =
    let get<'a> (memberPath:string) (options: Options) (assembly:Assembly) =

      let (fqTypeName, memberName) =
        let splitIndex = memberPath.LastIndexOf(".")
        memberPath.[0..splitIndex - 1], memberPath.[splitIndex + 1..]

      let candidates = assembly.GetTypes() |> Seq.where (fun t -> t.FullName = fqTypeName) |> Seq.toList
      if options.Verbose then assembly.GetTypes() |> Seq.iter (fun t ->  printfn "%s" t.FullName)

      match candidates with
      | [t] ->
        match t.GetProperty(memberName, BindingFlags.Static ||| BindingFlags.Public) with
        | null -> raise (ScriptsPropertyNotFound (memberPath, t.GetProperties() |> Seq.map (fun p -> p.Name) |> Seq.toList))
        | p ->
          try
            p.GetValue(null) :?> 'a
          with
          | :? System.InvalidCastException -> raise (ScriptsPropertyHasInvalidType ( memberPath, p.PropertyType))
      | [] -> raise (ExpectedMemberParentTypeNotFound ( memberPath))
      | _ -> raise (MultipleMemberParentTypeCandidatesFound ( memberPath))

  [<RequireQualifiedAccess>]
  module CompilerHost =

    module private Internals =
      let checker = FSharpChecker.Create()
    
      let ensureScriptFile (script:Script) =
        let getScriptFilePath =
          function
          | File path -> path
          | Inline _ -> 
            let path = Path.GetTempFileName()
            sprintf "%s/%s.fsx" (Path.GetTempPath()) (Path.GetFileNameWithoutExtension path)

        let createInlineScriptFile (filePath:string) =
          function
          | Inline body -> File.WriteAllText(filePath, body)
          | _ -> ()

        let path = script |> getScriptFilePath
        script |> createInlineScriptFile path
        path
    
      let compileScript (filePath:string) (options: Options) (nugetResolutions:string seq) =
        async {
          
          let refs = nugetResolutions |> Seq.map (sprintf "-r:%s") |> Seq.toList
          nugetResolutions |> Seq.iter (Assembly.LoadFrom >> ignore)
          let compilerArgs = options.Compiler.Args filePath refs options.Compiler
          
          if options.Verbose then printfn "Compiler args: %s" (compilerArgs |> String.concat " ")

          let! errors, _, maybeAssembly =
            checker.CompileToDynamicAssembly(compilerArgs, None)
          
          return
            match maybeAssembly with
            | Some x -> x
            | None -> raise (ScriptCompileError (errors |> Seq.map (fun d -> d.ToString())))
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
              | _ -> raise (ScriptParseError (projResults.Diagnostics |> Seq.map string) )
          | _ -> return raise (ScriptParseError (errors |> Seq.map string) )
        }
    
    open Internals

    let getAssembly (options: Options) (script:Script) : Async<Assembly> =
      let filePath = script |> ensureScriptFile
      async {
        let! nugets = resolveNugets filePath 
        return! nugets |> compileScript filePath options
      }

    let getScriptProperty<'a> (Path pathA: Property<'a>) (options: Options) (script:Script) : Async<'a> =
      async {
        let! assembly = script |> getAssembly options
        return assembly |> Property.get pathA options
      }

    let getScriptProperties2<'a,'b> (Path pathA: Property<'a>) (Path pathB: Property<'b>) (options: Options) (script:Script) : Async<'a * 'b> =
      async {
        let! assembly = script |> getAssembly options
        return
          assembly |> Property.get<'a> pathA options,
          assembly |> Property.get<'b> pathB options
      }

    let getScriptProperties3<'a,'b,'c> (Path pathA: Property<'a>) (Path pathB: Property<'b>) (Path pathC: Property<'c>) (options: Options) (script:Script) : Async<'a * 'b * 'c> =
      async {
        let! assembly = script |> getAssembly options
        return
          assembly |> Property.get<'a> pathA options,
          assembly |> Property.get<'b> pathB options,
          assembly |> Property.get<'c> pathC options
      }

    let getScriptProperties4<'a,'b,'c,'d> (Path pathA: Property<'a>) (Path pathB: Property<'b>) (Path pathC: Property<'c>)  (Path pathD: Property<'d>) (options: Options) (script:Script) : Async<'a * 'b * 'c * 'd> =
      async {
        let! assembly = script |> getAssembly options
        return
          assembly |> Property.get<'a> pathA options,
          assembly |> Property.get<'b> pathB options,
          assembly |> Property.get<'c> pathC options,
          assembly |> Property.get<'d> pathD options
      }