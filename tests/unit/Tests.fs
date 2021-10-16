module Queil.FSharp.FscHost.Tests

open Expecto
open Queil.FSharp.FscHost

let options = 
  { Options.Default with
      UseCache = true
      Logger = printfn "%s"
  }

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
          |> CompilerHost.getScriptProperty options (Property<string list>.Path "Test.Script.Countries.myList") |> Async.RunSynchronously
      
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
          |> CompilerHost.getScriptProperty options (Property<string ->string>.Path "Test.Script.myFunc") |> Async.RunSynchronously
      
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
                |> CompilerHost.getScriptProperty options (Property<string -> int>.Path "Test.Script.myFunc" )
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
          CompilerHost.getScriptProperties2 options
            (Property<string list>.Path "Test.Script.Countries.myList")
            (Property<int>.Path "Test.Script.Countries.myCount")
            
             |> Async.RunSynchronously

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
          CompilerHost.getScriptProperties3 options
            (Property<string list>.Path "Test.Script.Countries.myList")
            (Property<int>.Path "Test.Script.Countries.myCount")
            (Property<float>.Path "Test.Script.Countries.myFloat")
            
             |> Async.RunSynchronously

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
          CompilerHost.getScriptProperties4 options
            (Property<string list>.Path "Test.Script.Countries.myList")
            (Property<int>.Path "Test.Script.Countries.myCount")
            (Property<float>.Path "Test.Script.Countries.myFloat")
            (Property<Map<string,int>>.Path "Test.Script.Countries.myMap")
            
             |> Async.RunSynchronously

      "Lists should be equal" |> Expect.equal result (["UK"; "Poland"; "France"], 3, 44.44,  [("s", 1)] |> Map.ofList )
    }

    test "Should not fail on warnings" {
      let script =
        """System.DateTime.Now.ToString() |> printfn "%s"
"""
      invoke <| fun () ->
        Inline script |>
          CompilerHost.getAssembly options |> Async.RunSynchronously |> ignore
    }

    test "Should fail on errors" {
      let script =
        """let 9999
"""
      "Should throw compilation error" |> Expect.throws (fun () ->
        invoke <| fun () ->
          Inline script |>
            CompilerHost.getAssembly options |> Async.RunSynchronously |> ignore)
    }

    test "Should load assembly" {
      let script = """
module Test.Script

#r "nuget: JsonPatch.Net, 1.1.0"

let myFunc () = Json.Pointer.JsonPointer.Parse("/some").ToString()
let export = myFunc
"""
      invoke <| fun () ->
        let myFunc =
          Inline script |>
            CompilerHost.getAssembly options |> Async.RunSynchronously |> Property.get<unit -> string> "Test.Script.export"
        "Value should match" |> Expect.equal (myFunc ()) "/some"
    }

    test "Should pass defined symbols" {
      let script = """module Test.Script
#if MY_SYMBOL
let export = "WORKED"
#else
let export = "NOT_WORKED"
#endif
      """
      let opts = { options with Compiler = { options.Compiler with Symbols = ["MY_SYMBOL"]}}
      invoke <| fun () ->
        let value =
          Inline script |>
            CompilerHost.getAssembly opts |> Async.RunSynchronously |> Property.get<string> "Test.Script.export"
        "Value should match" |> Expect.equal value "WORKED"
    }
  ]
