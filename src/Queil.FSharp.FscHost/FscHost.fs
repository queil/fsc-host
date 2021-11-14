namespace Queil.FSharp

open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text
open System
open System.IO
open System.Reflection
open System.Text
open System.Security.Cryptography

module FscHost =

  type Script = | File of path: string | Inline of body: string
  type Property<'a> = | Path of string
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

  exception NuGetRestoreFailed of message: string
  exception ScriptParseError of errors: string seq
  exception ScriptCompileError of errors: string seq
  exception ScriptModuleNotFound of path: string * moduleName: string
  exception ScriptMemberHasInvalidType of memberName: string * actualTypeSignature: string
  exception ScriptMemberNotFound of memberName: string * foundMembers: string list
  exception ExpectedMemberParentTypeNotFound of memberPath: string
  exception MultipleMemberParentTypeCandidatesFound of memberPath: string

  [<RequireQualifiedAccess>]
  module Property =
    open FSharp.Reflection
    open FSharp.Quotations
    open FSharp.Linq.RuntimeHelpers
    open System.Collections.Generic
    
    let (|FsFunc|_|) (t:Type) =
      match t with
      | t when FSharpType.IsFunction t -> Some (FSharpType.GetFunctionElements t)
      | _ -> None
    
    let (|FsTuple|_|) (t:Type) =
      match t with
      | t when FSharpType.IsTuple t -> Some (FSharpType.GetTupleElements t |> Seq.toList)
      | _ -> None
    
    let (|GenericFunc|_|) (t:Type) =
      match t with
      | t when FSharpType.IsFunction t -> Some (FSharpType.GetFunctionElements t )
      | _ -> None
    
    type State = {
      Vars: Var list
      Index: int
      TupleVar: Var option
      GenericParams: Dictionary<Type, Type>

    }
    with
      static member Empty = {
        Vars = []
        Index = 0
        TupleVar = None
        GenericParams = Dictionary<Type, Type>()
      }

    let private toExpr (funcType: Type) (methodInfo:MethodInfo) : Expr = 
      let parameters = methodInfo.GetParameters() |> Seq.toList
      let funTypes = funcType |> Seq.unfold (function | FsFunc (a, b) -> Some (a, b) |_ -> None) |> Seq.toList

      let handleGenerics (f:Type) (p:ParameterInfo) (s:State) =
        let rec handle f (p:Type) s =
          match (f, p) with
          | f, p when f = p -> ()
          | f, p when p.IsGenericMethodParameter -> s.GenericParams.TryAdd(p, f) |> ignore
          | (FsFunc (fi,fo)), (FsFunc (pi,po)) ->
            handle fi pi s
            handle fo po s
          | _ -> ()
        handle f p.ParameterType s

      let rec build (fs: Type list) (ps:ParameterInfo list) (s: State) =
        match (fs, ps) with
        | f::fs', p::ps' when f = p.ParameterType || p.ParameterType.IsGenericMethodParameter ->
          handleGenerics f p s
          let v = Var(p.Name, f)
          Expr.Lambda(v, build fs' ps' { s with Vars = v::s.Vars })

        | (FsTuple us as f)::fs', p::ps' ->
          handleGenerics f p s

          if s.Index < us.Length then
            let v = Var($"{p.Name}_{s.Index}", us.[s.Index])
            let expr tv = Expr.Let(v, Expr.TupleGet(Expr.Var tv, s.Index),
              build fs ps' {s with Vars = v::s.Vars; Index = s.Index+1; TupleVar = Some tv})

            match s.TupleVar with
            | Some tv -> expr tv
            | None -> 
              let tupleVar = Var("tupledArg", f)
              Expr.Lambda(tupleVar, expr tupleVar)
          else
            build fs' ps { s with Index = 0; TupleVar = None }

        | (FsFunc _ as f)::fs', p::ps' ->
          handleGenerics f p s
          let v = Var(p.Name, f)
          Expr.Lambda(v, build fs' ps' { s with Vars = v::s.Vars })

        | _ ->
          let vars = s.Vars |> Seq.map(Expr.Var) |> Seq.rev |> Seq.toList

          let maybeGeneric =
            if methodInfo.IsGenericMethod then
              let gps = methodInfo.GetGenericArguments() |> Seq.map (fun t -> s.GenericParams.[t]) |> Seq.toArray
              methodInfo.MakeGenericMethod(gps)
            else
              methodInfo

          Expr.Call(maybeGeneric, vars)

      match parameters with
      | [] -> Expr.Lambda(Var("()", typeof<unit>), build funTypes [] State.Empty)
      | ps -> build funTypes ps State.Empty

    let get<'a> (memberPath:string) (assembly:Assembly) =

      let (fqTypeName, memberName) =
        let splitIndex = memberPath.LastIndexOf(".")
        memberPath.[0..splitIndex - 1], memberPath.[splitIndex + 1..]

      let candidates = assembly.GetTypes() |> Seq.where (fun t -> t.FullName = fqTypeName) |> Seq.toList
      
      let tryCast (actualType:string) (value:obj) =
        try
          value :?> 'a
        with
        | :? InvalidCastException -> raise (ScriptMemberHasInvalidType (memberPath, actualType))

      let (|Property|_|) (name:string) (t:Type) =
        match t.GetProperty(name, BindingFlags.Static ||| BindingFlags.Public) with
        | null -> None
        | pi -> Some pi

      let (|Method|_|) (name:string) (t:Type) =
        match t.GetMethod(name, BindingFlags.Static ||| BindingFlags.Public) with
        | null -> None
        | mi when FSharpType.IsFunction typeof<'a> -> Some mi
        | _ -> None

      match candidates with
      | [Property memberName info] ->
        info.GetValue(null) |> tryCast (info.PropertyType.ToString())
      | [Method memberName info] ->
        info |> toExpr typeof<'a>
             |> (fun x -> printfn "%A" x; x)
             |> LeafExpressionConverter.EvaluateQuotation
             |> tryCast (info.ToString())
      | [t] -> raise (ScriptMemberNotFound (memberPath, t.GetMembers() |> Seq.map (fun p -> $"{p.MemberType}: {p.Name}") |> Seq.toList))
      | [] -> raise (ExpectedMemberParentTypeNotFound memberPath)
      | _ -> raise (MultipleMemberParentTypeCandidatesFound memberPath)

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

    let getScriptProperty<'a> (options: Options) (Path pathA: Property<'a>) (script:Script) : Async<'a> =
      async {
        let! assembly = script |> getAssembly options
        return assembly |> Property.get pathA
      }

    let getScriptProperties2<'a,'b> (options: Options) (Path pathA: Property<'a>) (Path pathB: Property<'b>) (script:Script) : Async<'a * 'b> =
      async {
        let! assembly = script |> getAssembly options
        return
          assembly |> Property.get<'a> pathA,
          assembly |> Property.get<'b> pathB
      }

    let getScriptProperties3<'a,'b,'c> (options: Options) (Path pathA: Property<'a>) (Path pathB: Property<'b>) (Path pathC: Property<'c>) (script:Script) : Async<'a * 'b * 'c> =
      async {
        let! assembly = script |> getAssembly options
        return
          assembly |> Property.get<'a> pathA,
          assembly |> Property.get<'b> pathB,
          assembly |> Property.get<'c> pathC
      }

    let getScriptProperties4<'a,'b,'c,'d> (options: Options) (Path pathA: Property<'a>) (Path pathB: Property<'b>) (Path pathC: Property<'c>) (Path pathD: Property<'d>) (script:Script) : Async<'a * 'b * 'c * 'd> =
      async {
        let! assembly = script |> getAssembly options
        return
          assembly |> Property.get<'a> pathA,
          assembly |> Property.get<'b> pathB,
          assembly |> Property.get<'c> pathC,
          assembly |> Property.get<'d> pathD
      }
