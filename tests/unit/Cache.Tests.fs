module Queil.FSharp.FscHost.Cache.Tests

open Expecto
open Queil.FSharp.FscHost.Plugin
open System.IO

[<Tests>]
let cacheTests =
  testList "Cache invalidation" [
    let asScript filePath lines = File.WriteAllLines(filePath, lines |> Seq.toArray)
    
    testAsync "File script" {
      let tmpPath = Path.Combine(Path.GetTempPath(), "fsc-host", Path.GetRandomFileName())
      Directory.CreateDirectory tmpPath |> ignore
      let scriptDir = tmpPath
      let rootScriptName = "script.fsx"
      let rootScriptPath = Path.Combine(scriptDir, rootScriptName)
      let fileA = Path.Combine(scriptDir, "depa.fsx")
      let fileB = Path.Combine(scriptDir, "depb.fsx")

      [
        $"""#load "%s{fileB.Replace(@"\", @"\\")}" """
        """let valueA = 11"""
      ] |> asScript fileA
      
      [
        """let valueB = 13"""
      ] |> asScript fileB
      
      [
        $"""#load "%s{fileA.Replace(@"\", @"\\")}" """
        """let plugin = Some (Depa.valueA * Depb.valueB) """
      ] |> asScript rootScriptPath

      let plugin () = 
        plugin<int option> {
          load
          dir scriptDir
          file rootScriptName
          cache true
          compiler (fun x -> { x with LangVersion = Some "preview" } )
        }
      
      let! firstResult = plugin ()  
      
      let result = "Some int expected" |> Expect.wantSome firstResult
      "Result should be '143'" |> Expect.equal result 143
      
      // editing non-root files should also dump the cache
      // and re-compiling should return the updated result
      [
        $"""#load "%s{fileB}" """
        """let valueA = 17"""
      ] |> asScript fileA
      
      [
        """let valueB = 19"""
      ] |> asScript fileB
      
      let! secondResult = plugin ()  
      
      let result = "Some int expected" |> Expect.wantSome secondResult
      "Result should be '323'" |> Expect.equal result 323
      
    }
  ]
