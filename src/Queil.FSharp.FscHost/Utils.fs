namespace Queil.FSharp.FscHost

module Utils =
    open System.Text.RegularExpressions

    let (|ParseRegex|_|) regex str =
        let m = Regex(regex).Match(str)

        if m.Success then
            Some(List.tail [ for x in m.Groups -> x.Value ])
        else
            None
