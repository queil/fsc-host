﻿namespace Queil.FSharp.Hashing

open System
open System.IO
open System.Text
open System.Security.Cryptography
open System.Threading

[<RequireQualifiedAccess>]
module Hash =
    let private sha256Hasher = new ThreadLocal<SHA256>(fun () -> SHA256.Create())

    let sha256 (s: string) =

        s
        |> Encoding.UTF8.GetBytes
        |> sha256Hasher.Value.ComputeHash
        |> Convert.ToHexString

    let short (s: string) = s[0..10].ToLowerInvariant()

    let deepSourceHash rootContentHash sourceFiles =

        let fileHash filePath = File.ReadAllText filePath |> sha256

        let combinedHash =
            sourceFiles
            |> Seq.map fileHash
            |> Seq.sort
            |> Seq.fold (fun a b -> a + b) String.Empty
            |> (+) rootContentHash
            |> sha256

        short combinedHash
