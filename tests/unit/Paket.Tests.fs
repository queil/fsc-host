module Queil.FSharp.FscHost.Paket.Tests

open Expecto
open Queil.FSharp.FscHost

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


          ]
