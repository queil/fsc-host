module Microsoft.FSharp.FscHost.Plugin.Tests

open System.IO
open Expecto
open Microsoft.FSharp.FscHost.Plugin
open System

[<Tests>]
let ceTests =

    let ensureTempPath () =
        let tmpPath = Path.Combine(Path.GetTempPath(), "fsc-host", Path.GetRandomFileName())
        Directory.CreateDirectory tmpPath |> ignore
        tmpPath

    testList
        "Plugin builder"
        [

          testAsync "Inline script" {
              let! plugin =
                  plugin<string option> {
                      body """let plugin = Some "test971" """
                      log (fun s -> printfn $"%s{s}")
                  }

              let result = "Some string expected" |> Expect.wantSome plugin
              "String should be 'test971'" |> Expect.equal result "test971"
          }

          testAsync "Inline script - w/cache" {
              let! plugin =
                  plugin<string option> {
                      body """let plugin = Some "test971" """
                      cache true
                      log (fun s -> printfn $"%s{s}")
                  }

              let result = "Some string expected" |> Expect.wantSome plugin
              "String should be 'test971'" |> Expect.equal result "test971"
          }

          testAsync "Inline script - nested duplicate properties" {
              let! plugin =
                  plugin<string option> {
                      body
                          """
let plugin = Some "test971"

module OtherPlugin =
  let plugin = Some "test974"
"""

                      cache true
                      log (fun s -> printfn $"%s{s}")
                  }

              let result = "Some string expected" |> Expect.wantSome plugin
              "String should be 'test971'" |> Expect.equal result "test971"
          }

          testAsync "File script" {
              let tmpDir = ensureTempPath ()
              let fileName = Path.Combine(tmpDir, "plugin.builder.file.fsx")
              let lines = [ """let plugin = Some "test971" """ ]
              File.WriteAllLines(fileName, lines)

              let! plugin =
                  plugin<string option> {
                      load
                      dir tmpDir
                      file "plugin.builder.file.fsx"
                      cache true
                      compiler (fun x -> { x with LangVersion = Some "preview" })
                  }

              let result = "Some string expected" |> Expect.wantSome plugin
              "String should be 'test971'" |> Expect.equal result "test971"
          }

          testAsync "File script - w/binding" {
              let tmpDir = ensureTempPath ()
              let fileName = Path.Combine(tmpDir, "plugin.builder.binding.file.fsx")
              let lines = [ """let export = Some "test971" """ ]
              File.WriteAllLines(fileName, lines)

              let! plugin =
                  plugin<string option> {
                      load
                      dir tmpDir
                      file "plugin.builder.binding.file.fsx"
                      binding "export"
                      cache true
                      compiler (fun x -> { x with LangVersion = Some "preview" })
                  }

              let result = "Some string expected" |> Expect.wantSome plugin
              "String should be 'test971'" |> Expect.equal result "test971"
          }

          ]
    |> testLabel "plugin"
