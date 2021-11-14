namespace Queil.FSharp.FscHost

[<AutoOpen>]
module Errors =
  exception NuGetRestoreFailed of message: string
  exception ScriptParseError of errors: string seq
  exception ScriptCompileError of errors: string seq
  exception ScriptModuleNotFound of path: string * moduleName: string
  exception ScriptMemberHasInvalidType of memberName: string * actualTypeSignature: string
  exception ScriptMemberNotFound of memberName: string * foundMembers: string list
  exception ExpectedMemberParentTypeNotFound of memberPath: string
  exception MultipleMemberParentTypeCandidatesFound of memberPath: string
