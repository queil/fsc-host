module Queil.FSharp.DependencyManager.Paket.Tests.Rids

open Expecto
open Queil.FSharp.DependencyManager.Paket

let private normalizeCases =
    [
        // distro-specific linux RIDs -> portable linux RIDs
        "fedora.44-x64", "linux-x64"
        "fedora.39-arm64", "linux-arm64"
        "rhel.9-x64", "linux-x64"
        "centos.7-x64", "linux-x64"
        "ubuntu.22.04-x64", "linux-x64"
        "ubuntu.24.04-arm64", "linux-arm64"
        "debian.12-x64", "linux-x64"
        "opensuse.15-x64", "linux-x64"
        // alpine -> musl
        "alpine.3.18-x64", "linux-musl-x64"
        "alpine.3.19-arm64", "linux-musl-arm64"
        // portable RIDs pass through
        "linux-x64", "linux-x64"
        "linux-arm64", "linux-arm64"
        "linux-musl-x64", "linux-musl-x64"
        "osx-x64", "osx-x64"
        "osx-arm64", "osx-arm64"
        "win-x64", "win-x64"
        "win-arm64", "win-arm64"
        "unix", "unix"
        "any", "any"
        // osx with version suffix left alone (current behavior)
        "osx.13-x64", "osx.13-x64"
        // empty
        "", ""
    ]

[<Tests>]
let normalizeTests =
    testList "Rids.normalize" [
        for input, expected in normalizeCases ->
            testCase $"'%s{input}' -> '%s{expected}'" <| fun _ ->
                Expect.equal (Rids.normalize input) expected "normalized RID"
    ]

[<Tests>]
let ridChainTests =
    testList "Rids.ridChain" [
        testCase "fedora.44-x64 -> linux-x64, linux, unix" <| fun _ ->
            Expect.equal
                (Rids.ridChain "fedora.44-x64")
                [ "linux-x64"; "linux"; "unix" ]
                "fedora normalizes then expands"

        testCase "alpine.3.18-x64 -> musl chain then unix" <| fun _ ->
            Expect.equal
                (Rids.ridChain "alpine.3.18-x64")
                [ "linux-musl-x64"; "linux-musl"; "linux"; "unix" ]
                "alpine normalizes to musl variant"

        testCase "linux-x64 -> linux, unix" <| fun _ ->
            Expect.equal
                (Rids.ridChain "linux-x64")
                [ "linux-x64"; "linux"; "unix" ]
                "portable linux RID"

        testCase "osx-arm64 -> osx, unix" <| fun _ ->
            Expect.equal
                (Rids.ridChain "osx-arm64")
                [ "osx-arm64"; "osx"; "unix" ]
                "osx falls back to unix"

        testCase "win-x64 -> win (no unix)" <| fun _ ->
            Expect.equal
                (Rids.ridChain "win-x64")
                [ "win-x64"; "win" ]
                "win does not fall back to unix"

        testCase "freebsd-x64 -> freebsd, unix" <| fun _ ->
            Expect.equal
                (Rids.ridChain "freebsd-x64")
                [ "freebsd-x64"; "freebsd"; "unix" ]
                "freebsd falls back to unix"

        testCase "ubuntu.22.04-arm64 -> linux-arm64, linux, unix" <| fun _ ->
            Expect.equal
                (Rids.ridChain "ubuntu.22.04-arm64")
                [ "linux-arm64"; "linux"; "unix" ]
                "ubuntu arm64 normalizes then expands"

        testCase "chain has no duplicates" <| fun _ ->
            let chain = Rids.ridChain "linux-x64"
            Expect.equal (List.length chain) (chain |> List.distinct |> List.length) "no duplicates"
    ]
