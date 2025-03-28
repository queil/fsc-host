module Queil.FSharp.FscHost.Core.Tests

open Expecto
open Queil.FSharp.FscHost
open Queil.FSharp.FscHost.Common
open System.IO

[<Tests>]
let tests =
    testList
        "Core"
        [ test "Should be able to extract a list" {
              let script =
                  """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
"""

              let result =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember Common.options (Member<string list>.Path "Test.Script.Countries.myList")
                      |> Async.RunSynchronously

              "Lists should be equal" |> Expect.equal result [ "UK"; "Poland"; "France" ]
          }

          test "Should be able to invoke function value" {
              let script =
                  """
module Test.Script

let myFuncOrig (name:string) = sprintf "Hello %s!" name
let myFunc = myFuncOrig
"""

              let myFunc =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember Common.options (Member<string -> string>.Path "Test.Script.myFunc")
                      |> Async.RunSynchronously

              let callResult = myFunc "TEST 109384"

              "Unexpected call result" |> Expect.equal callResult "Hello TEST 109384!"
          }

          test "Should show the actual property type if it's invalid" {
              let script =
                  """
module Test.Script

let myFuncOrig (name:string) = sprintf "Hello %s!" name
let myFunc = myFuncOrig
"""

              Expect.throwsC
                  (fun () ->
                      Common.invoke
                      <| fun () ->
                          Inline script
                          |> CompilerHost.getMember Common.options (Member<string -> int>.Path "Test.Script.myFunc")
                          |> Async.RunSynchronously
                          |> ignore)

                  (fun exn ->
                      match exn with
                      | ScriptMemberHasInvalidType(_, typ) ->
                          "Expected type is not right"
                          |> Expect.equal typ (typeof<string -> string>.ToString())
                      | _ -> failtest "Should throw ScriptsPropertyHasInvalidType")
              |> ignore
          }

          test "Should be able to extract 2 porperties" {
              let script =
                  """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
  let myCount = myList |> List.length
"""

              let result =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember2
                          Common.options
                          (Member<string list>.Path "Test.Script.Countries.myList")
                          (Member<int>.Path "Test.Script.Countries.myCount")

                      |> Async.RunSynchronously

              "Lists should be equal" |> Expect.equal result ([ "UK"; "Poland"; "France" ], 3)
          }

          test "Should be able to extract 3 properties" {
              let script =
                  """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
  let myCount = myList |> List.length
  let myFloat = 44.44
  
"""

              let result =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember3
                          Common.options
                          (Member<string list>.Path "Test.Script.Countries.myList")
                          (Member<int>.Path "Test.Script.Countries.myCount")
                          (Member<float>.Path "Test.Script.Countries.myFloat")

                      |> Async.RunSynchronously

              "Lists should be equal"
              |> Expect.equal result ([ "UK"; "Poland"; "France" ], 3, 44.44)
          }

          test "Should be able to extract 4 properties" {
              let script =
                  """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
  let myCount = myList |> List.length
  let myFloat = 44.44
  let myMap = [("s", 1)] |> Map.ofList
  
"""

              let result =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember4
                          Common.options
                          (Member<string list>.Path "Test.Script.Countries.myList")
                          (Member<int>.Path "Test.Script.Countries.myCount")
                          (Member<float>.Path "Test.Script.Countries.myFloat")
                          (Member<Map<string, int>>.Path "Test.Script.Countries.myMap")

                      |> Async.RunSynchronously

              "Lists should be equal"
              |> Expect.equal result ([ "UK"; "Poland"; "France" ], 3, 44.44, [ ("s", 1) ] |> Map.ofList)
          }

          test "Should not fail on warnings" {
              let script =
                  """System.DateTime.Now.ToString() |> printfn "%s"
"""

              Common.invoke
              <| fun () ->
                  Inline script
                  |> CompilerHost.getAssembly Common.options
                  |> Async.RunSynchronously
                  |> ignore
          }

          test "Should fail on errors" {
              let script =
                  """let 9999
"""

              "Should throw compilation error"
              |> Expect.throws (fun () ->
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getAssembly Common.options
                      |> Async.RunSynchronously
                      |> ignore)
          }

          test "Should load assembly ID:2" {
              let script =
                  """
module Test.Script

#r "nuget: JsonPatch.Net, 1.1.0"

let myFunc () = Json.Pointer.JsonPointer.Parse("/some").ToString()
"""

              let myFunc =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |>

                      CompilerHost.getAssembly Common.options
                      |> Async.RunSynchronously
                      |> fun x -> x.Assembly.Value
                      |> Member.get<unit -> string> "Test.Script.myFunc"

              "Value should match" |> Expect.equal (myFunc ()) "/some"
          }

          test "Should correctly load dlls via r ID:94723" {

              let tmpPath = ensureTempPath ()

              System.IO.File.Copy(
                  $"{FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).DirectoryName}/Queil.FSharp.FscHost.dll",
                  $"{tmpPath}/Queil.FSharp.FscHost.dll"
              )

              let fullScriptPath = $"{tmpPath}/94723.fsx"

              let scriptBody =
                  """
module Test.Script

#r "./Queil.FSharp.FscHost.dll"

open Queil.FSharp.FscHost

let myFunc () = Inline "test" |> string
"""

              System.IO.File.WriteAllText(fullScriptPath, scriptBody)

              let myFunc =
                  Common.invoke
                  <| fun () ->
                      Queil.FSharp.FscHost.File fullScriptPath
                      |>

                      CompilerHost.getAssembly
                          { Common.options with
                              UseCache = false
                              Compiler =
                                  { Common.options.Compiler with
                                      IncludeHostEntryAssembly = false } }
                      |> Async.RunSynchronously
                      |> fun x -> x.Assembly.Value
                      |> Member.get<unit -> string> "Test.Script.myFunc"

              "Value should match" |> Expect.equal (myFunc ()) @"Inline ""test"""
          }

          test "Should pass defined symbols" {
              let script =
                  """module Test.Script
#if MY_SYMBOL
let export = "WORKED"
#else
let export = "NOT_WORKED"
#endif
      """

              let opts =
                  { Common.options with
                      Compiler =
                          { Common.options.Compiler with
                              Symbols = [ "MY_SYMBOL" ] } }

              Common.invoke
              <| fun () ->
                  let value =
                      Inline script
                      |> CompilerHost.getAssembly opts
                      |> Async.RunSynchronously
                      |> fun x -> x.Assembly.Value
                      |> Member.get<string> "Test.Script.export"

                  "Value should match" |> Expect.equal value "WORKED"
          }

          test "Should be able to Common.invoke func" {
              let script =
                  """
namespace Test.Script

module Func =
  let myFunc (tuple2:float * int) (text:string) (number: int) (test: unit option) = 
    
    let (t2f, t2i) = tuple2
    sprintf "tuple2: (%f, %i) - text: %s - number: %i - unit option: %A" t2f t2i text number test
  
  let sideEffect () =
    raise (System.Exception("THAT WORKED"))
    ()

"""

              let resultFunc, sideEffect =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember2
                          Common.options
                          (Member<float * int -> string -> int -> unit option -> string>.Path "Test.Script.Func.myFunc")
                          (Member<unit -> unit>.Path "Test.Script.Func.sideEffect")
                      |> Async.RunSynchronously

              let result = resultFunc (2.0, 8) "expected" 451 (Some())
              let msg = Expect.throwsC (fun () -> sideEffect ()) (fun exn -> exn.Message)
              "Unexpected exception message" |> Expect.equal msg "THAT WORKED"

              "Lists should be equal"
              |> Expect.equal result "tuple2: (2.000000, 8) - text: expected - number: 451 - unit option: Some ()"
          }

          test "Should handle two decomposed tuples in func" {
              let script =
                  """
namespace Test.Script

module Func =

  let myFunc (tuple1:float * int) (tuple2: ('a -> string) * (string -> 'a)) = 
    
    let (t1f, t1i) = tuple1
    let (t2s, t2s') = tuple2
    sprintf "tuple1: (%f, %i) - tuple2: (%s, %A)" t1f t1i (t2s ()) (t2s' "")

"""

              let resultFunc =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember
                          Common.options
                          (Member<float * int -> (_ -> string) * (string -> _) -> string>.Path "Test.Script.Func.myFunc")
                      |> Async.RunSynchronously
              //this tests fails when the unit in fun () -> "test" is replaced by fun x -> "test"
              let result = resultFunc (2.0, 8) ((fun () -> "test"), (fun _ -> ()))

              "Lists should be equal"
              |> Expect.equal result "tuple1: (2.000000, 8) - tuple2: (test, ())"
          }

          test "Should handle non-decomposed tuples in func" {
              let script =
                  """
namespace Test.Script

module Func =
  let myFunc something = sprintf "Generic: %A" something

"""

              let resultFunc =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember Common.options (Member<_ -> string>.Path "Test.Script.Func.myFunc")
                      |> Async.RunSynchronously

              let result = resultFunc (2.0, 8)

              "Values should be equal" |> Expect.equal result "Generic: (2.0, 8)"
          }

          test "Should handle fully generic method with tuples in func" {
              let script =
                  """
namespace Test.Script

module Func =
  let myFunc something toB : 'b = something |> toB

"""

              let resultFunc =
                  Common.invoke
                  <| fun () ->
                      Inline script
                      |> CompilerHost.getMember
                          Common.options
                          (Member<_ -> (_ -> _) -> _>.Path "Test.Script.Func.myFunc")
                      |> Async.RunSynchronously

              let result = resultFunc (2.0, 8) <| fun (a, b) -> $"Generic: (%f{a}, %i{b})"

              "Values should be equal" |> Expect.equal result "Generic: (2.000000, 8)"
          } ]
