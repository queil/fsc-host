namespace Queil.FSharp.FscHost

open System
open Queil.FSharp.FscHost

module Plugin =

  type PluginBuilder<'a> =
    {
      script: Script
      plugin: 'a option
      options: Options
    }

    with
      static member internal Default: PluginBuilder<'a> = {
        script = Inline ""
        plugin = None
        options = Options.Default
      }
  
  type PluginBuilder<'a> with

     
     [<CustomOperation("file")>]
     member x.File(state: PluginBuilder<'a>, path: string) =
       { state with script = File path  }
     
     [<CustomOperation("script")>]
     member x.Inline(state: PluginBuilder<'a>, script: string) =
       { state with script = Inline script  }
       
     member x.Yield _ = x
     
     member x.Run(state: PluginBuilder<'a>) =
        async {
          let! asm = state.script |> CompilerHost.getAssembly state.options
          let name =
            match state.script with
            | File path -> IO.Path.GetFileNameWithoutExtension(path)
            | Inline _ -> asm.GetName().Name
          return asm |> Member.getCore<'a> { comparison = StringComparison.CurrentCultureIgnoreCase } $"{name}.plugin"
        }

  let plugin<'a> = PluginBuilder<'a>.Default
