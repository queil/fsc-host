module Queil.FSharp.FscHost.Core.Tests

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
  | ScriptMemberHasInvalidType (propertyName, actualTypeSignature) ->
    printfn "Diagnostics: Property '%s' should be of type '%s' but is '%s'" propertyName (typeof<'a>.ToString()) actualTypeSignature
    reraise ()

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
          |> CompilerHost.getMember options (Member<string list>.Path "Test.Script.Countries.myList") |> Async.RunSynchronously
      
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
          |> CompilerHost.getMember options (Member<string ->string>.Path "Test.Script.myFunc") |> Async.RunSynchronously
      
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
                |> CompilerHost.getMember options (Member<string -> int>.Path "Test.Script.myFunc" )
                |> Async.RunSynchronously
                |> ignore)
                
                (fun exn ->
                  match exn with
                  | ScriptMemberHasInvalidType(_, typ) -> 
                    "Expected type is not right" |> Expect.equal typ (typeof<string -> string>.ToString())
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
          CompilerHost.getMember2 options
            (Member<string list>.Path "Test.Script.Countries.myList")
            (Member<int>.Path "Test.Script.Countries.myCount")
            
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
          CompilerHost.getMember3 options
            (Member<string list>.Path "Test.Script.Countries.myList")
            (Member<int>.Path "Test.Script.Countries.myCount")
            (Member<float>.Path "Test.Script.Countries.myFloat")
            
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
          CompilerHost.getMember4 options
            (Member<string list>.Path "Test.Script.Countries.myList")
            (Member<int>.Path "Test.Script.Countries.myCount")
            (Member<float>.Path "Test.Script.Countries.myFloat")
            (Member<Map<string,int>>.Path "Test.Script.Countries.myMap")
            
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
"""
      let myFunc = invoke <| fun () ->
        Inline script |>
          CompilerHost.getAssembly options |> Async.RunSynchronously |> Member.get<unit -> string> "Test.Script.myFunc"
      
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
            CompilerHost.getAssembly opts |> Async.RunSynchronously |> Member.get<string> "Test.Script.export"
        "Value should match" |> Expect.equal value "WORKED"
    }
  
    test "Should be able to invoke func" {
      let script = """
namespace Test.Script

module Func =
  let myFunc (tuple2:float * int) (text:string) (number: int) (test: unit option) = 
    
    let (t2f, t2i) = tuple2
    sprintf "tuple2: (%f, %i) - text: %s - number: %i - unit option: %A" t2f t2i text number test
  
  let sideEffect () =
    raise (System.Exception("THAT WORKED"))
    ()

"""
      let (resultFunc, sideEffect) = invoke <| fun () ->
        Inline script |>
          CompilerHost.getMember2 options
            (Member<(float * int) -> string -> int -> unit option -> string>.Path "Test.Script.Func.myFunc")
            (Member<unit -> unit>.Path "Test.Script.Func.sideEffect")
             |> Async.RunSynchronously

      let result = resultFunc (2.0, 8) "expected" 451 (Some ())
      let msg = Expect.throwsC( fun () -> sideEffect ()) (fun exn -> exn.Message)
      "Unexpected exception message" |> Expect.equal msg "THAT WORKED"
      "Lists should be equal" |> Expect.equal result "tuple2: (2.000000, 8) - text: expected - number: 451 - unit option: Some ()"
    }

    test "Should handle two decomposed tuples in func" {
      let script = """
namespace Test.Script

module Func =

  let myFunc (tuple1:float * int) (tuple2: ('a -> string) * (string -> 'a)) = 
    
    let (t1f, t1i) = tuple1
    let (t2s, t2s') = tuple2
    sprintf "tuple1: (%f, %i) - tuple2: (%s, %A)" t1f t1i (t2s ()) (t2s' "")

"""
      let (resultFunc) = invoke <| fun () ->
        Inline script |>
          CompilerHost.getMember options
            (Member<(float * int) -> ((_ -> string) * (string -> _))-> string>.Path "Test.Script.Func.myFunc")
             |> Async.RunSynchronously
      //this tests fails when the unit in fun () -> "test" is replaced by fun x -> "test"
      let result = resultFunc (2.0, 8) ((fun () -> "test"), (fun _ -> ()))
      
      "Lists should be equal" |> Expect.equal result "tuple1: (2.000000, 8) - tuple2: (test, ())"
    }

    test "Should handle non-decomposed tuples in func" {
      let script = """
namespace Test.Script

module Func =
  let myFunc something = sprintf "Generic: %A" something

"""
      let (resultFunc) = invoke <| fun () ->
        Inline script |>
          CompilerHost.getMember options
            (Member<_ -> string>.Path "Test.Script.Func.myFunc")
             |> Async.RunSynchronously

      let result = resultFunc (2.0, 8)
      
      "Values should be equal" |> Expect.equal result "Generic: (2.0, 8)"
    }

    test "Should handle fully generic method with tuples in func" {
      let script = """
namespace Test.Script

module Func =
  let myFunc something toB : 'b = something |> toB

"""
      let (resultFunc) = invoke <| fun () ->
        Inline script |>
          CompilerHost.getMember options
            (Member<_ -> (_ -> _) -> _>.Path "Test.Script.Func.myFunc")
             |> Async.RunSynchronously

      let result = resultFunc (2.0, 8) <| fun (a, b) -> sprintf "Generic: (%f, %i)" a b
      
      "Values should be equal" |> Expect.equal result "Generic: (2.000000, 8)"
    }
 ]
