namespace Queil.FSharp.FscHost.Plugin

module CE =

  type State<'a> =
    {
      filePath: string
    }

  type PluginBuilder<'a>() =
     [<CustomOperation("file")>]
     member x.File(path:string, state: State<'a>) =
       {state with filePath = path}

  let plugin<'a> = PluginBuilder<'a>()
  
module Test =
  
  open CE
  let plugin =
    
    
    plugin<int> {
        file "test"
    }