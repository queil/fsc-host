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

          test "Should support Paket nuget with cache" {
              let script =
                  """
                #r "paket: nuget Yzl"

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
          }

          test "Should crash" {
              let script =
                  """
                      #r "paket: 
                            nuget Fake.Core.Trace = 6.1.0"
                      
                      namespace Script
      
                      module X =
      
                          let x () = 10 |> printfn "%i"
                    """

              let tmpDir = ensureTempPath ()

              let script2 =
                  """
                     #r "nuget: Fake.Core.Environment, 6.0.0"
                          """

              let scriptPath1 = $"{tmpDir}/script.fsx"
              let scriptPath2 = $"{tmpDir}/script2.fsx"
              File.WriteAllText(scriptPath1, script)
              File.WriteAllText(scriptPath2, script2)

              let main = $"""
                #load "{scriptPath1}"
                #load "{scriptPath2}"
              """


              let resultFunc =
                  Common.invoke
                  <| fun () ->
                      Inline main
                      |> CompilerHost.getMember
                          { options with UseCache = false }
                          (Member<unit -> unit>.Path("Script.X.x"))
                      |> Async.RunSynchronously

              resultFunc ()
          } ]
