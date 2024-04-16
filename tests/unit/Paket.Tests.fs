module Queil.FSharp.FscHost.Paket.Tests

open System.IO
open Expecto
open Queil.FSharp.FscHost

[<Tests>]
let paketTests =

    let options = Common.options

    testList
        "Paket"
        [

          test "Should support Paket with cache" {
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


          ]
