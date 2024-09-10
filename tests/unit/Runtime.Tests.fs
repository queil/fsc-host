module Queil.FSharp.FscHost.Runtime.Tests

open Expecto
open Queil.FSharp.FscHost

[<Tests>]
let tests =
    testList
        "Runtime"
        [ test "Should be able to load System.Security.Cryptography.ProtectedData" {
              let script =
                  """
                    #r "paket: nuget System.Security.Cryptography.ProtectedData >= 8.0.0"
                    """

              Common.invoke
              <| fun () ->
                  Inline script
                  |> CompilerHost.getAssembly Common.options
                  |> Async.RunSynchronously
                  |> _.Assembly.Value
                  |> ignore

          } ]
