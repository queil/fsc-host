namespace Queil.FSharp.FscHost

open System.IO

module Common =

    let options =
        { Options.Default with
            UseCache = true
            Logger = Some(printfn "%s") }

    let invoke<'a> (func: unit -> 'a) : 'a =
        try
            func ()
        with ScriptMemberHasInvalidType(propertyName, actualTypeSignature) ->
            printfn
                $"Diagnostics: Property '%s{propertyName}' should be of type '%s{typeof<'a>.ToString()}' but is '%s{actualTypeSignature}'"

            reraise ()


    let ensureTempPath () =
        let tmpPath =
            Path.Combine(Path.GetTempPath(), ".fsch-override", Path.GetRandomFileName())

        Directory.CreateDirectory tmpPath |> ignore
        tmpPath
