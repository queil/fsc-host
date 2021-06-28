module Queil.FSharp.FscHost.Tests

open Expecto
open Queil.FSharp.FscHost
open System.Collections.Generic

[<Tests>]
let tests =
  testList "Tests" [

    test "Should be able to extract a list" {
      let script = """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
"""
      let result =
        Inline script 
          |> CompilerHost.getScriptMember (Member<string list>.Path "Test.Script.Countries.myList") ScriptExtractOptions.Default |> Async.RunSynchronously

      "Lists should be equal" |> Expect.equal result ["UK"; "Poland"; "France"]
    }

    test "Should be able to invoke function value" {
      let script = """
module Test.Script

let myFuncOrig (name:string) = sprintf "Hello %s!" name
let myFunc = myFuncOrig
"""
      let myFunc =
        Inline script
          |> CompilerHost.getScriptMember (Member<string ->string>.Path "Test.Script.myFunc") ScriptExtractOptions.Default |> Async.RunSynchronously
      
      let callResult = myFunc "TEST 109384"


      "Unexpected call result" |> Expect.equal callResult "Hello TEST 109384!"
    }

    test "Should show the actual property type if it's invalid" {
      let script = """
module Test.Script

let myFuncOrig (name:string) = sprintf "Hello %s!" name
let myFunc = myFuncOrig
"""
      
      Expect.throwsC (fun () ->
                Inline script
                |> CompilerHost.getScriptMember (Member<string -> int>.Path "Test.Script.myFunc" ) ScriptExtractOptions.Default
                |> Async.RunSynchronously
                |> ignore)
                
                (fun exn ->
                  match exn with
                  | ScriptsPropertyHasInvalidType(_, typ) -> 
                    "Expected type is not right" |> Expect.equal typ typeof<string -> string>
                  |_ -> failtest "Should throw ScriptsPropertyHasInvalidType") |> ignore
    }

    test "Should be able to extract 2 members" {
      let script = """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
  let myCount = myList |> List.length
"""
      let result =
        Inline script |>
          CompilerHost.getScriptMembers2
            (Member<string list>.Path "Test.Script.Countries.myList")
            (Member<int>.Path "Test.Script.Countries.myCount")
            
            ScriptExtractOptions.Default |> Async.RunSynchronously

      "Lists should be equal" |> Expect.equal result (["UK"; "Poland"; "France"], 3)
    }

    test "Should be able to extract 3 members" {
      let script = """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
  let myCount = myList |> List.length
  let myFloat = 44.44
  
"""
      let result =
        Inline script |>
          CompilerHost.getScriptMembers3
            (Member<string list>.Path "Test.Script.Countries.myList")
            (Member<int>.Path "Test.Script.Countries.myCount")
            (Member<float>.Path "Test.Script.Countries.myFloat")
            
            ScriptExtractOptions.Default |> Async.RunSynchronously

      "Lists should be equal" |> Expect.equal result (["UK"; "Poland"; "France"], 3, 44.44 )
    }

    test "Should be able to extract 4 members" {
      let script = """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
  let myCount = myList |> List.length
  let myFloat = 44.44
  let myMap = [("s", 1)] |> Map.ofList
  
"""
      let result =
        Inline script |>
          CompilerHost.getScriptMembers4
            (Member<string list>.Path "Test.Script.Countries.myList")
            (Member<int>.Path "Test.Script.Countries.myCount")
            (Member<float>.Path "Test.Script.Countries.myFloat")
            (Member<Map<string,int>>.Path "Test.Script.Countries.myMap")
            
            ScriptExtractOptions.Default |> Async.RunSynchronously

      "Lists should be equal" |> Expect.equal result (["UK"; "Poland"; "France"], 3, 44.44,  [("s", 1)] |> Map.ofList )
    }
  ]
