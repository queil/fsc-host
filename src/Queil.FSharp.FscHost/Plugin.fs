namespace Queil.FSharp.FscHost

open Queil.FSharp.FscHost

module Plugin =
  
  type PluginBuilder<'a> =
    {
      script: Script
      bindingName: string
      options: Options
    }

    with
      static member internal Default: PluginBuilder<'a> = {
        script = Inline ""
        bindingName = "plugin"
        options = Options.Default
      }
  
  type PluginBase<'a>(state: PluginBuilder<'a>) =      
      member x.State = state
      member x.Yield _ = x
      member x.Run(state: PluginBuilder<'a>) =
        async {
          let! asm = state.script |> CompilerHost.getAssembly state.options
          let expectedMemberName = state.bindingName
          
          let candidateTypes =
            asm.GetTypes()
            |> Seq.sortBy (fun typ -> typ.FullName.Split('+', '.').Length)
            |> Seq.tryFind (fun typ -> match typ.GetMember(expectedMemberName) |> Seq.toList with | [] -> false | _ -> true)
            |> Option.toList

          return Member.getCore<'a> candidateTypes expectedMemberName
        }
      
      /// Controls script caching behaviour
      [<CustomOperation("cache")>]
      member x.Cache(state: PluginBuilder<'a>, useCache: bool) =
        let options = {state.options with UseCache = useCache} 
        { state with options = options }
      
      /// Enables a custom logging function
      [<CustomOperation("log")>]
      member x.Log(state: PluginBuilder<'a>, logFun: string -> unit) =
        let options = {state.options with Logger =  logFun} 
        { state with options = options }
     
      /// Defines the name of a binding to extract. Default: plugin
      [<CustomOperation("binding")>]
      member x.Binding(state: PluginBuilder<'a>, name: string) =
        { state with bindingName = name }
      
      /// Enables customization of a subset of compiler options
      [<CustomOperation("compiler")>]
      member x.Compiler(state: PluginBuilder<'a>, configure: CompilerOptions -> CompilerOptions) =
         let compiler = configure state.options.Compiler
         let options = { state.options with Compiler = compiler }
         { state with options = options }
  type InlineScriptPlugin<'a>(state: PluginBuilder<'a>) =
     inherit PluginBase<'a>(state)
     
     /// Defines the body of a script to compile
     [<CustomOperation("body")>]
     member x.Body(state: PluginBase<'a>, script: string) =
       { state.State with script = Inline script }        

  type FileScriptPlugin<'a>(state: PluginBuilder<'a>) =
     inherit PluginBase<'a>(state)
     
     /// Defines file path of an F# script (fsx) to compile
     [<CustomOperation("path")>]
     member x.Path(state: PluginBase<'a>, path: string) =
       { state.State with script = File path }

  /// Compiles an F# script given by 'body' and extracts the value of 'let plugin = ...' binding
  /// If multiple bindings are found it extracts the value of the least nested one.
  let plugin_inline<'a> = InlineScriptPlugin<'a>(PluginBuilder.Default)
  
  /// Compiles an F# script file given by 'path' and extracts the value of 'let plugin = ...' binding.
  /// If multiple bindings are found it extracts the value of the least nested one.
  let plugin<'a> = FileScriptPlugin<'a>(PluginBuilder.Default)
