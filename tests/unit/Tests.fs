module Queil.FSharp.FscHost.Tests

open Expecto
open Queil.FSharp.FscHost

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
        {Script = OfString script; MemberFqName= "Test.Script.Countries.myList"} 
          |> CompilerHost.getScriptMember<string list> false |> Async.RunSynchronously

      "Lists should be equal" |> Expect.equal result (Ok ["UK"; "Poland"; "France"])
    }

    test "Should be able to invoke function value" {
      let script = """
module Test.Script

let myFuncOrig (name:string) = sprintf "Hello %s!" name
let myFunc = myFuncOrig
"""
      let result =
        {Script = OfString script; MemberFqName = "Test.Script.myFunc"} 
          |> CompilerHost.getScriptMember<string -> string> false |> Async.RunSynchronously
      
      let callResult =
        match result with
        | Ok myFunc -> myFunc "TEST 109384"
        | Error x -> failwithf "%A" x 

      "Unexpected call result" |> Expect.equal callResult "Hello TEST 109384!"
    }

    test "Should show the actual property type if it's invalid" {
      let script = """
module Test.Script

let myFuncOrig (name:string) = sprintf "Hello %s!" name
let myFunc = myFuncOrig
"""
      let result =
        {Script = OfString script; MemberFqName = "Test.Script.myFunc"} 
          |> CompilerHost.getScriptMember<string -> int> false |> Async.RunSynchronously
      
      let error = "Expected ScriptsPropertyHasInvalidType error" |> Expect.wantError result
      
      match error with
      | ScriptsPropertyHasInvalidType (_, _, typ) -> 
        "Unexpected type" |> Expect.equal typ typeof<string -> string>
      | _ -> failwithf "Unexpected error type"

    }
  ]
