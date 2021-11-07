# fsc-host [![Build Status](https://dev.azure.com/queil/fsc-host/_apis/build/status/queil.fsc-host?branchName=main)](https://dev.azure.com/queil/fsc-host/_build/latest?definitionId=3&branchName=main)  [![NuGet Badge](https://buildstats.info/nuget/Queil.FSharp.FscHost?includePreReleases=true)](https://www.nuget.org/packages/Queil.FSharp.FscHost) [![Coverage](https://img.shields.io/azure-devops/coverage/queil/fsc-host/3?style=flat)](https://img.shields.io/azure-devops/coverage/queil/fsc-host/3?style=plastic)

## Host the F# compiler in your own apps.

You can easily extend your applications by calling functions and retrieving values on runtime from dynamically 
compiled scripts.

## What is supported

* Referencing NuGet packages and other scripts via the usual [`#r` directive](https://docs.microsoft.com/en-us/dotnet/fsharp/tools/fsharp-interactive/#referencing-packages-in-f-interactive)
* Controlling compilation options
* Basic assembly caching (opt-in via options - so far invalidation is only supported for the root script file)
* Basic logging (by passing a logging func via options)

## Requirements

* .NET SDK 6 (it's the best to package apps using fsc-host as Docker images)

## Example

1. Create a console app and add the package

```
dotnet new console -lang F# --name fsc-host-test && cd fsc-host-test && dotnet add package Queil.FSharp.FscHost --version 0.13.0
```

2. Save the below as `script.fsx`:

```fsharp
let helloFromScript name = sprintf "HELLO FROM THE SCRIPT, %s" name

let myPrimes = [2; 3; 5]
```

2. In your `Program.cs`:

```fsharp
open Queil.FSharp.FscHost

try

  // compile a script and retrieve two properties out of it
  let getScriptProperties () =
    File "script.fsx"
    |> CompilerHost.getScriptProperties2 Options.Default
         (Property<string -> string>.Path "Script.helloFromScript")
         (Property<int list>.Path "Script.myPrimes")

  let (helloWorld, primesOfTheDay) = getScriptProperties () |> Async.RunSynchronously

  let myName = "Console Example"

  myName |> helloWorld |> printfn "%s"

  printfn "Primes of the day: "
  primesOfTheDay |> Seq.iter (printfn "%i")

with
// you can handle more exceptions from the CompilerHost here
| ScriptMemberNotFound(propertyName, foundProperties) ->
  printfn "Couldn't find member: '%s'" propertyName
  printfn "Found properties: "
  foundProperties |> Seq.iter(printfn "%s")

```

3. You should get the following output when `dotnet run`:

```
HELLO FROM THE SCRIPT, Console Example
Primes of the day: 
2
3
5

```

## Resources

* [My blog post about the package](https://queil.net/2021/10/embedding-fsharp-compiler-fsc-host-nuget/)
* [FSharp Compiler Docs](https://fsharp.github.io/fsharp-compiler-docs/)
