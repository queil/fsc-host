module Queil.FSharp.FscHost.Tests

open Expecto
open Queil.FSharp.FscHost

let invoke<'a> (func:unit -> 'a) =
  try
    func ()
  with 
  | ScriptCompileError errors -> 
    failwithf "%s" (errors |> String.concat "\n")

[<Tests>]
let tests =
  testList "Tests" [

    test "Should be able to extract a list" {
      let script = """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
"""
      let result = invoke <| fun () ->
        Inline script 
          |> CompilerHost.getScriptProperty (Property<string list>.Path "Test.Script.Countries.myList") Options.Default |> Async.RunSynchronously
      
      "Lists should be equal" |> Expect.equal result ["UK"; "Poland"; "France"]
    }

    test "Should be able to invoke function value" {
      let script = """
module Test.Script

let myFuncOrig (name:string) = sprintf "Hello %s!" name
let myFunc = myFuncOrig
"""
      let myFunc = invoke <| fun () ->
        Inline script
          |> CompilerHost.getScriptProperty (Property<string ->string>.Path "Test.Script.myFunc") Options.Default |> Async.RunSynchronously
      
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
              invoke <| fun () ->
                Inline script
                |> CompilerHost.getScriptProperty (Property<string -> int>.Path "Test.Script.myFunc" ) Options.Default
                |> Async.RunSynchronously
                |> ignore)
                
                (fun exn ->
                  match exn with
                  | ScriptsPropertyHasInvalidType(_, typ) -> 
                    "Expected type is not right" |> Expect.equal typ typeof<string -> string>
                  |_ -> failtest "Should throw ScriptsPropertyHasInvalidType") |> ignore
    }

    test "Should be able to extract 2 porperties" {
      let script = """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
  let myCount = myList |> List.length
"""
      let result = invoke <| fun () ->
        Inline script |>
          CompilerHost.getScriptProperties2
            (Property<string list>.Path "Test.Script.Countries.myList")
            (Property<int>.Path "Test.Script.Countries.myCount")
            
            Options.Default |> Async.RunSynchronously

      "Lists should be equal" |> Expect.equal result (["UK"; "Poland"; "France"], 3)
    }

    test "Should be able to extract 3 properties" {
      let script = """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
  let myCount = myList |> List.length
  let myFloat = 44.44
  
"""
      let result = invoke <| fun () ->
        Inline script |>
          CompilerHost.getScriptProperties3
            (Property<string list>.Path "Test.Script.Countries.myList")
            (Property<int>.Path "Test.Script.Countries.myCount")
            (Property<float>.Path "Test.Script.Countries.myFloat")
            
            Options.Default |> Async.RunSynchronously

      "Lists should be equal" |> Expect.equal result (["UK"; "Poland"; "France"], 3, 44.44 )
    }

    test "Should be able to extract 4 properties" {
      let script = """
namespace Test.Script

module Countries =
  let myList = ["UK"; "Poland"; "France"]
  let myCount = myList |> List.length
  let myFloat = 44.44
  let myMap = [("s", 1)] |> Map.ofList
  
"""
      let result = invoke <| fun () ->
        Inline script |>
          CompilerHost.getScriptProperties4
            (Property<string list>.Path "Test.Script.Countries.myList")
            (Property<int>.Path "Test.Script.Countries.myCount")
            (Property<float>.Path "Test.Script.Countries.myFloat")
            (Property<Map<string,int>>.Path "Test.Script.Countries.myMap")
            
            Options.Default |> Async.RunSynchronously

      "Lists should be equal" |> Expect.equal result (["UK"; "Poland"; "France"], 3, 44.44,  [("s", 1)] |> Map.ofList )
    }
  ]
