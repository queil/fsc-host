namespace Queil.FSharp.FscHost

module Common =

    let options =
        { Options.Default with
            UseCache = true
            Logger = printfn "%s" }

    let invoke<'a> (func: unit -> 'a) =
        try
            func ()
        with ScriptMemberHasInvalidType(propertyName, actualTypeSignature) ->
            printfn
                $"Diagnostics: Property '%s{propertyName}' should be of type '%s{typeof<'a>.ToString()}' but is '%s{actualTypeSignature}'"

            reraise ()
