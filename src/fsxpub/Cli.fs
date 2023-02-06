namespace FsxPub.Cli

open Argu


type Args =
  | [<MainCommand>][<Last>][<ExactlyOnceAttribute>] ScriptFilePath of string
  | [<AltCommandLine("-r")>] Run
  | [<AltCommandLine("-o")>] Output_Dir of string
  | [<AltCommandLine("-c")>] Clean
  | [<AltCommandLine("-v")>] Verbose
 with
    interface IArgParserTemplate with
     member this.Usage =
       match this with
       | ScriptFilePath _ -> "Fsx script file path."
       | Run _ -> "If set the compiled script will be executed."
       | Output_Dir _ -> "Sets the output directory. Default: ./out"
       | Clean -> "If set the output directory gets cleared before"
       | Verbose -> "Shows some log messages"
    static member FromCmdLine() =
      let argv = System.Environment.GetCommandLineArgs()[1..]
      let parser = ArgumentParser.Create<Args>(programName = "fsxpub")
      parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
