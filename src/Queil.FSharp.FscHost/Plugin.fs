namespace Queil.FSharp.FscHost.Plugin

module CE =

  type State<'a> =
    {
      filePath: string option
      plugin: 'a option
    }
    with
      static member internal Default: State<'a> = {
        filePath = None
        plugin = None
      }

  and PluginBuilder<'a>() =

     [<CustomOperation("file")>]
     member x.File(state: State<'a>, path: string) =
       {state with filePath = Some path}
     
     member x.Delay(f: unit -> State<'a>) = f ()
     
     member x.Combine(builder: State<'a>, newState: State<'a>) =
      { builder with
          filePath = newState.filePath
          plugin = newState.plugin
      }

     member x.Yield (plugin: 'a) : State<'a> =
       {State.Default with plugin = Some plugin}

     member x.Run(state: State<'a>) =
       state.plugin
  
     member x.Zero() = State.Default
  
  let plugin<'a> = PluginBuilder<'a>()
  
open CE

module Test =
  
  open CE
  let plugin2 () =

    plugin<int> {
        
        yield 3
        yield 6
        yield 9
        
    }
    
  