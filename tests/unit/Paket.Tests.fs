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
