module Queil.FSharp.FscHost.Cache.Tests

open System.Diagnostics
open Expecto
open Queil.FSharp.FscHost.Plugin
open System.IO

[<Tests>]
let cacheTests =
    testList
        "Caching"
        [ let asScript filePath lines =
              File.WriteAllLines(filePath, lines |> Seq.toArray)

          let prepareScripts () =
              let tmpPath = Path.Combine(Path.GetTempPath(), "fsc-host", Path.GetRandomFileName())
              Directory.CreateDirectory tmpPath |> ignore
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
              let tmpPath = Path.Combine(Path.GetTempPath(), ".fsch-override", Path.GetRandomFileName())
              Directory.CreateDirectory tmpPath |> ignore

              let findDlls () =
                  Directory.EnumerateDirectories tmpPath
                  |> Seq.tryHead
                  |> Option.bind (Directory.EnumerateFiles >> Seq.tryFind (fun f -> f.EndsWith(".dll")))

              findDlls ()
              |> Expecto.Flip.Expect.isNone "The cache dir should not contain dlls"

              let! _ =
                  plugin<int option> {
                      body "let plugin = Some 10"
                      cache true
                      cache_dir tmpPath
                      log System.Console.WriteLine
                  }

              findDlls ()
              |> Expecto.Flip.Expect.isSome $"The cache dir %s{tmpPath} should contain dlls"
          }

          testAsync "Shouldn't cache if caching not enabled" {
              let tmpPath = Path.Combine(Path.GetTempPath(), ".fsch-override", Path.GetRandomFileName())
              Directory.CreateDirectory tmpPath |> ignore

              let findDlls () =
                  Directory.EnumerateFiles tmpPath |> Seq.tryFind (fun f -> f.EndsWith(".dll"))

              findDlls ()
              |> Expecto.Flip.Expect.isNone "The cache dir should not contain dlls"

              let! _ =
                  plugin<int option> {
                      body "let plugin = Some 10"
                      cache_dir tmpPath
                      log System.Console.WriteLine
                  }

              findDlls ()
              |> Expecto.Flip.Expect.isNone "The cache dir should not contain dlls"
          } ]
