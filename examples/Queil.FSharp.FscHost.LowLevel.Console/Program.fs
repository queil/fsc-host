open Queil.FSharp.FscHost

module Script =

  let private assembly =
    File "script.fsx"
    |> CompilerHost.getAssembly Options.Default
    |> Async.RunSynchronously

  let generic<'a, 'b> =
    assembly |> Member.get<'a * 'b -> string> "Script.generic"

  let helloFromScript =
    assembly |> Member.get<string -> string> "Script.helloFromScript"

  let myPrimes =
    assembly |> Member.get<int list> "Script.myPrimes"

try
  let myName = "Console Example"
  myName |> Script.helloFromScript |> printfn "%s"

  printfn "Primes of the day: "
  Script.myPrimes |> Seq.iter (printfn "%i")

  printfn "Generics are supported:"
  Script.generic (10, "test") |> printfn "%s"
  Script.generic (true, 19.0M) |> printfn "%s"

with
// you can handle more exceptions from the CompilerHost here
| ScriptMemberNotFound(memberName, foundProperties) ->
  printfn "Couldn't find member: '%s'" memberName
  printfn "Found members: "
  foundProperties |> Seq.iter(printfn "%s")
