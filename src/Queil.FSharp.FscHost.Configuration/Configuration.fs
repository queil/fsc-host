namespace Queil.FSharp.FscHost.Configuration

open System.IO
open System.Text.Json

type Configuration =
    { IsDefault: bool
      Verbose: bool
      RootScriptFilePath: string option
      OutputRootDir: string
      ScriptOutputRootDir: string option
      ScriptOutputVersionDir: string option }

    static member Default =
        { IsDefault = true
          RootScriptFilePath = None
          Verbose = false
          OutputRootDir = Path.Combine(Path.GetTempPath(), ".fsch")
          ScriptOutputRootDir = None
          ScriptOutputVersionDir = None }

module Configure =

    let render key =
        if File.Exists key then
            File.ReadAllText key
            |> JsonSerializer.Deserialize<Configuration>
            |> Option.ofObj
            |> _.Value
        else
            Configuration.Default
