namespace Queil.FSharp.FscHost

open System

[<AutoOpen>]
module Errors =
  exception NuGetRestoreFailed of message: string
  
  type ScriptParseError(messages : string seq) =
    inherit Exception(String.Join(Environment.NewLine, messages))
    member _.Diagnostics = messages

   type ScriptCompileError(messages : string seq) =
    inherit Exception(String.Join(Environment.NewLine, messages))
    member _.Diagnostics = messages
  
  exception ScriptModuleNotFound of path: string * moduleName: string
  exception ScriptMemberHasInvalidType of memberName: string * actualTypeSignature: string
  exception ScriptMemberNotFound of memberName: string * foundMembers: string list
  exception ExpectedMemberParentTypeNotFound of memberPath: string
  exception MultipleMemberParentTypeCandidatesFound of memberPath: string
