module Printer

open System
open System.Text.RegularExpressions
open Ast
open Options.Globals

module private PrinterImpl =

    let mutable outputFormat = Options.Text

    let out a = sprintf a

    let precedenceList = [
        [","]
        ["="; "+="; "-="; "*="; "/="; "%="; "<<="; ">>="; "&="; "^="; "|="] // precedence = 1
        ["?:"]
        ["||"]
        ["^^"]
        ["&&"]
        ["|"]
        ["^"]
        ["&"]
        ["=="; "!="]
        ["<"; ">"; "<="; ">="]
        ["<<"; ">>"]
        ["+"; "-"]
        ["*"; "/"; "%"]
        // _++ is prefix and $++ is postfix
        ["_++"; "_--"; "_+"; "_-"; "_~"; "_!"; "$++"; "$--"]
        ["."]
    ]

    let precedence =
        precedenceList
        |> List.mapi (fun k li -> List.map (fun op -> op, k) li)
        |> List.concat
        |> dict

    let idToS (id: Ident) =
        // In mode Unambiguous, ids contain numbers. We print a single unicode char instead.
        if id.IsUniqueId then
            string (char (1000 + int id.Name))
        else id.Name

    let commaListToS toS li =
        List.map toS li |> String.concat ","

    let floatToS f =
        let si = if f < 0. then "-" else ""
        let test = out "%g" (abs f)
        // %g outputs 1e-06 instead of 1e-6
        if test.Contains("e") then si + Regex.Replace(test.Replace("e+", "e"), "^(.*e[-]?)?(0+)(.*)$", "$1$3")
        // display "3." instead of "3"
        elif fst (System.Int32.TryParse test) then (out "%g." f)
        // display ".5" instead of "0.5"
        else if test.[0] = '0' then si + (test.[1..])
        else si + test

    let rec exprToS exp = exprToSLevel 0 exp

    // Convert Expr option to string, with default value.
    and exprToSOpt def exp =
        defaultArg (Option.map exprToS exp) def

    and exprToSLevel level = function
        | Int (i, suf) -> (out "%d%s" i suf)
        | Float (f, suf) -> out "%s%s" (floatToS f) suf
        | Var s -> idToS s
        | Op s -> s
        | FunCall(f, args) ->
            match f, args with
            | Op "?:", [a1; a2; a3] ->
                let prec = precedence.["?:"]
                let res = out "%s?%s:%s" (exprToSLevel prec a1) (exprToSLevel prec a2) (exprToSLevel prec a3)
                if prec < level then out "(%s)" res else res
            // Function calls.
            | Var op, _ ->
                // We set level to 1 in case in case a comma operator is used in the argument list.
                out "%s(%s)" (idToS op) (commaListToS (exprToSLevel 1) args)
            
            // Unary operators. _++ is prefix and $++ is postfix
            | Op op, [a1] when op.[0] = '$' -> out "%s%s" (exprToSLevel precedence.[op] a1) op.[1..]
            | Op op, [a1] -> out "%s%s" op (exprToSLevel precedence.["_" + op] a1)

            // Binary operators.
            | Op op, [a1; a2] ->
                let prec = precedence.[op]
                let res =
                    if prec = 1 then // "=", "+=", or other operator with right-associativity
                        out "%s%s%s" (exprToSLevel (prec+1) a1) op (exprToSLevel prec a2)
                    else
                        out "%s%s%s" (exprToSLevel prec a1) op (exprToSLevel (prec+1) a2)
                if prec < level then out "(%s)" res
                else res
            | _ -> out "%s(%s)" (exprToS f) (commaListToS exprToS args)
        | Subscript(arr, ind) ->
            out "%s[%s]" (exprToS arr) (exprToSOpt "" ind)
        | Cast(id, e) ->
            // Cast seems to have the same precedence as unary minus
            out "(%s)%s" id.Name (exprToSLevel precedence.["_-"] e)
        | VectorExp(li) ->
            out "{%s}" (commaListToS exprToS li)
        | Dot(e, field) ->
            out "%s.%s" (exprToSLevel precedence.["."] e) field

    // Add a space if needed
    let sp (s: string) =
        if s.Length > 0 && System.Char.IsLetterOrDigit (s.[0]) then " " + s
        else s

    let sp2 (s: string) (s2: string) =
        if s.Length > 0 && System.Char.IsLetterOrDigit(s.[s.Length-1]) &&
            s2.Length > 0 && System.Char.IsLetterOrDigit(s2.[0]) then s + " " + s2
        else s + s2

    let backslashN() =
        match outputFormat with
        | Options.Text | Options.JS -> "\n"
        | Options.Nasm -> "', 10, '"
        | _ ->  "\\n"

    // Print HLSL semantics
    let semToS sem =
        let res = sem |> List.map exprToS |> String.concat ":"
        if res = "" then res else ":" + res

    let rec structToS prefix id decls =
        let name = match id with None -> "" | Some (s: Ident) -> " " + s.Name
        let d = decls |> List.map (fun s -> declToS s + ";") |> String.concat ""
        out "%s{%s}" (sp2 prefix name) d

    and typeSpecToS = function
        | TypeName s -> s
        | TypeStruct(prefix, id, decls) -> structToS prefix id decls

    and typeToS (ty: Type) =
        let get = Option.fold (fun _ s -> s + " ") ""
        let typeSpec = typeSpecToS ty.name
        out "%s%s" (get ty.typeQ) typeSpec

    and declToS (ty, vars) =
        let out1 decl =
            let size =
                match decl.size with
                | None -> ""
                | Some (Int (0, _)) -> "[]"
                | Some n -> out "[%s]" (exprToS n)

            let init =
                match decl.init with
                | None -> ""
                | Some i -> out "=%s" (exprToS i)
            out "%s%s%s%s" (idToS decl.name) size (semToS decl.semantics) init

        if vars.IsEmpty then ""
        else out "%s %s" (typeToS ty) (vars |> commaListToS out1)

    let mutable ignoreFirstNewLine = true
    let nl indent = // newline and optionally indent
        if ignoreFirstNewLine then
            ignoreFirstNewLine <- false
            ""
        else
            let spaces = new string(' ', indent * 2 + 1)
            match outputFormat with
            | Options.IndentedText -> Environment.NewLine + new string(' ', indent * 2)
            | Options.Text | Options.JS -> ""
            | Options.CHeader | Options.CList -> out "\"%s%s\"" Environment.NewLine spaces
            | Options.Nasm -> out "'%s\tdb%s'" Environment.NewLine spaces

    let escape (s: string) =
        match outputFormat with
        | Options.IndentedText -> s
        | Options.Text -> s
        | Options.JS -> s
        | Options.CHeader | Options.CList | Options.JS -> s.Replace("\"", "\\\"").Replace("\n", "\\n")
        | Options.Nasm -> s.Replace("'", "\'").Replace("\n", "', 10, '")

    let rec stmtToS' indent = function
        | Block [] -> ";"
        | Block b ->
            let body = List.map (stmtToS (indent+1)) b |> String.concat ""
            out "{%s%s}" body (nl indent)
        | Decl (_, []) -> ""
        | Decl d -> out "%s;" (declToS d)
        | Expr e -> out "%s;" (exprToS e)
        | If(cond, th, el) ->
            let el = match el with
                     | None -> ""
                     | Some el -> out "%s%s%s%s" (nl indent) "else" (nl (indent+1)) (stmtToS' (indent+1) el |> sp)
            out "if(%s)%s%s" (exprToS cond) (stmtToSInd indent th) el
        | ForD(init, cond, inc, body) ->
            let cond = exprToSOpt "" cond
            let inc = exprToSOpt "" inc
            out "%s(%s;%s;%s)%s" "for" (declToS init) cond inc (stmtToSInd indent body)
        | ForE(init, cond, inc, body) ->
            let cond = exprToSOpt "" cond
            let inc = exprToSOpt "" inc
            let init = exprToSOpt "" init
            out "%s(%s;%s;%s)%s" "for" init cond inc (stmtToSInd indent body)
        | While(cond, body) ->
            out "%s(%s)%s" "while" (exprToS cond) (stmtToSInd indent body)
        | DoWhile(cond, body) ->
            out "%s%s%s(%s)" "do" "while" (exprToS cond |> sp) (stmtToS indent body)
        | Jump(k, None) -> out "%s;" (jumpKeywordToString k)
        | Jump(k, Some exp) -> out "%s%s;" (jumpKeywordToString k) (exprToS exp |> sp)
        | Verbatim s ->
            // add a space at end when it seems to be needed
            let s = if System.Char.IsLetterOrDigit s.[s.Length - 1] then s + " " else s
            if s <> "" && s.[0] = '#' then out "%s%s" (backslashN()) (escape s)
            else escape s

    and stmtToS indent i =
        out "%s%s" (nl indent) (stmtToS' indent i)

    // print indented statement
    and stmtToSInd indent i = stmtToS (indent+1) i

    let funToS (f: FunctionType) =
        out "%s %s(%s)%s" (typeToS f.retType) (idToS f.fName) (commaListToS declToS f.args) (semToS f.semantics)

    let topLevelToS = function
        | TLVerbatim s ->
            // add a space at end when it seems to be needed
            let s = if System.Char.IsLetterOrDigit s.[s.Length - 1] then s + " " else s
            out "%s%s" (nl 0) (escape s)
        | Function (fct, Block []) -> out "%s%s%s{}" (nl 0) (funToS fct) (nl 0)
        | Function (fct, (Block _ as body)) -> out "%s%s%s" (nl 0) (funToS fct) (stmtToS 0 body)
        | Function (fct, body) -> out "%s%s%s{%s%s}" (nl 0) (funToS fct) (nl 0) (stmtToS 1 body) (nl 0)
        | TLDecl (_, []) -> ""
        | TLDecl decl -> out "%s%s;" (nl 0) (declToS decl)
        | TypeDecl t -> out "%s;" (typeSpecToS t)

    let print tl =
        let mutable wasMacro = true
        // handle the required \n before a macro
        ignoreFirstNewLine <- true
        let f x =
            let isMacro = match x with TLVerbatim s -> s <> "" && s.[0] = '#' | _ -> false
            let needEndLine = isMacro && not wasMacro
            wasMacro <- isMacro
            if needEndLine then out "%s%s" (backslashN()) (topLevelToS x)
            else topLevelToS x

        tl |> List.map f |> String.concat ""

let floatToS = PrinterImpl.floatToS

let print tl = 
    PrinterImpl.outputFormat <- options.outputFormat
    PrinterImpl.print tl

let printText tl =
    PrinterImpl.outputFormat <- Options.Text
    PrinterImpl.print tl
   
let exprToS x =
    PrinterImpl.outputFormat <- Options.Text
    PrinterImpl.exprToS x

let typeToS ty =
    PrinterImpl.outputFormat <- Options.Text
    PrinterImpl.typeToS ty
