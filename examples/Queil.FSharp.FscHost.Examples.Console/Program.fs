open Queil.FSharp.FscHost

try

  // compile a script and retrieve two properties out of it
  let getScriptProperties () =
    File "script.fsx"
    |> CompilerHost.getScriptProperties2 Options.Default
         (Property<string -> string>.Path "Script.export")
         (Property<int list>.Path "Script.myPrimes")

  let (helloWorld, primesOfTheDay) = getScriptProperties () |> Async.RunSynchronously

  let myName = "Console Example"

  myName |> helloWorld |> printfn "%s"

  printfn "Primes of the day: "
  primesOfTheDay |> Seq.iter (printfn "%i")

with
// you can handle more exceptions from the CompilerHost here
| ScriptsPropertyNotFound(propertyName, foundProperties) ->
  printfn "Couldn't find property: '%s'" propertyName
  printfn "Found properties: "
  foundProperties |> Seq.iter(printfn "%s")
