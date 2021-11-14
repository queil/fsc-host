open Queil.FSharp.FscHost

try

  // compile a script and retrieve two properties out of it
  let getScriptProperties () =
    File "script.fsx"
    |> CompilerHost.getScriptProperties3 Options.Default
         (Property<string -> string>.Path "Script.helloFromScript")
         (Property<int list>.Path "Script.myPrimes")
         (Property<_ -> string>.Path "Script.generic")

  let (helloWorld, primesOfTheDay, generic) = getScriptProperties () |> Async.RunSynchronously

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
