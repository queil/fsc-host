module Queil.FSharp.FscHost.Cache.Tests

open System.Diagnostics
open Expecto
open Queil.FSharp.FscHost.Plugin
open System.IO

[<Tests>]
let cacheTests =
  testList "Cache invalidation" [
    let asScript filePath lines = File.WriteAllLines(filePath, lines |> Seq.toArray)
    
    let prepareScripts () =
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
      scriptDir, rootScriptName, fileA, fileB
    
    testAsync "Should reuse cached assembly" {
      let scriptDir, rootScriptName, _, _ = prepareScripts ()
      let plugin () = 
        plugin<int option> {
          load
          dir scriptDir
          file rootScriptName
          cache true
          log System.Console.WriteLine
        }
      
      let sw = Stopwatch.StartNew()
      let! _ = plugin ()
      printfn $"%A{sw.Elapsed}"
      sw.Restart()

      let! _ = plugin ()
      let timingFromCache = sw.Elapsed
      printfn $"%A{timingFromCache}"
      "Should read assembly from cache" |> Expect.isLessThan timingFromCache.Milliseconds 100
      
    }
    
    testAsync "Should invalidate compiled assembly if leaf scripts change" {
    
      let scriptDir, rootScriptName, fileA, fileB = prepareScripts ()
      let plugin () = 
        plugin<int option> {
          load
          dir scriptDir
          file rootScriptName
          cache true
        }
      
      let! firstResult = plugin ()
      
      let result = "Some int expected" |> Expect.wantSome firstResult
      "Result should be '143'" |> Expect.equal result 143
      
      // editing non-root files should also dump the cache
      // and re-compiling should return the updated result
      [
        $"""#load "%s{fileB.Replace(@"\", @"\\")}" """
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
