namespace Queil.FSharp.DependencyManager.Paket

open System
open System.IO

[<RequireQualifiedAccess>]
module Rids =


    // Normalize distro-specific RIDs (fedora.44-x64, rhel.9-x64, alpine.3.18-x64, ubuntu.22.04-x64)
    // to portable RIDs (linux-x64, linux-musl-x64). .NET 8+ uses portable RIDs but some
    // distros (Fedora's dotnet package) still report distro-specific ones.
    let normalize (r: string) =
        if String.IsNullOrEmpty r then
            r
        else
            let arch =
                let i = r.LastIndexOf('-')

                if i > 0 && i < r.Length - 1 then
                    Some(r.Substring(i + 1))
                else
                    None

            let isDistroLinux =
                r.Contains('.')
                && not (r.StartsWith "osx")
                && not (r.StartsWith "win")
                && not (r.StartsWith "linux-")

            match isDistroLinux, arch with
            | true, Some a when r.StartsWith "alpine" -> $"linux-musl-{a}"
            | true, Some a -> $"linux-{a}"
            | _ -> r

    // RID fallback chain: linux-x64 -> linux -> unix; osx-arm64 -> osx -> unix; win-x64 -> win
    let ridChain rid =
        let normalized = normalize rid

        let rec expand (r: string) =
            seq {
                yield r
                let i = r.LastIndexOf('-')

                if i > 0 then
                    yield! expand (r.Substring(0, i))
            }

        seq {
            yield! expand normalized

            if
                normalized.StartsWith("linux")
                || normalized.StartsWith("osx")
                || normalized.StartsWith("freebsd")
            then
                yield "unix"
        }
        |> Seq.distinct
        |> Seq.toList

    let rewriteRuntimeRefs (scriptPath: string) (rid: string) (log: string -> unit) =


        // matches:  #r @"<path>/lib/<tfm>/<dll>"
        // also handles plain (non-verbatim) #r "..."  and Windows backslashes
        let sep = @"[/\\]"

        let pattern =
            $@"^(\s*#r\s+@?"")(?<root>.+?){sep}lib{sep}(?<tfm>[^/\\]+){sep}(?<dll>[^/\\""]+\.dll)(""\s*)$"

        let rx =
            System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.Compiled)

        let tryPickTfm (ridDir: string) (preferredTfm: string) =
            if not (Directory.Exists ridDir) then
                None
            else
                // prefer exact TFM match, then highest net*, then netstandard2.1, then netstandard2.0
                let tfms =
                    Directory.GetDirectories ridDir
                    |> Array.map (Path.GetFileName >> Option.ofObj)
                    |> Array.choose id

                let rank (t: string) =
                    if t = preferredTfm then
                        1000
                    elif
                        t.StartsWith "net"
                        && not (t.StartsWith "netstandard")
                        && not (t.StartsWith "netcoreapp")
                    then
                        // net9.0 -> 900, net10.0 -> 1000... good enough
                        match Double.TryParse(t.Substring 3) with
                        | true, v -> int (v * 100.0)
                        | _ -> 0
                    elif t = "netstandard2.1" then
                        10
                    elif t = "netstandard2.0" then
                        5
                    else
                        0

                tfms |> Array.sortByDescending rank |> Array.tryHead

        let rewriteLine (line: string) =
            let m = rx.Match line

            if not m.Success then
                line
            else
                let root = m.Groups["root"].Value
                let tfm = m.Groups["tfm"].Value
                let dll = m.Groups["dll"].Value

                let candidate =
                    ridChain rid
                    |> List.tryPick (fun r ->
                        let ridLibRoot = Path.Combine(root, "runtimes", r, "lib")

                        tryPickTfm ridLibRoot tfm
                        |> Option.map (fun chosenTfm -> Path.Combine(ridLibRoot, chosenTfm, dll))
                        |> Option.filter File.Exists)

                match candidate with
                | Some p ->
                    log $"rewrite: {dll} -> runtimes/.../{Path.GetFileName(Path.GetDirectoryName p)}"
                    $"#r \"{p}\""
                | None -> line

        let lines = File.ReadAllLines scriptPath
        let rewritten = lines |> Array.map rewriteLine

        if rewritten <> lines then
            File.WriteAllLines(scriptPath, rewritten)
