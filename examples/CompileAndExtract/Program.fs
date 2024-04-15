open Queil.FSharp.FscHost

module Script =

    let private assembly =
        File "script.fsx"
        |> CompilerHost.getAssembly Options.Default
        |> Async.RunSynchronously

    let generic<'a, 'b> = assembly |> Member.get<'a * 'b -> string> "Script.generic"

    let helloFromScript =
        assembly |> Member.get<string -> string> "Script.helloFromScript"

    let myPrimes = assembly |> Member.get<int list> "Script.myPrimes"

open Script

let myName = "Console Example"
myName |> helloFromScript |> printfn "%s"

printfn "Primes of the day: "
myPrimes |> Seq.iter (printfn "%i")

printfn "Generics are supported:"
generic (10, "test") |> printfn "%s"
generic (true, 19.0M) |> printfn "%s"
