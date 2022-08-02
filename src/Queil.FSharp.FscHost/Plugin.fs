namespace Queil.FSharp.FscHost

open Queil.FSharp.FscHost

module Plugin =
  
  type PluginOptions =
    {
      script: Script
      bindingName: string
      options: Options
    }

    with
      static member internal Default: PluginOptions = {
        script = Inline ""
        bindingName = "plugin"
        options = Options.Default
      }
  
  type PluginBase<'a>(state: PluginOptions) =      
      member x.State = state
      member x.Yield _ = x
      member x.Run(state: PluginOptions) =
        async {
          let! asm = state.script |> CompilerHost.getAssembly state.options          

          let candidateTypes =
            asm.GetTypes()
            |> Seq.sortBy (fun typ -> typ.FullName.Split('+', '.').Length)
            |> Seq.tryFind (fun typ -> match typ.GetMember(state.bindingName) |> Seq.toList with | [] -> false | _ -> true)
            |> Option.toList

          return Member.getCore<'a> candidateTypes state.bindingName
        }
      
      /// Controls script caching behaviour. Default: caching is off
      [<CustomOperation("cache")>]
      member x.Cache(state: PluginOptions, useCache: bool) =
        let options = {state.options with UseCache = useCache} 
        { state with options = options }
      
      /// Enables a custom logging function
      [<CustomOperation("log")>]
      member x.Log(state: PluginOptions, logFun: string -> unit) =
        let options = {state.options with Logger =  logFun} 
        { state with options = options }
     
      /// Defines the name of a binding to extract. Default: plugin
      [<CustomOperation("binding")>]
      member x.Binding(state: PluginOptions, name: string) =
        { state with bindingName = name }
      
      /// Enables customization of a subset of compiler options
      [<CustomOperation("compiler")>]
      member x.Compiler(state: PluginOptions, configure: CompilerOptions -> CompilerOptions) =
         let compiler = configure state.options.Compiler
         let options = { state.options with Compiler = compiler }
         { state with options = options }
  type InlineScriptPlugin<'a>(state: PluginOptions) =
     inherit PluginBase<'a>(state)
     
     /// Defines the body of a script to compile
     [<CustomOperation("body")>]
     member x.Body(state: PluginBase<'a>, script: string) =
       { state.State with script = Inline script }        

  type FileScriptPlugin<'a>(state: PluginOptions) =
     inherit PluginBase<'a>(state)
     
     /// Defines file path of an F# script (fsx) to compile
     [<CustomOperation("path")>]
     member x.Path(state: PluginBase<'a>, path: string) =
       { state.State with script = File path }

  /// Compiles an F# script given by 'body' and extracts the value of 'let plugin = ...' binding
  /// If multiple bindings are found it extracts the value of the least nested one.
  let plugin_inline<'a> = InlineScriptPlugin<'a>(PluginOptions.Default)
  
  /// Compiles an F# script file given by 'path' and extracts the value of 'let plugin = ...' binding.
  /// If multiple bindings are found it extracts the value of the least nested one.
  let plugin<'a> = FileScriptPlugin<'a>(PluginOptions.Default)
