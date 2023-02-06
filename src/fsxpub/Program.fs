open Queil.FSharp.FscHost
open System.Text.Json
open System.IO
open FsxPub.Cli

let cmd = Args.FromCmdLine()

let compilerOptions = 
    {
        CompilerOptions.Default with
          IncludeHostEntryAssembly = false
          Target = "exe"
          Args = fun scriptPath refs opts -> 
            [
                "--noframework"
                "--standalone"
                "--nowin32manifest"
                yield! CompilerOptions.Default.Args scriptPath refs opts
            ]      
    }
let outDir = cmd.TryGetResult Output_Dir |> Option.defaultValue "./out"

if cmd.Contains Clean && Directory.Exists outDir then
  Directory.Delete(outDir, true)

let options = {
    Options.Default
      with Compiler = compilerOptions
           Logger = if cmd.Contains Verbose then System.Console.WriteLine else ignore
           AutoLoadNugetReferences = false
           UseCache = true
           CacheDir = outDir
}

let scriptFilePath = cmd.GetResult ScriptFilePath

let output = CompilerHost.getAssembly options (scriptFilePath |> Queil.FSharp.FscHost.File) |> Async.RunSynchronously

let runtimeconfig = 
    JsonSerializer.Serialize {|
        runtimeOptions = {|
            tfm = "net7.0"
            framework = {|
                name = "Microsoft.NETCore.App"
                version = "7.0.0"
            |}
        |}
    |}

System.IO.File.WriteAllText(
    $"""{Path.ChangeExtension(output.AssemblyFilePath, ".runtimeconfig.json")}""",
    runtimeconfig)

if cmd.Contains Run then
  output.Assembly.EntryPoint.Invoke(null, Array.empty) |> ignore