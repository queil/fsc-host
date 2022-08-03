open Queil.FSharp.FscHost.Plugin

let myWriter =
  plugin<string -> unit> {
    load
  } |> Async.RunSynchronously

myWriter $"I hereby send the message"
