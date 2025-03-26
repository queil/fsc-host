# fsc-host [![Build Status](https://dev.azure.com/queil/fsc-host/_apis/build/status/queil.fsc-host?branchName=main)](https://dev.azure.com/queil/fsc-host/_build/latest?definitionId=3&branchName=main)  [![NuGet Badge](https://buildstats.info/nuget/Queil.FSharp.FscHost?includePreReleases=true)](https://www.nuget.org/packages/Queil.FSharp.FscHost) [![Coverage](https://img.shields.io/azure-devops/coverage/queil/fsc-host/3?style=flat)](https://img.shields.io/azure-devops/coverage/queil/fsc-host/3?style=plastic)

## Extend your F# apps with F# scripts

You can easily extend your applications by calling functions and retrieving values on run time from dynamically compiled scripts. A bare minimum example (Plugin API):

##### plugins/default/plugin.fsx (Plugin)
```fsharp
let plugin (s:string) = printfn $"HELLO: %s{s}"
```
##### Program.fs (Host)
```fsharp
let myWriter =
  plugin<string -> unit> {
    load
  } |> Async.RunSynchronously

myWriter $"I hereby send the message"
```
##### Output

```
HELLO: I hereby send the message
```
## What is supported

* Accessing script members on the host side in a strongly-typed way (please note: if the members are not of expected types they will fail casting causing a runtime exception - there is no magic that could fix it)   
* Consuming values and functions (including generics)
* Referencing other scripts and dlls via `#r`
* Referencing NuGet packages via the [`#r "nuget: ...` directive](https://docs.microsoft.com/en-us/dotnet/fsharp/tools/fsharp-interactive/#referencing-packages-in-f-interactive)
* Paket support via the `#r "paket: ..."` directive
* Controlling compilation options
* Full assembly caching (opt-in via options)
* Basic logging (by passing a logging func via options)

## Requirements

* .NET SDK (it is convenient to package apps using `fsc-host` as Docker images)

## Warning

This project is still in v0 which means the public API hasn't stabilised yet and breaking changes may happen between minor versions. Breaking changes are indicated in the release notes in GitHub releases. 

## Example (Basic API)

1. Create a console app and add the package

```sh
dotnet new console -lang F# --name fsc-host-test && cd fsc-host-test && dotnet add package Queil.FSharp.FscHost --version 0.16.0
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

```

3. You should get the following output when `dotnet run`:

```
HELLO FROM THE SCRIPT, Console Example
Primes of the day: 
2
3
5

```

## APIs

The public API of this library comes in three flavours:

* plugin - high-level, declarative, and it's the recommended API to use.
  [Example](examples/Plugin)

* basic - the `CompilerHost.getMember` functions family. They take a script and options as the input and return a tuple of extracted member(s).
  [Example](examples/Simple)

* compile'n'extract - `CompilerHost.getAssembly` can be used to compile a script into an assembly (which is automatically loaded). Then members can be extracted with `Member.get` function. This API gives more flexibility and enables using generic functions. 
  [Example](examples/CompileAndExtract)

## Known issues

* it's recommended to avoid mixing `nuget:` and `paket: nuget` in `#r` directives as it may result in an error where two versions of the same assembly are resolved. If it is not possible to avoid then the top-level script should specify [NuGet-compatible resolution strategies](https://fsprojects.github.io/Paket/dependencies-file.html#Resolver-strategy-for-transitive-dependencies) for paket. I.e. `strategy: min` and `lowest_matching: true`.

## Resources

* [My blog post about the package](https://queil.net/2021/10/embedding-fsharp-compiler-fsc-host-nuget/)
* [FSharp Compiler Docs](https://fsharp.github.io/fsharp-compiler-docs/)

## Development

Fix FSharp.Core package hash locally:

```sh
docker run --rm -it  -v $(pwd):/build -w /build  mcr.microsoft.com/dotnet/sdk:8.0 dotnet restore --force-evaluate
```
