namespace Microsoft.FSharp.FscHost

open FSharp.Quotations
open FSharp.Reflection
open System
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.FscHost

module internal Reflection =

    let (|FsFunc|_|) (t: Type) =
        match t with
        | t when FSharpType.IsFunction t -> Some(FSharpType.GetFunctionElements t)
        | _ -> None

    let (|FsTuple|_|) (t: Type) =
        match t with
        | t when FSharpType.IsTuple t -> Some(FSharpType.GetTupleElements t |> Seq.toList)
        | _ -> None

    type State =
        { Vars: Var list
          Index: int
          TupleVar: Var option
          GenericParams: Dictionary<Type, Type> }

        static member Empty =
            { Vars = []
              Index = 0
              TupleVar = None
              GenericParams = Dictionary<Type, Type>() }

    /// Converts a MethodInfo to Expr. Expr can be further compiled, cast to a statically-known type, and invoked.
    let toExpr (funcType: Type) (methodInfo: MethodInfo) : Expr =
        let parameters = methodInfo.GetParameters() |> Seq.toList

        let funTypes =
            funcType
            |> Seq.unfold (function
                | FsFunc(a, b) -> Some(a, b)
                | _ -> None)
            |> Seq.toList

        let handleGenerics (f: Type) (p: ParameterInfo) (s: State) =
            let rec handle f (p: Type) s =
                match (f, p) with
                | f, p when p.IsGenericMethodParameter ->
                    if s.GenericParams.ContainsKey(p) && s.GenericParams.[p] <> f then
                        failwithf
                            $"Failed to add type '%s{f.ToString()}' as a substitute for generic parameter type '%s{p.ToString()}'. It already has a substitute (%s{s.GenericParams.[p].ToString()})"
                    else
                        s.GenericParams.TryAdd(p, f) |> ignore
                | FsFunc(fi, fo), FsFunc(pi, po) ->
                    handle fi pi s
                    handle fo po s
                | _ -> ()

            handle f p.ParameterType s

        let rec build (fs: Type list) (ps: ParameterInfo list) (s: State) =
            match (fs, ps) with
            | f :: fs', p :: ps' when f = p.ParameterType || p.ParameterType.IsGenericMethodParameter ->
                handleGenerics f p s
                let v = Var(p.Name, f)
                Expr.Lambda(v, build fs' ps' { s with Vars = v :: s.Vars })

            | FsTuple us as f :: _, p :: ps' when s.Index < us.Length ->
                handleGenerics us.[s.Index] p s
                let v = Var($"{p.Name}_{s.Index}", us.[s.Index])

                let expr tv =
                    Expr.Let(
                        v,
                        Expr.TupleGet(Expr.Var tv, s.Index),
                        build
                            fs
                            ps'
                            { s with
                                Vars = v :: s.Vars
                                Index = s.Index + 1
                                TupleVar = Some tv }
                    )

                match s.TupleVar with
                | Some tv -> expr tv
                | None ->
                    let tupleVar = Var("tupledArg", f)
                    Expr.Lambda(tupleVar, expr tupleVar)

            | FsTuple _ as f :: fs', p :: _ ->
                handleGenerics f p s
                build fs' ps { s with Index = 0; TupleVar = None }

            | FsFunc _ as f :: fs', p :: ps' ->
                handleGenerics f p s
                let v = Var(p.Name, f)
                Expr.Lambda(v, build fs' ps' { s with Vars = v :: s.Vars })

            | _ ->
                let vars = s.Vars |> Seq.map (Expr.Var) |> Seq.rev |> Seq.toList

                let maybeGeneric =
                    if methodInfo.IsGenericMethod then
                        let gps =
                            methodInfo.GetGenericArguments()
                            |> Seq.map (fun t -> s.GenericParams.[t])
                            |> Seq.toArray

                        methodInfo.MakeGenericMethod(gps)
                    else
                        methodInfo

                Expr.Call(maybeGeneric, vars)

        match parameters with
        | [] -> Expr.Lambda(Var("()", typeof<unit>), build funTypes [] State.Empty)
        | ps -> build funTypes ps State.Empty

[<RequireQualifiedAccess>]
module Member =
    open FSharp.Linq.RuntimeHelpers

    /// Retrieves a type member given by fully-qualified path from an assembly
    let getCore<'a> (candidates: Type list) (memberPath: string) =

        let memberName =
            let splitIndex = memberPath.LastIndexOf(".")
            memberPath[splitIndex + 1 ..]

        let tryCast (actualType: string) (value: obj) =
            try
                value :?> 'a
            with :? InvalidCastException ->
                raise (ScriptMemberHasInvalidType(memberPath, actualType))

        let (|Property|_|) (name: string) (t: Type) =
            match t.GetProperty(name, BindingFlags.Static ||| BindingFlags.Public) with
            | null -> None
            | pi -> Some pi

        let (|Method|_|) (name: string) (t: Type) =
            match t.GetMethod(name, BindingFlags.Static ||| BindingFlags.Public) with
            | null -> None
            | mi when FSharpType.IsFunction typeof<'a> -> Some mi
            | _ -> None

        match candidates with
        | [ Property memberName pi ] -> pi.GetValue(null) |> tryCast (pi.PropertyType.ToString())
        | [ Method memberName mi ] ->
            mi
            |> Reflection.toExpr typeof<'a>
            //|> (fun x -> printfn "%A" x; x)
            |> LeafExpressionConverter.EvaluateQuotation
            |> tryCast (mi.ToString())
        | [ t ] ->
            raise (
                ScriptMemberNotFound(
                    memberPath,
                    t.GetMembers() |> Seq.map (fun p -> $"{p.MemberType}: {p.Name}") |> Seq.toList
                )
            )
        | [] -> raise (ExpectedMemberParentTypeNotFound memberPath)
        | _ -> raise (MultipleMemberParentTypeCandidatesFound memberPath)

    /// Retrieves a type member given by fully-qualified path from an assembly using StringComparison.CurrentCulture for comparing names
    let get<'a> (memberPath: string) (assembly: Assembly) =
        let fqTypeName =
            let splitIndex = memberPath.LastIndexOf(".")
            memberPath[0 .. splitIndex - 1]

        let candidateTypes =
            assembly.GetTypes()
            |> Seq.where (fun typ -> typ.FullName = fqTypeName)
            |> Seq.toList

        getCore<'a> candidateTypes memberPath
