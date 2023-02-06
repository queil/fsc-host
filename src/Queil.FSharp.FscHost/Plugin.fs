namespace Queil.FSharp.FscHost

open Queil.FSharp.FscHost
open System
module Plugin =
  
  type PluginOptions =
    {
      script: Script
      dir: string
      bindingName: string
      options: Options
    }
    with
      static member internal Default: PluginOptions = {
        script = File "plugin.fsx"
        bindingName = "plugin"
        dir = "plugins/default"
        options = Options.Default
      }
  
  type CommonBuilder(state: PluginOptions) =
     member x.State = state
  
  type BodyBuilder(state: PluginOptions) =
    inherit CommonBuilder(state)
 
  type FileBuilder(state: PluginOptions) =
    inherit CommonBuilder(state)
   
  type Plugin<'a>(state: PluginOptions) =

    member x.State = state
    member x.Yield _ = x
    member x.Run(state: CommonBuilder) =
      async {
        let script =
          match state.State.script with
          | File f -> File (IO.Path.Combine(state.State.dir, f))
          | s -> s
        let! output = script |> CompilerHost.getAssembly state.State.options          

        let candidateTypes =
          output.Assembly.GetTypes()
          |> Seq.sortBy (fun typ -> typ.FullName.Split('+', '.').Length)
          |> Seq.tryFind (fun typ -> match typ.GetMember(state.State.bindingName) |> Seq.toList with | [] -> false | _ -> true)
          |> Option.toList

        return Member.getCore<'a> candidateTypes state.State.bindingName
      }
    
    /// Controls script caching behaviour. Default: caching is off
    [<CustomOperation("cache")>]
    member x.Cache(state: CommonBuilder, useCache: bool) =
      let options = {state.State.options with UseCache = useCache} 
      CommonBuilder { state.State with options = options }
    
    /// Overrides the default cache dir path. It is only relevant if cache is enabled. Default: .fsc-host/cache
    [<CustomOperation("cache_dir")>]
    member x.CacheDir(state: CommonBuilder, cacheDir: string) =
      let options = {state.State.options with CacheDir = cacheDir} 
      CommonBuilder { state.State with options = options }
    
    /// Enables a custom logging function
    [<CustomOperation("log")>]
    member x.Log(state: CommonBuilder, logFun: string -> unit) =
      let options = {state.State.options with Logger =  logFun} 
      CommonBuilder { state.State with options = options }
     
    /// Defines the name of a binding to extract. Default: plugin
    [<CustomOperation("binding")>]
    member x.Binding(state: CommonBuilder, name: string) =
      CommonBuilder { state.State with bindingName = name }
      
    /// Enables customization of a subset of compiler options
    [<CustomOperation("compiler")>]
    member x.Compiler(state: CommonBuilder, configure: CompilerOptions -> CompilerOptions) =
      let compiler = configure state.State.options.Compiler
      let options = { state.State.options with Compiler = compiler }
      CommonBuilder { state.State with options = options }
      
    /// The directory plugin gets loaded from. Default: plugins/default
    [<CustomOperation("dir")>]
    member x.Dir(state: FileBuilder, dir: string) =
      FileBuilder { state.State with dir = dir }

    /// Sets plugin script file name. Default: plugin.fsx
    [<CustomOperation("file")>]
    member x.File(state: CommonBuilder, file: string) =
      FileBuilder({ state.State with script = File file })

    /// Defines the body of a script to compile
    [<CustomOperation("body")>]
    member x.Body(state: Plugin<'a>, script: string) =
      BodyBuilder { state.State with script = Inline script }
     
    /// Loads a plugin with default configuration. 
    /// It expects ./plugins/default/plugin.fsx with 'let plugin = ... ' binding matching the
    /// specified plugin type.
    [<CustomOperation("load")>]
    member x.Load(state: Plugin<'a>) =
      FileBuilder state.State
  
  /// Compiles an F# script either defined by 'body' or 'load'
  /// If multiple bindings are found it extracts the value of the least nested one.
  let plugin<'a> = Plugin<'a>(PluginOptions.Default)
