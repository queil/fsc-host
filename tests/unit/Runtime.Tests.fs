module Queil.FSharp.FscHost.Runtime.Tests

open Expecto
open Queil.FSharp.FscHost

[<Tests>]
let tests =
    testList
        "Runtime"
        [ testAsync "Should be able to load System.Security.Cryptography.ProtectedData" {
              let script =
                  """
                    #r "paket: nuget System.Security.Cryptography.ProtectedData >= 8.0.0"
                    """

              let! _ =
                  Common.invoke
                  <| fun () ->
                      async {
                          let! r = Inline script |> CompilerHost.getAssembly Common.options

                          r.Assembly.Value |> ignore
                          return ()
                      }

              return ()

          } ]
