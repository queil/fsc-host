module Queil.FSharp.FscHost.Tests

open Expecto
open Queil.FSharp.FscHost
open Queil.FSharp.FscHost.Types

[<Tests>]
let tests =
  testList "samples" [

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
  ]
