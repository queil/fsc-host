module Queil.FSharp.FscHost.Paket.Tests

open Expecto
open Queil.FSharp.FscHost
open Queil.FSharp.FscHost.Common
open System.IO

[<Tests>]
let paketTests =

    let options = Common.options

    testList
        "Paket"
        [

          let asScript filePath lines =
              Directory.CreateDirectory(FileInfo(filePath).DirectoryName) |> ignore
              File.WriteAllLines(filePath, lines |> Seq.toArray)

          let prepareScripts () =
              let tmpPath =
                  Path.Combine(Path.GetTempPath(), "fsch-paket-tests", Path.GetRandomFileName())

              Directory.CreateDirectory tmpPath |> ignore
              let scriptDir = tmpPath
              let rootScriptName = "script.fsx"
              let rootScriptPath = Path.Combine(scriptDir, rootScriptName)
              let fileA = Path.Combine(scriptDir, "depa.fsx")
              let fileB = Path.Combine(scriptDir, "depb", "depb.fsx")

              [ $""" #r  "paket: nuget Yzl >= 1.0.0" ;#load "%s{fileB.Replace(@"\", @"\\")}" """; """let valueA = 11""" ]
              |> asScript fileA

              [ """let valueB = 13""" ] |> asScript fileB

              [ $""" #r  "paket: nuget Yzl >= 2.0.0" ; #load "%s{fileA.Replace(@"\", @"\\")}" """
                """let plugin = Some (Depa.valueA * Depb.valueB) """ ]
              |> asScript rootScriptPath

              scriptDir, rootScriptName, fileA, fileB

          testAsync "Smoke test (ID: 783)" {
              let scriptDir, rootScriptName, _, _ = prepareScripts ()

              let! _ = Queil.FSharp.FscHost.File (Path.Combine(scriptDir, rootScriptName))
                       |> CompilerHost.getAssembly { options with UseCache = false }
              ()
          }
        
          test "Should support Paket nuget with cache" {
              let script =
                  """
                #r "paket: nuget Yzl"

                namespace Script

                module X =

                    open Yzl

                    let x () = 10 |> Yzl.render |> printfn "%s"
                """

              let dir = "/tmp/.fsch/fixed-location"

              let scriptFilePath = $"{dir}/paket.nuget.cache.fsx"
              Directory.CreateDirectory(dir) |> ignore
              File.WriteAllText(scriptFilePath, script)

              let resultFunc =
                  Common.invoke
                  <| fun () ->
                      Queil.FSharp.FscHost.File scriptFilePath
                      |> CompilerHost.getMember
                          { options with UseCache = true }
                          (Member<unit -> unit>.Path("Script.X.x"))
                      |> Async.RunSynchronously

              resultFunc ()
          }

          test "Should support Paket GitHub" {
              let script =
                  """
                #r "paket: github queil/yzl src/Yzl/Yzl.fs"
                #load "queil/yzl/src/Yzl/Yzl.fs"
                namespace Script

                module X =

                    open Yzl

                    let x () = 10 |> Yzl.render |> printfn "%s"
                """

              let resultFunc =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember
                          { options with UseCache = false }
                          (Member<unit -> unit>.Path("Script.X.x"))
                      |> Async.RunSynchronously

              resultFunc ()
          } ]
