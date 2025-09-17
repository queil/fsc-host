module Queil.FSharp.FscHost.Cache.Tests

open System.Diagnostics
open Expecto
open Queil.FSharp.FscHost.Plugin
open Queil.FSharp.FscHost.Common
open System.IO
open Queil.FSharp.Hashing

[<Tests>]
let cacheTests =
    testList
        "Caching"
        [ let asScript filePath lines =
              File.WriteAllLines(filePath, lines |> Seq.toArray)

          let prepareScripts () =
              let tmpPath = ensureTempPath ()
              let scriptDir = tmpPath
              let rootScriptName = "script.fsx"
              let rootScriptPath = Path.Combine(scriptDir, rootScriptName)
              let fileA = Path.Combine(scriptDir, "depa.fsx")
              let fileB = Path.Combine(scriptDir, "depb.fsx")

              [ $"""#load "%s{fileB.Replace(@"\", @"\\")}" """; """let valueA = 11""" ]
              |> asScript fileA

              [ """let valueB = 13""" ] |> asScript fileB

              [ $"""#load "%s{fileA.Replace(@"\", @"\\")}" """
                """let plugin = Some (Depa.valueA * Depb.valueB) """ ]
              |> asScript rootScriptPath

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
              let timingCold = sw.Elapsed
              printfn $"%A{timingCold}"
              sw.Restart()

              let! _ = plugin ()
              let timingFromCache = sw.Elapsed
              printfn $"%A{timingFromCache}"

              let ratio = timingCold.Ticks / timingFromCache.Ticks
              let expectedRatioAtLeast = 5

              $"Should read assembly from cache. Timings: cold {timingCold}, cache {timingFromCache}, ratio {ratio} "
              |> Expect.isGreaterThan ratio expectedRatioAtLeast
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
              [ $"""#load "%s{fileB.Replace(@"\", @"\\")}" """; """let valueA = 17""" ]
              |> asScript fileA

              [ """let valueB = 19""" ] |> asScript fileB

              let! secondResult = plugin ()

              let result = "Some int expected" |> Expect.wantSome secondResult
              "Result should be '323'" |> Expect.equal result 323
          }

          testAsync "Override output dir path" {
              let tmpPath = ensureTempPath ()

              let content = "let plugin = Some 10"
              let shallowHash = content |> Hash.sha256 |> Hash.short
              let scriptDir = Path.Combine(tmpPath, shallowHash)
              let filePath = Path.Combine(scriptDir, "inline.fsx")
              let hashes = (filePath, Some shallowHash) ||> Hash.fileHash

              let assemblyOutputPath = hashes.HashedScriptVersionDir tmpPath

              Directory.CreateDirectory assemblyOutputPath |> ignore

              let findDlls () =
                  Directory.EnumerateFiles assemblyOutputPath
                  |> Seq.tryFind ((<>) "18423585a9c.dll")

              findDlls () |> Flip.Expect.isNone "The cache dir should not contain dlls"

              let! _ =
                  plugin<int option> {
                      body content
                      cache true
                      output_dir tmpPath
                      log System.Console.WriteLine
                  }

              findDlls ()
              |> Flip.Expect.isSome $"The cache dir %s{assemblyOutputPath} should contain dlls"
          }

          testAsync "Shouldn't cache if caching not enabled" {
              let tmpPath = ensureTempPath ()

              let findDlls () =
                  Directory.EnumerateFiles tmpPath |> Seq.tryFind (fun f -> f.EndsWith(".dll"))

              findDlls ()
              |> Expecto.Flip.Expect.isNone "The cache dir should not contain dlls"

              let! _ =
                  plugin<int option> {
                      body "let plugin = Some 10"
                      output_dir tmpPath
                      log System.Console.WriteLine
                  }

              findDlls ()
              |> Expecto.Flip.Expect.isNone "The cache dir should not contain dlls"
          } ]
