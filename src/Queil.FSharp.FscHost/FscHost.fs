namespace Queil.FSharp

open FSharp.Compiler.SourceCodeServices
open System.IO
open System.Reflection
open FSharp.Compiler.Text

module FscHost =

  type Script = | File of path: string | Inline of body: string
  type ScriptInput = {
    Script: Script
    MemberFqName: string
  }

  type Member<'a> = | Path of string

  exception NuGetRestoreFailed of message: string
  exception ScriptParseError of raises: string seq
  exception ScriptCompileError of raises: string seq
  exception ScriptModuleNotFound of path: string * moduleName: string
  exception ScriptsPropertyHasInvalidType of propertyName: string * actualType: System.Type
  exception ScriptsPropertyNotFound of propertyName: string * foundProperties: string list
  exception ExpectedMemberParentTypeNotFound of memberPath: string
  exception MultipleMemberParentTypeCandidatesFound of memberPath: string

  type ScriptExtractOptions = 
    {
      Verbose: bool
    }
  with 
    static member Default = 
      {
        Verbose = false
      }

  [<RequireQualifiedAccess>]
  module Member =
    let get<'a> (memberPath:string) (options: ScriptExtractOptions) (assembly:Assembly) =

      let (fqTypeName, memberName) =
        let splitIndex = memberPath.LastIndexOf(".")
        memberPath.[0..splitIndex - 1], memberPath.[splitIndex + 1..]

      let candidates = assembly.GetTypes() |> Seq.where (fun t -> t.FullName = fqTypeName) |> Seq.toList
      if options.Verbose then assembly.GetTypes() |> Seq.iter (fun t ->  printfn "%s" t.FullName)

      match candidates with
      | [t] ->
        match t.GetProperty(memberName, BindingFlags.Static ||| BindingFlags.Public) with
        | null -> raise (ScriptsPropertyNotFound (
                           memberPath,
                          t.GetProperties() |> Seq.map (fun p -> p.Name) |> Seq.toList))
        | p ->
          try
            p.GetValue(null) :?> 'a
          with
          | :? System.InvalidCastException -> raise (ScriptsPropertyHasInvalidType ( memberPath, p.PropertyType))

      | [] -> raise (ExpectedMemberParentTypeNotFound ( memberPath))
      | _ -> raise (MultipleMemberParentTypeCandidatesFound ( memberPath))

  [<RequireQualifiedAccess>]
  module CompilerHost =

    let private (>>=) (a) (func) = (a,func) |> async.Bind

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
    
      let compileScript (filePath:string) (options: ScriptExtractOptions) (nugetResolutions:string seq) =
        async {
          
          let refs = nugetResolutions |> Seq.map (sprintf "-r:%s")
          nugetResolutions |> Seq.iter (Assembly.LoadFrom >> ignore)

          let compilerArgs = [|
            "-a"; filePath
            "--targetprofile:netcore"
            "--target:module"
            yield! refs
            sprintf "-r:%s" (Assembly.GetEntryAssembly().GetName().Name)
            "--langversion:preview"
          |]

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
              | _ -> raise (ScriptParseError (projResults.Errors |> Seq.map string) )
          | _ -> return raise (ScriptParseError (errors |> Seq.map string) )
        }
    
    open Internals

    let getAssembly (options: ScriptExtractOptions) (script:Script) : Async<Assembly> =
      let filePath = script |> ensureScriptFile
      resolveNugets filePath >>= (compileScript filePath options)

    let getScriptMember<'a> (Path pathA: Member<'a>) (options: ScriptExtractOptions) (script:Script) : Async<'a> =
      async {
        let! assembly = script |> getAssembly options
        return assembly |> Member.get pathA options
      }

    let getScriptMembers2<'a,'b> (Path pathA: Member<'a>) (Path pathB: Member<'b>) (options: ScriptExtractOptions) (script:Script) : Async<'a * 'b> =
      async {
        let! assembly = script |> getAssembly options
        return
          assembly |> Member.get<'a> pathA options,
          assembly |> Member.get<'b> pathB options
      }

    let getScriptMembers3<'a,'b,'c> (Path pathA: Member<'a>) (Path pathB: Member<'b>) (Path pathC: Member<'c>) (options: ScriptExtractOptions) (script:Script) : Async<'a * 'b * 'c> =
      async {
        let! assembly = script |> getAssembly options
        return
          assembly |> Member.get<'a> pathA options,
          assembly |> Member.get<'b> pathB options,
          assembly |> Member.get<'c> pathC options
      }

    let getScriptMembers4<'a,'b,'c,'d> (Path pathA: Member<'a>) (Path pathB: Member<'b>) (Path pathC: Member<'c>)  (Path pathD: Member<'d>) (options: ScriptExtractOptions) (script:Script) : Async<'a * 'b * 'c * 'd> =
      async {
        let! assembly = script |> getAssembly options
        return
          assembly |> Member.get<'a> pathA options,
          assembly |> Member.get<'b> pathB options,
          assembly |> Member.get<'c> pathC options,
          assembly |> Member.get<'d> pathD options
      }