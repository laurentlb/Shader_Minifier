﻿module Main

open System
open System.IO
open Microsoft.FSharp.Text
open Options.Globals

let printSize (shaders: Ast.Shader[]) =
    if options.verbose then
        let length = shaders |> Array.map (fun s -> Printer.printText s.code)
                   |> Array.sumBy (fun s -> s.Length)
        printfn "Shader size is: %d" length

let readFile file =
    let stream =
        if file = "" then new StreamReader(Console.OpenStandardInput())
        else new StreamReader(file)
    stream.ReadToEnd()

let minify (files: (string*string)[]) =
    vprintf "Input file size is: %d\n" (files |> Array.sumBy (fun (_, s) -> s.Length))
    let shaders = files |> Array.map (fun (f, c) -> Parse.runParser f c)
    vprintf "File parsed. "; printSize shaders

    for shader in shaders do
        shader.code <- Rewriter.reorder shader.code
        shader.code <- Rewriter.simplify shader.code
    vprintf "Rewrite tricks applied. "; printSize shaders

    if options.noRenaming then
        shaders
    else
        let shaders = Renamer.rename shaders
        vprintf "Identifiers renamed. "; printSize shaders
        shaders

let minifyFiles files =
    let files = files |> Array.map (fun f ->
        let content = readFile f
        let filename = if f = "" then "stdin" else f
        filename, content)
    minify files

let run files =
    let fail (exn:exn) s =
          printfn "%s" s;
          printfn "%s" exn.StackTrace
          1
    use out =
        if Options.debugMode || options.outputName = "" || options.outputName = "-" then stdout
        else new StreamWriter(options.outputName) :> TextWriter
    try
        let codes = minifyFiles files |> Array.map (fun s -> s.code)
        Formatter.print out (Array.zip files codes) options.outputFormat
        0
    with
        | Failure s as exn -> fail exn s
        | exn -> fail exn exn.Message

[<EntryPoint>]
let main argv =
    let err =
        try
            if options.init argv then 
                if options.verbose then
                    printfn "Shader Minifier %s - https://github.com/laurentlb/Shader_Minifier" Options.version
                run options.filenames
            else 1
        with
        | :? Argu.ArguParseException as ex ->
            printfn "%s" ex.Message
            1
    if Options.debugMode then System.Console.ReadLine() |> ignore
    exit err
