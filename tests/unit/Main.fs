module unit

open Expecto
open System.IO
open System.Reflection

[<EntryPoint>]
let main argv =

    Tests.runTestsInAssemblyWithCLIArgs [] argv
