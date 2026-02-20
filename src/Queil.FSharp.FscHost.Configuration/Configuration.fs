namespace Queil.FSharp.FscHost.Configuration

open System
open System.IO
open System.Text.Json

[<AttributeUsage(AttributeTargets.Assembly ||| AttributeTargets.Class, AllowMultiple = false)>]
type DependencyManagerAttribute() =
    inherit Attribute()

type ResolveDependenciesResult
    (
        success: bool,
        stdOut: string array,
        stdError: string array,
        resolutions: string seq,
        sourceFiles: string seq,
        roots: string seq
    ) =

    member _.Success = success
    member _.StdOut = stdOut
    member _.StdError = stdError
    member _.Resolutions = resolutions
    member _.SourceFiles = sourceFiles
    member _.Roots = roots

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
