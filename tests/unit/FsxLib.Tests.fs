module Queil.FSharp.FscHost.FsxLib.Tests

open Expecto
open Queil.FSharp.FscHost
open System.IO

[<Tests>]
let FsxLibTests =

    let options = Common.options

    testList
        "Fsxlib"
        [

          let asScript filePath lines =
              Directory.CreateDirectory(FileInfo(filePath).DirectoryName) |> ignore
              File.WriteAllLines(filePath, lines |> Seq.toArray)

          let prepareScripts () =
              let tmpPath = Path.Combine(Path.GetTempPath(), "fsch-fsxlib-tests", "static")

              let tmpPath2 = Path.Combine(Path.GetTempPath(), "fsch-fsxlib-tests", "static2")

              Directory.CreateDirectory tmpPath |> ignore
              let scriptDir = tmpPath
              let rootScriptName = "script.fsx"
              let rootScriptPath = Path.Combine(scriptDir, rootScriptName)
              let fileA = Path.Combine(scriptDir, "depa.fsx")
              let fileB = Path.Combine(scriptDir, "depb", "depb.fsx")
              let fileC = Path.Combine(tmpPath2, "depc.fsx")

              [ $""" #r  "paket: nuget Yzl >= 1.0.0"  """; """let valueA = 11""" ]
              |> asScript fileA

              [ "let a = 0" ] |> asScript fileC

              [ $""" #load "%s{fileC.Replace(@"\", @"\\")}"; let valueB = 13""" ]
              |> asScript fileB

              [ $""" #r  "paket: nuget Yzl >= 2.0.0" ;#r  "paket: nuget Arquidev.Fetch >= 1.0.0"; #r "fsxlib: %s{fileA.Replace(@"\", @"\\")}"; #r "fsxlib: %s{fileB.Replace(@"\", @"\\")}" """
                """let plugin = Some (Depa.valueA * Depb.valueB) """ ]
              |> asScript rootScriptPath

              scriptDir, rootScriptName, fileA, fileB

          testAsync "Smoke test (ID: 801)" {
              let scriptDir, rootScriptName, _, _ = prepareScripts ()

              let! c =
                  Queil.FSharp.FscHost.File(Path.Combine(scriptDir, rootScriptName))
                  |> CompilerHost.getAssembly
                      { options with
                          UseCache = false
                          AutoLoadNugetReferences = true
                          Verbose = true }

              c.Assembly.Value |> ignore

              ()
          }



          ]
