﻿module Renamer

open System.Collections.Generic
open Ast
open Options.Globals

module private RenamerImpl =

    // Environment for renamer
    // This object is useful to separate the AST walking from the renaming strategy.
    // Maybe we could use a single mutable object, instead of creating envs all the time.
    // TODO: create a real class.
    [<NoComparison; NoEquality>]
    type Env = {
        // Map from an old variable name to the new one.
        varRenames: Map<Ident, Ident>
        // Map from a new function name and function arity to the old name.
        funRenames: Map<Ident, Map<int, Ident>>
        // List of names that are still available.
        availableNames: Ident list

        // Whether multiple functions can have the same name (but different arity).
        allowOverloading: bool
        // Function that decides a name (and returns the modified Env).
        newName: Env -> Ident -> (Env * string)
        // Function called when we enter a function, optionally updates the Env.
        onEnterFunction: Env -> Stmt -> Env
    }
    with
        static member Create(availableNames, allowOverloading, newName, onEnterFunction) = {
            varRenames = Map.empty
            funRenames = Map.empty
            availableNames = availableNames
            allowOverloading = allowOverloading
            newName = newName
            onEnterFunction = onEnterFunction
        }

        member this.Rename(oldName, newName) =
            let names = this.availableNames |> List.filter ((<>) newName)
            {this with varRenames = this.varRenames.Add(oldName, newName); availableNames = names}

        member this.Update(varRenames, funRenames, availableNames) = 
            {this with varRenames = varRenames; funRenames = funRenames; availableNames = availableNames}


          (* Contextual renaming *)

    let computeContextTable text =
        let contextTable = new HashMultiMap<(char*char), int>(HashIdentity.Structural)
        Seq.pairwise text |> Seq.iter (fun (prev, next) ->
            match contextTable.TryFind (prev, next) with
            | Some n -> contextTable.[(prev, next)] <- n + 1
            | None -> contextTable.[(prev, next)] <- 1
        )
        contextTable
        //let chars, n = Seq.maxBy snd [for pair in contextTable -> pair.Key, pair.Value]
        //printfn "max occ: %A -> %d" chars n

    // /!\ This function is a performance bottleneck.
    let chooseIdent (contextTable: HashMultiMap<(char*char), int>) ident candidates =
        let allChars = [char 32 .. char 127] // printable chars
        let prevs = allChars |> Seq.choose (fun c ->
            match contextTable.TryFind (c, ident) with
            | Some occ -> Some (c, occ)
            | None -> None
        )
        let nexts = allChars |> Seq.choose (fun c ->
            match contextTable.TryFind (ident, c) with
            | Some occ -> Some (c, occ)
            | None -> None
        )

        let mutable best = -10000, ""
        // For performance, consider at most 25 candidates.
        for word: string in candidates |> Seq.take 25 do
            let firstLetter = word.[0]
            let lastLetter = word.[word.Length - 1]
            let mutable score = 0

            for c, _ in prevs do
                match contextTable.TryFind (c, firstLetter) with
                | None -> ()
                | Some occ -> score <- score + occ

            for c, _ in nexts do
                match contextTable.TryFind (lastLetter, c) with
                | None -> ()
                | Some occ -> score <- score + occ

            if word.Length > 1 then
                score <- score - 1000 // avoid long names if there are 1-letter names available
                match contextTable.TryFind (firstLetter, lastLetter) with
                | None -> ()
                | Some occ -> score <- score + occ

            if score > fst best then best <- score, word

        let best = snd best
        assert (best.Length > 0)
        let firstLetter = best.[0]
        let lastLetter = best.[best.Length - 1]

        // update table
        for c in allChars do
            match contextTable.TryFind (c, ident), contextTable.TryFind (c, firstLetter) with
            | None, _ -> ()
            | Some n1, None -> contextTable.[(c, firstLetter)] <- n1
            | Some n1, Some n2 -> contextTable.[(c, firstLetter)] <- n1 + n2
            match contextTable.TryFind (ident, c), contextTable.TryFind (lastLetter, c) with
            | None, _ -> ()
            | Some n1, None -> contextTable.[(lastLetter, c)] <- n1
            | Some n1, Some n2 -> contextTable.[(lastLetter, c)] <- n1 + n2
          
        best


                                    (* ** Renamer ** *)

    let alwaysNewName (numberOfUsedIdents: int ref) (env: Env) id =
        incr numberOfUsedIdents
        let newName = sprintf "%04d" !numberOfUsedIdents
        env.Rename(id, newName), newName

    // FIXME: handle 2-letter names
    let optimizeContext contextTable env id =
        let cid = char (1000 + int id)
        let newName = chooseIdent contextTable cid env.availableNames
        env.Rename(id, newName), newName

    let dontRename (env: Env) name =
        env.Rename(name, name)

    let dontRenameList env names =
        let mutable env = env
        for name in names do env <- dontRename env name
        env

    let renFunction env nbArgs id =
        if List.exists ((=) id) options.noRenamingList then env, id // don't rename "main"
        else
            // we're looking for a function name, already used before,
            // but not with the same number of arg, and which is not in options.noRenamingList.
            let isFunctionNameAvailableForThisArity (x: KeyValuePair<Ident,Map<int,Ident>>) =
                not (x.Value.ContainsKey nbArgs ||
                     List.exists ((=) x.Key) options.noRenamingList)

            match env.funRenames |> Seq.tryFind isFunctionNameAvailableForThisArity with
            | Some res when env.allowOverloading ->
                // overload an existing function name used with a different arity
                let newName = res.Key
                let funRenames = env.funRenames.Add (res.Key, res.Value.Add(nbArgs, id))
                let env = env.Update(env.varRenames.Add(id, newName), funRenames, env.availableNames)
                env, newName
            | _ ->
                // find a new function name
                let env, newName = env.newName env id
                let funRenames = env.funRenames.Add (newName, Map.empty.Add(nbArgs, id))
                let env = env.Update(env.varRenames, funRenames, env.availableNames)
                env, newName

    let renFctName env (f: FunctionType) =
        let isExternal = options.hlsl && f.semantics <> []
        if (isExternal && options.preserveExternals) || options.preserveAllGlobals then
            env, f
        else
            let newEnv, newName = renFunction env (List.length f.args) f.fName
            if isExternal then Formatter.export "F" f.fName newName
            newEnv, {f with fName = newName}

    let renList env fct li =
        let mutable env = env
        let res = li |> List.map (fun i ->
            let x = fct env i
            env <- fst x
            snd x)
        env, res

    let rec renExpr env =
        let mapper _ = function
            | Var v -> Var (defaultArg (Map.tryFind v env.varRenames) v)
            | e -> e
        mapExpr (mapEnv mapper id)

    let renDecl isTopLevel env (ty:Type, vars) : Env * Decl =
        let aux env decl =
            let env, newName =
                let ext =
                    match ty.typeQ with
                    | Some tyQ -> ["in"; "out"; "attribute"; "varying"; "uniform"]
                                 |> List.exists (fun s -> tyQ.Contains(s))
                    | None -> false
                if isTopLevel && (ext || options.hlsl || options.preserveAllGlobals) then
                    if options.preserveExternals then
                        dontRename env decl.name, decl.name
                    else
                        let env, newName = env.newName env decl.name
                        Formatter.export "" decl.name newName // TODO: first argument seems now useless
                        env, newName
                else
                    env.newName env decl.name

            let init = Option.map (renExpr env) decl.init
            let size = Option.map (renExpr env) decl.size
            env, {decl with name=newName; size=size; init=init}
        let env, res = renList env aux vars
        env, (ty, res)

    // "Garbage collection": remove names that are not used in the block
    // so that we can reuse them. In other words, this function allows us
    // to shadow global variables in a function.
    let shadowVariables (env: Env) block =
        let d = HashSet()
        let collect mEnv = function
            | Var id as e ->
                if not (mEnv.vars.ContainsKey(id)) then d.Add id |> ignore
                e
            | FunCall(Var id, li) as e ->
                match env.funRenames.TryFind id with
                | Some m -> if not (m.ContainsKey li.Length) then d.Add id |> ignore
                | None -> d.Add id |> ignore
                e
            | e -> e
        mapStmt (mapEnv collect id) block |> ignore
        let set = HashSet(Seq.choose env.varRenames.TryFind d)
        let varRenames, reusable = env.varRenames |> Map.partition (fun _ id -> id.Length > 2 || set.Contains id)
        let reusable = reusable |> Seq.filter (fun x -> not (List.exists ((=) x.Value) options.noRenamingList))
        let allAvailable = [for i in reusable -> i.Value] @ env.availableNames |> Seq.distinct |> Seq.toList
        env.Update(varRenames, env.funRenames, allAvailable)

    let rec renStmt env =
        let renOpt o = Option.map (renExpr env) o
        function
        | Expr e -> env, Expr (renExpr env e)
        | Decl d ->
            let env, res = renDecl false env d
            env, Decl res
        | Block b ->
            let _, res = renList env renStmt b
            env, Block res
        | If(cond, th, el) ->
            let _, th = renStmt env th
            let el = Option.map (fun x -> snd (renStmt env x)) el
            env, If(renExpr env cond, th, el)
        | ForD(init, cond, inc, body) ->
            let newEnv, init = renDecl false env init
            let _, body = renStmt newEnv body
            let cond = Option.map (renExpr newEnv) cond
            let inc = Option.map (renExpr newEnv) inc
            if options.hlsl then newEnv, ForD(init, renOpt cond, renOpt inc, body)
            else env, ForD(init, renOpt cond, renOpt inc, body)
        | ForE(init, cond, inc, body) ->
            let _, body = renStmt env body
            env, ForE(renOpt init, renOpt cond, renOpt inc, body)
        | While(cond, body) ->
            let _, body = renStmt env body
            env, While(renExpr env cond, body)
        | DoWhile(cond, body) ->
            let _, body = renStmt env body
            env, DoWhile(renExpr env cond, body)
        | Jump(k, e) -> env, Jump(k, renOpt e)
        | Verbatim _ as v -> env, v

    let rec renTopLevelName env = function
        | TLDecl d ->
            let env, res = renDecl true env d
            env, TLDecl res
        | Function(fct, body) ->
            let env, res = renFctName env fct
            env, Function(res, body)
        | e -> env, e

    let rec renTopLevelBody (env: Env) = function
        | Function(fct, body) ->
            let env = env.onEnterFunction env body
            let env, args = renList env (renDecl false) fct.args
            let _env, body = renStmt env body
            Function({fct with args=args}, body)
        | e -> e

    let renameTopLevel li env =
        // Rename top-level values first
        let env = dontRenameList env options.noRenamingList
        let env, li = renList env renTopLevelName li

        // Then, rename local values
        List.map (renTopLevelBody env) li

    // Compute table of variables names, based on frequency
    let computeFrequencyIdentTable text =
        let charCounts = Seq.countBy id text |> dict
        let count c = let ok, res = charCounts.TryGetValue(c) in if ok then res else 0
        let letters = ['a'..'z']@['A'..'Z']

        // First, use most frequent letters
        let oneLetterIdentifiers = letters |> List.sortBy count |> List.rev |> List.map string

        // Then, generate identifiers with 2 letters
        let twoLettersIdentifiers =
            [for c1 in letters do
             for c2 in letters do
             yield c1.ToString() + c2.ToString()]
            |> List.sortByDescending (fun s -> count s.[0] + count s.[1])

        Array.ofList (oneLetterIdentifiers @ twoLettersIdentifiers)

    // TODO: rename should take a list of ASTs (not just one).
    let rename shader =
        // First rename: give a unique id to each variable.
        let numberOfUsedIdents = ref 0
        let env1 = Env.Create([], false, alwaysNewName numberOfUsedIdents, fun env _ -> env)
        let code = renameTopLevel shader.code env1

        // Get data about the context
        let text = Printer.printText code
        let identTable = computeFrequencyIdentTable text
        let contextTable = computeContextTable text
        
        // Second rename: use the context.
        let idents = identTable |> Array.toList
                  |> List.filter (fun x -> not <| List.exists ((=) x) shader.forbiddenNames)
        let env2 = Env.Create(idents, true, optimizeContext contextTable, shadowVariables)
        renameTopLevel code env2

    let renameAll shaders =
        for shader in shaders do
            shader.code <- rename shader
        shaders

let rename = RenamerImpl.renameAll
