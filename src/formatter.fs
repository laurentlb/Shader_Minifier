﻿module Formatter

open System
open System.IO
open Options.Globals

// Values to export in the C code (uniform and attribute values)
let mutable private exportedValues = ([] : (string * string * string) list)

let reset () = exportedValues <- []

// 'ty' is a prefix for the type of shader param. Nothing (VAR) for vars, "F" for hlsl functions
let export ty name (newName:string) =
    if newName.[0] <> '0' then
        exportedValues <- exportedValues |> List.map (fun (ty2, name2, newName2 as arg) ->
            if ty = ty2 && name = newName2 then ty, name2, newName
            else arg
        )
    else
        exportedValues <- (ty, name, newName) :: exportedValues

let private printHeader out (shaders: Ast.Shader[]) asAList =
    let fileName =
        if options.outputName = "" || options.outputName = "-" then "shader_code.h"
        else Path.GetFileName options.outputName
    let macroName = fileName.Replace(".", "_").ToUpper() + "_"

    fprintfn out "/* File generated with Shader Minifier %s" Options.version
    fprintfn out " * http://www.ctrl-alt-test.fr"
    fprintfn out " */"

    if not asAList then
        fprintfn out "#ifndef %s" macroName
        fprintfn out "# define %s" macroName

    for ty, name, newName in List.sort exportedValues do
        // let newName = Printer.identTable.[int newName]
        if ty = "" then
            fprintfn out "# define VAR_%s \"%s\"" (name.ToUpper()) newName
        else
            fprintfn out "# define %c_%s \"%s\"" (System.Char.ToUpper ty.[0]) (name.ToUpper()) newName

    fprintfn out ""
    for shader in shaders do
        let name = (Path.GetFileName shader.filename).Replace(".", "_")
        if asAList then
            fprintfn out "// %s" shader.filename
            fprintfn out "\"%s\"," (Printer.print shader.code)
        else
            fprintfn out "const char *%s =%s \"%s\";" name Environment.NewLine (Printer.print shader.code)
        fprintfn out ""

    if not asAList then fprintfn out "#endif // %s" macroName

let private printNoHeader out (shaders: Ast.Shader[]) =
    let str = [for shader in shaders -> Printer.print shader.code] |> String.concat "\n"
    fprintf out "%s" str

let private printJSHeader out (shaders: Ast.Shader[]) =
    fprintfn out "/* File generated with Shader Minifier %s" Options.version
    fprintfn out " * http://www.ctrl-alt-test.fr"
    fprintfn out " */"

    for ty, name, newName in List.sort exportedValues do
        if ty = "" then
            fprintfn out "var var_%s = \"%s\"" (name.ToUpper()) newName
        else
            fprintfn out "var %c_%s = \"%s\"" (System.Char.ToUpper ty.[0]) (name.ToUpper()) newName

    fprintfn out ""
    for shader in shaders do
        let name = (Path.GetFileName shader.filename).Replace(".", "_")
        fprintfn out "var %s = `%s`" name (Printer.print shader.code)
        fprintfn out ""

let private printNasmHeader out (shaders: Ast.Shader[]) =
    fprintfn out "; File generated with Shader Minifier %s" Options.version
    fprintfn out "; http://www.ctrl-alt-test.fr"

    for ty, name, newName in List.sort exportedValues do
        if ty = "" then
            fprintfn out "_var_%s: db '%s', 0" (name.ToUpper()) newName
        else
            fprintfn out "_%c_%s: db '%s', 0" (System.Char.ToUpper ty.[0]) (name.ToUpper()) newName

    fprintfn out ""
    for shader in shaders do
        let name = (Path.GetFileName shader.filename).Replace(".", "_")
        fprintfn out "_%s:%s\tdb '%s', 0" name Environment.NewLine (Printer.print shader.code)
        fprintfn out ""

let print out shaders = function
    | Options.Text -> printNoHeader out shaders
    | Options.CHeader -> printHeader out shaders false
    | Options.CList -> printHeader out shaders true
    | Options.JS -> printJSHeader out shaders
    | Options.Nasm -> printNasmHeader out shaders
