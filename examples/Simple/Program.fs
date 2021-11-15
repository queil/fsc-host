open Queil.FSharp.FscHost

try

  // compile a script and retrieve two properties out of it
  let getScriptProperties () =
    File "script.fsx"
    |> CompilerHost.getMember2 Options.Default
         (Member<string -> string>.Path "Script.helloFromScript")
         (Member<int list>.Path "Script.myPrimes")

  let (helloWorld, primesOfTheDay) = getScriptProperties () |> Async.RunSynchronously

  let myName = "Console Example"

  myName |> helloWorld |> printfn "%s"

  printfn "Primes of the day: "
  primesOfTheDay |> Seq.iter (printfn "%i")

with
// you can handle more exceptions from the CompilerHost here
| ScriptMemberNotFound(name, foundMembers) ->
  printfn "Couldn't find member: '%s'" name
  printfn "Found members: "
  foundMembers |> Seq.iter(printfn "%s")
