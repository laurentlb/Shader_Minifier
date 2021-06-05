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
        varRenames: Map<string, string>
        // Map from a new function name and function arity to the old name.
        funRenames: Map<string, Map<int, string>>
        // List of names that are still available.
        availableNames: string list

        exportedNames: Ast.ExportedName list ref

        // Whether multiple functions can have the same name (but different arity).
        allowOverloading: bool
        // Function that decides a name (and returns the modified Env).
        newName: Env -> Ident -> Env
        // Function called when we enter a function, optionally updates the Env.
        onEnterFunction: Env -> Stmt -> Env
    }
    with
        static member Create(availableNames, allowOverloading, newName, onEnterFunction) = {
            varRenames = Map.empty
            funRenames = Map.empty
            availableNames = availableNames
            exportedNames = ref []
            allowOverloading = allowOverloading
            newName = newName
            onEnterFunction = onEnterFunction
        }

        member this.Rename(id: Ident, newName) =
            let prevName = id.Name
            let names = this.availableNames |> List.filter ((<>) newName)
            id.Rename(newName)
            {this with varRenames = this.varRenames.Add(prevName, newName); availableNames = names}

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

    let newUniqueId (numberOfUsedIdents: int ref) (env: Env) (id: Ident) =
        incr numberOfUsedIdents
        let newName = sprintf "%04d" !numberOfUsedIdents
        env.Rename(id, newName)

    // A renaming strategy that considers how a variable is used. It optimizes the frequency of
    // adjacent characters, which can make the output more compression-friendly. This is best
    // suited when minifying a single file.
    let optimizeContext contextTable env (id: Ident) =
        let cid = char (1000 + int id.Name)
        let newName = chooseIdent contextTable cid env.availableNames
        env.Rename(id, newName)

    // A renaming strategy that always picks the first available name. This optimizes the
    // frequency of a few variables. It also ensures that two identical functions will use the
    // same names for local variables, which can be very important in some multifile scenarios.
    let optimizeNameFrequency (env: Env) (id: Ident) =
        let newName = env.availableNames.Head
        env.Rename(id, newName)

    // A renaming strategy that's bijective: if (and only if) two variables had the same old name,
    // they will the same new name.
    // This leads to a slightly longer output (because lots of names are created, most of them 
    // have two chars). However, this preserves similarities from the input code, so that can be
    // compression-friendly.
    let bijectiveRenaming (allNames: string list) =
        let mutable allNames = allNames
        let d = new HashMultiMap<string, string>(HashIdentity.Structural)
        fun (env: Env) (id: Ident) ->
            match d.TryFind(id.OldName) with
            | Some name ->
                env.Rename(id, name)
            | None ->
                let newName = allNames.Head
                allNames <- allNames.Tail
                d.[id.OldName] <- newName
                env.Rename(id, newName)

    let dontRename (env: Env) (id: Ident) =
        env.Rename(id, id.Name)

    let dontRenameList env names =
        let mutable env = env
        for name in names do env <- dontRename env (Ident name)
        env

    let export env ty (id: Ident) =
        if not (isUniqueId id) then
            env.exportedNames := {ty = ty; name = id.OldName; newName = id.Name} :: !env.exportedNames

    let renFunction env nbArgs (id: Ident) =
        // we're looking for a function name, already used before,
        // but not with the same number of arg, and which is not in options.noRenamingList.
        let isFunctionNameAvailableForThisArity (x: KeyValuePair<string, Map<int,string>>) =
            not (x.Value.ContainsKey nbArgs ||
                    List.exists ((=) x.Key) options.noRenamingList)

        match env.funRenames |> Seq.tryFind isFunctionNameAvailableForThisArity with
        | Some res when env.allowOverloading ->
            // overload an existing function name used with a different arity
            let newName = res.Key
            let funRenames = env.funRenames.Add (res.Key, res.Value.Add(nbArgs, id.Name))
            let env = env.Update(env.varRenames.Add(id.Name, newName), funRenames, env.availableNames)
            id.Rename(newName)
            env
        | _ ->
            // find a new function name
            let prevName = id.Name
            let env = env.newName env id
            let funRenames = env.funRenames.Add (id.Name, Map.empty.Add(nbArgs, prevName))
            let env = env.Update(env.varRenames, funRenames, env.availableNames)
            env

    let renFctName env (f: FunctionType) =
        let isExternal = options.hlsl && f.semantics <> []
        if (isExternal && options.preserveExternals) || options.preserveAllGlobals then
            env, f
        elif List.exists ((=) f.fName.Name) options.noRenamingList then
            env, f
        else
            match env.varRenames.TryFind(f.fName.Name) with
            | Some name -> env, {f with fName = Ident(name)}
            | None ->
                let newEnv = renFunction env (List.length f.args) f.fName
                if isExternal then export env "F" f.fName
                newEnv, f

    let renList env fct li =
        let mutable env = env
        let res = li |> List.map (fun i ->
            let x = fct env i
            env <- fst x
            snd x)
        env, res

    let rec renExpr env =
        let mapper _ = function
            | Var v -> Var (Ident (defaultArg (Map.tryFind v.Name env.varRenames) v.Name))
            | e -> e
        mapExpr (mapEnv mapper id)

    let renDecl isTopLevel env (ty:Type, vars) : Env * Decl =
        let aux (env: Env) (decl: Ast.DeclElt) =
            let ext =
                match ty.typeQ with
                | Some tyQ -> ["in"; "out"; "attribute"; "varying"; "uniform"]
                                |> List.exists (fun s -> tyQ.Contains(s))
                | None -> false
            let isExternal = isTopLevel && (ext || options.hlsl)
            let env =
                if isTopLevel && options.preserveAllGlobals then
                    dontRename env decl.name
                elif not isExternal then
                    env.newName env decl.name
                elif options.preserveExternals then
                    dontRename env decl.name
                else
                    match env.varRenames.TryFind(decl.name.Name) with
                    | Some name ->
                        decl.name.Rename(name)
                        env
                    | None ->
                        let env = env.newName env decl.name
                        export env "" decl.name
                        env

            let init = Option.map (renExpr env) decl.init
            let size = Option.map (renExpr env) decl.size
            env, {decl with size=size; init=init}

        let env, res = renList env aux vars
        env, (ty, res)

    // "Garbage collection": remove names that are not used in the block
    // so that we can reuse them. In other words, this function allows us
    // to shadow global variables in a function.
    let shadowVariables (env: Env) block =
        let d = HashSet()
        let collect mEnv = function
            | Var id as e ->
                if not (mEnv.vars.ContainsKey(id.Name)) then d.Add id.Name |> ignore
                e
            | FunCall(Var id, li) as e ->
                match env.funRenames.TryFind id.Name with
                | Some m -> if not (m.ContainsKey li.Length) then d.Add id.Name |> ignore
                | None -> d.Add id.Name |> ignore
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

    // Compute list of variables names, based on frequency
    let computeListOfNames text =
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

        oneLetterIdentifiers @ twoLettersIdentifiers

    let renameAsts shaders env =
        let mutable env = env
        // First, rename top-level values.
        for shader in shaders do
            let newEnv, code = renList env renTopLevelName shader.code
            env <- newEnv
            shader.code <- code

        // Rename local variables.
        for shader in shaders do
            shader.code <- List.map (renTopLevelBody env) shader.code
        !env.exportedNames

    let assignUniqueIds shaders =
        let numberOfUsedIdents = ref 0
        let mutable env = Env.Create([], false, newUniqueId numberOfUsedIdents, fun env _ -> env)
        renameAsts shaders env

    let rename shaders =
        let exportedNames = assignUniqueIds shaders

        // TODO: combine from all shaders
        let forbiddenNames = (Seq.head shaders).forbiddenNames

        // Compute the list of variable names to use
        let text = [for shader in shaders -> Printer.printText shader.code] |> String.concat "\0"
        let names = computeListOfNames text
                 |> List.filter (fun x -> not <| List.exists ((=) x) forbiddenNames)

        let mutable env =
            if Array.length shaders > 1 then
                Env.Create(names, true, bijectiveRenaming names, shadowVariables)
            else
                let contextTable = computeContextTable text
                Env.Create(names, true, optimizeContext contextTable, shadowVariables)
        env <- dontRenameList env options.noRenamingList
        env.exportedNames := exportedNames
        renameAsts shaders env

let rename = RenamerImpl.rename
