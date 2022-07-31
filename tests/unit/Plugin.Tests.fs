module Queil.FSharp.FscHost.Plugin.Tests

open Expecto
open Queil.FSharp.FscHost.Plugin
open System

[<Tests>]
let ceTests =
  testList "Plugin builder" [
    testAsync "Inline script" {
      let! plugin =
        plugin<string option> {
          script """let plugin = Some "test971" """
        }
      let result = "Some string expected" |> Expect.wantSome plugin
      "String should be 'test971'" |> Expect.equal result "test971"
    }
    
    testAsync "File script" {
      let fileName = "/tmp/plugin.builder.file.fsx"
      let lines = ["""let plugin = Some "test971" """]
      IO.File.WriteAllLines(fileName, lines)
      
      let! plugin =
        plugin<string option> {
          file fileName
        }
      let result = "Some string expected" |> Expect.wantSome plugin
      "String should be 'test971'" |> Expect.equal result "test971"
    }
    
  ] |> testLabel "plugin"
  

