﻿module Rewriter

open System
open System.Collections.Generic
open Ast
open Options.Globals

                                (* ** Rewrite tricks ** *)


let renameField field =
    let transform = function
        | 'r' | 'x' | 's' -> options.canonicalFieldNames.[0]
        | 'g' | 'y' | 't' -> options.canonicalFieldNames.[1]
        | 'b' | 'z' | 'p' -> options.canonicalFieldNames.[2]
        | 'a' | 'w' | 'q' -> options.canonicalFieldNames.[3]
        | c -> failwithf "Internal error: transform('%c')" c
    if Seq.forall (fun c -> Seq.exists ((=) c) "rgba") field ||
        Seq.forall (fun c -> Seq.exists ((=) c) "xyzw") field ||
        Seq.forall (fun c -> Seq.exists ((=) c) "stpq") field
    then
        field |> String.map transform
    else field

// Remove useless spaces in macros
let private stripSpaces str =
    let result = Text.StringBuilder()

    let mutable last = '\n'
    let write c =
        last <- c
        result.Append(c) |> ignore
    let isId c = Char.IsLetterOrDigit c || c = '_' || c = '('
    // hack because we can't remove space in "#define foo (1+1)"

    let mutable space = false
    let mutable wasNewline = false
    for c in str do
        if c = '\n' then
            if not wasNewline then
                write '\n'
            space <- false
            wasNewline <- true

        elif Char.IsWhiteSpace(c) then
            space <- true
            wasNewline <- false
        else
            wasNewline <- false
            if space && isId c && isId last then
                write ' '
            write c
            space <- false

    result.ToString()


let private declsNotToInline (d: DeclElt list) = d |> List.filter (fun x -> not x.name.ToBeInlined)

let private bool = function
    | true -> Var (Ident "true") // Int (1, "")
    | false -> Var (Ident "false") // Int (0, "")

let private inlineFn (declArgs:Decl list) passedArgs bodyExpr =
    let mutable argMap = Map.empty
    for declArg, passedArg in List.zip declArgs passedArgs do
        let declElt = List.exactlyOne (snd declArg)
        argMap <- argMap.Add(declElt.name.Name, passedArg)
    let mapInline _ = function
        | Var iv as ie ->
            match argMap.TryFind iv.Name with
            | Some inlinedExpr -> inlinedExpr
            | _ -> ie
        | ie -> ie
    mapExpr (mapEnv mapInline id) bodyExpr

/// Expression that doesn't need parentheses around it.
let (|NoParen|_|) = function
    | Int _ | Float _ | Dot _ | Var _ | FunCall (Var _, _) | Subscript _ as x -> Some x
    | _ -> None

let rec private simplifyExpr (didInline: bool ref) env = function
    | FunCall(Var v, passedArgs) as e when v.ToBeInlined ->
        match env.fns.TryFind v.Name with
        | None -> e
        | Some ({args = declArgs}, body) ->
            match body with
            | Jump (JumpKeyword.Return, Some bodyExpr)
            | Block [Jump (JumpKeyword.Return, Some bodyExpr)] ->
                didInline.Value <- true
                inlineFn declArgs passedArgs bodyExpr
            // Don't yell if we've done some inlining this pass -- maybe it
            // turned the function into a one-liner, so allow trying again on
            // the next pass. (If it didn't, we'll yell next pass.)
            | _ when didInline.Value -> e
            | _ -> failwithf "Cannot inline %s since it consists of more than a single return" v.Name
    | FunCall(Op "-", [Int (i1, su)]) -> Int (-i1, su)
    | FunCall(Op "-", [FunCall(Op "-", [e])]) -> e
    | FunCall(Op "+", [e]) -> e

    | FunCall(Op ",", [e1; FunCall(Op ",", [e2; e3])]) ->
        FunCall(Op ",", [env.fExpr env (FunCall(Op ",", [e1; e2])); e3])

    | FunCall(Op "-", [x; Float (f, s)]) when f < 0.M ->
        FunCall(Op "+", [x; Float (-f, s)]) |> env.fExpr env
    | FunCall(Op "-", [x; Int (i, s)]) when i < 0 ->
        FunCall(Op "+", [x; Int (-i, s)]) |> env.fExpr env

    // Swap operands to get rid of parentheses
    // x*(y*z) -> y*z*x
    | FunCall(Op "*", [NoParen x; FunCall(Op "*", [y; z])]) ->
        FunCall(Op "*", [FunCall(Op "*", [y; z]); x]) |> env.fExpr env
    // x+(y+z) -> y+z+x
    // x+(y-z) -> y-z+a
    | FunCall(Op "+", [NoParen x; FunCall(Op ("+"|"-") as op, [y; z])]) ->
        FunCall(Op "+", [FunCall(op, [y; z]); x]) |> env.fExpr env
    // x-(y+z) -> x-y-z
    | FunCall(Op "-", [x; FunCall(Op "+", [y; z])]) ->
        FunCall(Op "-", [FunCall(Op "-", [x; y]); z]) |> env.fExpr env
    // x-(y-z) -> x-y+z
    | FunCall(Op "-", [x; FunCall(Op "-", [y; z])]) ->
        FunCall(Op "+", [FunCall(Op "-", [x; y]); z]) |> env.fExpr env

    // Boolean simplifications (let's ignore the suffix)
    | FunCall(Op "<",  [Int (i1, _); Int (i2, _)]) -> bool(i1 < i2)
    | FunCall(Op ">",  [Int (i1, _); Int (i2, _)]) -> bool(i1 > i2)
    | FunCall(Op "<=", [Int (i1, _); Int (i2, _)]) -> bool(i1 <= i2)
    | FunCall(Op ">=", [Int (i1, _); Int (i2, _)]) -> bool(i1 <= i2)
    | FunCall(Op "==", [Int (i1, _); Int (i2, _)]) -> bool(i1 = i2)
    | FunCall(Op "!=", [Int (i1, _); Int (i2, _)]) -> bool(i1 <> i2)

    | FunCall(Op "<", [Float (i1,_); Float (i2,_)]) -> bool(i1 < i2)
    | FunCall(Op ">", [Float (i1,_); Float (i2,_)]) -> bool(i1 > i2)
    | FunCall(Op "<=", [Float (i1,_); Float (i2,_)]) -> bool(i1 <= i2)
    | FunCall(Op ">=", [Float (i1,_); Float (i2,_)]) -> bool(i1 <= i2)
    | FunCall(Op "==", [Float (i1,_); Float (i2,_)]) -> bool(i1 = i2)
    | FunCall(Op "!=", [Float (i1,_); Float (i2,_)]) -> bool(i1 <> i2)

    // Stupid simplifications (they can be useful to simplify rewritten code)
    | FunCall(Op "/", [e; Float (1.M,_)]) -> e
    | FunCall(Op "*", [e; Float (1.M,_)]) -> e
    | FunCall(Op "*", [Float (1.M,_); e]) -> e
    | FunCall(Op "*", [_; Float (0.M,_) as e]) -> e
    | FunCall(Op "*", [Float (0.M,_) as e; _]) -> e
    | FunCall(Op "+", [e; Float (0.M,_)]) -> e
    | FunCall(Op "+", [Float (0.M,_); e]) -> e
    | FunCall(Op "-", [e; Float (0.M,_)]) -> e
    | FunCall(Op "-", [Float (0.M,_); e]) -> FunCall(Op "-", [e])

    // No simplification when numbers have different suffixes
    | FunCall(_, [Int (_, su1); Int (_, su2)]) as e when su1 <> su2 -> e
    | FunCall(_, [Float (_, su1); Float (_, su2)]) as e when su1 <> su2 -> e

    | FunCall(Op "-", [Int (i1, su); Int (i2, _)]) -> Int (i1 - i2, su)
    | FunCall(Op "+", [Int (i1, su); Int (i2, _)]) -> Int (i1 + i2, su)
    | FunCall(Op "*", [Int (i1, su); Int (i2, _)]) -> Int (i1 * i2, su)
    | FunCall(Op "/", [Int (i1, su); Int (i2, _)]) -> Int (i1 / i2, su)
    | FunCall(Op "%", [Int (i1, su); Int (i2, _)]) -> Int (i1 % i2, su)

    | FunCall(Op "-", [Float (0.M,su)]) -> Float (0.M, su)
    | FunCall(Op "-", [Float (f1,su)]) -> Float (-f1, su)
    | FunCall(Op "-", [Float (i1,su); Float (i2,_)]) -> Float (i1 - i2, su)
    | FunCall(Op "+", [Float (i1,su); Float (i2,_)]) -> Float (i1 + i2, su)
    | FunCall(Op "*", [Float (i1,su); Float (i2,_)]) -> Float (i1 * i2, su)
    | FunCall(Op "/", [Float (i1,su); Float (i2,_)]) as e ->
        let div = Float (i1 / i2, su)
        if (Printer.exprToS e).Length <= (Printer.exprToS div).Length then e
        else div

    // iq's smoothstep trick: http://www.pouet.net/topic.php?which=6751&page=1#c295695
    | FunCall(Var var, [Float (0.M,_); Float (1.M,_); _]) as e when var.Name = "smoothstep" -> e
    | FunCall(Var var, [a; b; x]) when var.Name = "smoothstep" && options.smoothstepTrick ->
        let sub1 = FunCall(Op "-", [x; a])
        let sub2 = FunCall(Op "-", [b; a])
        let div  = FunCall(Op "/", [sub1; sub2]) |> mapExpr env
        FunCall(Var (Ident "smoothstep"),  [Float (0.M,""); Float (1.M,""); div])

    | Dot(e, field) when options.canonicalFieldNames <> "" -> Dot(e, renameField field)

    | Var s as e ->
        match env.vars.TryFind s.Name with
        | Some (_, {name = id; init = Some init}) when id.ToBeInlined ->
            didInline.Value <- true
            init |> mapExpr env
        | _ -> e

    // pi is acos(-1), pi/2 is acos(0)
    | Float(f, _) when float f = 3.141592653589793 -> FunCall(Var (Ident "acos"), [Float (-1.M, "")])
    | Float(f, _) when float f = 6.283185307179586 -> FunCall(Op "*", [Float (2.M, ""); FunCall(Var (Ident "acos"), [Float (-1.M, "")])])
    | Float(f, _) when float f = 1.5707963267948966 -> FunCall(Var (Ident "acos"), [Float (0.M, "")])

    | e -> e

// Squeeze declarations: "float a=2.; float b;" => "float a=2.,b;"
let rec private squeezeDeclarations = function
    | []-> []
    | Decl(ty1, li1) :: Decl(ty2, li2) :: l when ty1 = ty2 ->
        squeezeDeclarations (Decl(ty1, li1 @ li2) :: l)
    | e::l -> e :: squeezeDeclarations l

// Squeeze top-level declarations, e.g. uniforms
let rec private squeezeTLDeclarations = function
    | []-> []
    | TLDecl(ty1, li1) :: TLDecl(ty2, li2) :: l when ty1 = ty2 ->
        squeezeTLDeclarations (TLDecl(ty1, li1 @ li2) :: l)
    | e::l -> e :: squeezeTLDeclarations l

let private rwTypeSpec = function
    | TypeName n -> TypeName (stripSpaces n)
    | x -> x // structs

let rwType (ty: Type) =
    makeType (rwTypeSpec ty.name) (List.map stripSpaces ty.typeQ) ty.arraySizes

let rwFType fct =
    // The default for function parameters is "in", we don't need it.
    let rwFTypeType ty = {ty with typeQ = List.except ["in"] ty.typeQ}
    let rwFDecl (ty, elts) = (rwFTypeType ty, elts)
    {fct with args = List.map rwFDecl fct.args}

let private simplifyStmt = function
    | Block [] as e -> e
    | Block b ->
        // Remove dead code after return/break/...
        let endOfCode = Seq.tryFindIndex (function Jump _ -> true | _ -> false) b
        let b = match endOfCode with None -> b | Some x -> b |> Seq.truncate (x+1) |> Seq.toList

        // Remove inner empty blocks
        let b = b |> List.filter (function Block [] | Decl (_, []) -> false | _ -> true)
        
        // Try to remove blocks by using the comma operator
        let returnExp = b |> Seq.tryPick (function Jump(JumpKeyword.Return, e) -> e | _ -> None)
        let canOptimize = b |> List.forall (function
            | Expr _ -> true
            | Jump(JumpKeyword.Return, Some _) -> true
            | _ -> false)

        if not options.noSequence && canOptimize then
            let li = List.choose (function Expr e -> Some e | _ -> None) b
            match returnExp with
            | None ->
                if li.IsEmpty then Block []
                else Expr (List.reduce (fun acc x -> FunCall(Op ",", [acc;x])) li)
            | Some e ->
               let expr = List.reduce (fun acc x -> FunCall(Op ",", [acc;x])) (li@[e])
               Jump(JumpKeyword.Return, Some expr)
        else
            match squeezeDeclarations b with
            | [stmt] -> stmt
            | stmts -> Block stmts
    | Decl (ty, li) -> Decl (rwType ty, declsNotToInline li)
    | ForD((ty, d), cond, inc, body) -> ForD((rwType ty, declsNotToInline d), cond, inc, body)
    // FIXME: properly handle booleans
    | If(Var var, e1, _) when var.Name = "true" -> e1
    | If(Var var, _, Some e2) when var.Name = "false" -> e2
    | If(Var var, _, None) when var.Name = "false" -> Block []
    | If(c, b, Some (Block [])) -> If(c, b, None)
    | Verbatim s -> Verbatim (stripSpaces s)
    | e -> e

let rec iterateSimplifyAndInline li =
    if not options.noInlining then
        let mapExpr _ e = e
        let mapStmt = function
            | Block b as e -> Analyzer.findInlinable b; e
            | e -> e
        mapTopLevel (mapEnv mapExpr mapStmt) li |> ignore
    let didInline = ref false
    let simplified = mapTopLevel (mapEnv (simplifyExpr didInline) simplifyStmt) li
    if didInline.Value then iterateSimplifyAndInline simplified else simplified

let simplify li =
    li
    // markLValues doesn't change the AST so we could do it unconditionally,
    // but we only need the information for aggroInlining so don't bother if
    // it's off.
    |> Analyzer.markLValues
    |> if options.aggroInlining then Analyzer.inlineAllConsts else id
    |> iterateSimplifyAndInline
    |> List.choose (function
        | TLDecl (ty, li) -> TLDecl (rwType ty, declsNotToInline li) |> Some
        | TLVerbatim s -> TLVerbatim (stripSpaces s) |> Some
        | Function (fct, _) when fct.fName.ToBeInlined -> None
        | Function (fct, body) -> Function (rwFType fct, body) |> Some
        | e -> e |> Some
    )
    |> squeezeTLDeclarations

          (* Reorder functions because of forward declarations *)


type CallGraphNode = {
    func: TopLevel
    fName: Ident
    callees: string list
}

let rec private findRemove callback = function
    | node :: l when node.callees.IsEmpty ->
        //printfn "=> %s" name
        callback node
        l
    | [] -> failwith "Cannot reorder functions (probably because of a recursion)."
    | x :: l -> x :: findRemove callback l

// slow, but who cares?
let private graphReorder nodes =
    let mutable list = []
    let mutable lastName = ""

    let rec loop nodes =
        let nodes = findRemove (fun node -> lastName <- node.fName.Name; list <- node.func :: list) nodes
        let nodes = nodes |> List.map (fun n -> { n with callees = List.filter ((<>) lastName) n.callees })
        if nodes <> [] then loop nodes

    if nodes <> [] then loop nodes
    list |> List.rev


// get the list of external values the block depends on
let private computeDependencies block =
    let d = HashSet()
    let collect mEnv = function
        | Var id as e ->
            if not (mEnv.vars.ContainsKey(id.Name)) then d.Add id.Name |> ignore
            e
        | e -> e
    mapStmt (mapEnv collect id) block |> ignore
    d |> Seq.toList

// This function assumes that functions are NOT overloaded
let private computeAllDependencies code =
    let fct = code |> List.choose (function
        | Function(fct, block) as f -> Some (fct.fName, block, f)
        | _ -> None)
    let nodes = fct |> List.map (fun (name, block, f) ->
        let callees = computeDependencies block
                      |> List.filter (fun name -> fct |> List.exists (fun (x,_,_) -> name = x.Name))
        { CallGraphNode.func = f; fName = name; callees = callees })
    nodes

let removeUnused code =
    let nodes = computeAllDependencies code
    let isUnused name =
        name <> "main" &&
            not options.preserveExternals &&
            not options.preserveAllGlobals &&
            not (options.noRenamingList |> List.contains name) &&
            not (nodes |> List.exists (fun n -> n.callees |> List.contains name))
    let unused = nodes |> List.filter (fun node -> isUnused node.fName.Name)
    code |> List.filter (function
        | Function _ as t -> not (unused |> List.map (fun node -> node.func) |> List.contains t)
        | _ -> true)

// reorder functions if there were forward declarations
let reorder code =
    if options.verbose then
        printfn "Reordering functions because of forward declarations."
    let order = code |> computeAllDependencies |> graphReorder
    let rest = code |> List.filter (function Function _ -> false | _ -> true)
    rest @ order
