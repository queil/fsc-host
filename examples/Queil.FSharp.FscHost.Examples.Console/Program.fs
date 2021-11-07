open Queil.FSharp.FscHost

try

  // compile a script and retrieve two properties out of it
  let getScriptProperties () =
    File "script.fsx"
    |> CompilerHost.getScriptProperties2 Options.Default
         (Property<string -> string>.Path "Script.helloFromScript2")
         (Property<int list>.Path "Script.myPrimes")

  let (helloWorld, primesOfTheDay) = getScriptProperties () |> Async.RunSynchronously

  let myName = "Console Example"

  myName |> helloWorld |> printfn "%s"

  printfn "Primes of the day: "
  primesOfTheDay |> Seq.iter (printfn "%i")

with
// you can handle more exceptions from the CompilerHost here
| ScriptMemberNotFound(memberName, foundProperties) ->
  printfn "Couldn't find member: '%s'" memberName
  printfn "Found members: "
  foundProperties |> Seq.iter(printfn "%s")
