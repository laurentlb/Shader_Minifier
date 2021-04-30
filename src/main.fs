﻿module Main

open System
open System.IO
open Microsoft.FSharp.Text
open Options.Globals

// Compute table of variables names, based on frequency
let computeFrequencyIdentTable li =
    let str = Printer.quickPrint li

    let charCounts = Seq.countBy id str |> dict
    let count c = let ok, res = charCounts.TryGetValue(c) in if ok then res else 0
    let letters = ['a'..'z']@['A'..'Z']

    // First, use most frequent letters
    let oneLetterIdentifiers = letters |> List.sortBy count |> List.rev |> List.map string

    // Then, generate identifiers with 2 letters
    let score (s:string) = - (count s.[0] + count s.[1])
    let twoLettersIdentifiers =
        [for c1 in letters do
         for c2 in letters do
         yield c1.ToString() + c2.ToString()]
        |> List.sortByDescending (fun s -> count s.[0] + count s.[1])

    Printer.identTable <- Array.ofList (oneLetterIdentifiers @ twoLettersIdentifiers)

let nullOut = new StreamWriter(Stream.Null) :> TextWriter

// like printf when verbose option is set
let vprintf fmt =
    let out = if options.verbose then stdout else Ast.nullOut
    fprintf out fmt

let printSize code =
    if options.verbose then
        printfn "Shader size is: %d" (Printer.quickPrint code).Length

let rename code =
    Printer.printMode <- Printer.SingleChar
    let code = Renamer.renTopLevel code Renamer.Unambiguous
    computeFrequencyIdentTable code
    Renamer.computeContextTable code

    Printer.printMode <- Printer.FromTable
    let code = Renamer.renTopLevel code Renamer.Context
    vprintf "%d identifiers renamed. " Renamer.numberOfUsedIdents
    printSize code
    code

let readFile file =
    let stream =
        if file = "" then new StreamReader(Console.OpenStandardInput())
        else new StreamReader(file)
    stream.ReadToEnd()

let minify(filename, content: string) =
    vprintf "Input file size is: %d\n" (content.Length)
    let code = Parse.runParser filename content
    vprintf "File parsed. "; printSize code

    let code = Rewriter.reorder code

    let code = Rewriter.apply code
    vprintf "Rewrite tricks applied. "; printSize code

    let code =
        if options.noRenaming then code
        else rename code

    vprintf "Minification of '%s' finished.\n" filename
    code

let minifyFile file =
    let content = readFile file
    let filename = if file = "" then "stdin" else file
    minify(filename, content)

let run files =
    let fail (exn:exn) s =
          printfn "%s" s;
          printfn "%s" exn.StackTrace
          1
    try
        let codes = Array.map minifyFile files
        CGen.print (Array.zip files codes) options.targetOutput
        0
    with
        | Failure s as exn -> fail exn s
        | exn -> fail exn exn.Message

let printHeader () =
    printfn "Shader Minifier %s - https://github.com/laurentlb/Shader_Minifier" Options.version
    printfn ""

let () =
    let mutable files = []
    let setFile s = files <- s :: files

    let setFieldNames s =
        if not (options.trySetCanonicalFieldNames s) then
            printfn "'%s' is not a valid value for field-names" s
            printfn "You must use 'rgba', 'xyzw', or 'stpq'."

    let noRenamingFct (s:string) = options.noRenamingList <- [for i in s.Split([|','|]) -> i.Trim()]

    let setFormat = function
        | "c-variables" -> options.targetOutput <- Options.CHeader
        | "js" -> options.targetOutput <- Options.JS
        | "c-array" -> options.targetOutput <- Options.CList
        | "none" -> options.targetOutput <- Options.Text
        | "nasm" -> options.targetOutput <- Options.Nasm
        | s -> printfn "'%s' is not a valid format" s

    let specs =
        ["-o", ArgType.String (fun s -> options.outputName <- s), "Set the output filename (default is shader_code.h)"
         "-v", ArgType.Unit (fun() -> options.verbose<-true), "Verbose, display additional information"
         "--hlsl", ArgType.Unit (fun() -> options.hlsl<-true), "Use HLSL (default is GLSL)"
         "--format", ArgType.String setFormat, "Can be: c-variables (default), c-array, js, nasm, or none"
         "--field-names", ArgType.String setFieldNames, "Choose the field names for vectors: 'rgba', 'xyzw', or 'stpq'"
         "--preserve-externals", ArgType.Unit (fun() -> options.preserveExternals<-true), "Do not rename external values (e.g. uniform)"
         "--preserve-all-globals", ArgType.Unit (fun() -> options.preserveAllGlobals<-true; options.preserveExternals<-true), "Do not rename functions and global variables"
         "--no-renaming", ArgType.Unit (fun() -> options.noRenaming<-true), "Do not rename anything"
         "--no-renaming-list", ArgType.String noRenamingFct, "Comma-separated list of functions to preserve"
         "--no-sequence", ArgType.Unit (fun() -> options.noSequence<-true), "Do not use the comma operator trick"
         "--smoothstep", ArgType.Unit (fun() -> options.smoothstepTrick<-true), "Use IQ's smoothstep trick"
         "--", ArgType.Rest setFile, "Stop parsing command line"
        ] |> List.map ArgInfo

    ArgParser.Parse(specs, setFile)
    files <- List.rev files

    let myExit n =
        if Options.debugMode then System.Console.ReadLine() |> ignore
        exit n

    if files = [] then
        printHeader()
        ArgParser.Usage(specs, usage="Please give the shader files to compress on the command line.")
        myExit 1
    elif List.length files > 1 && not options.preserveExternals then
        printfn "When compressing multiple files, you must use the --preserve-externals option."
        myExit 1
    else
        if options.verbose then printHeader()
        myExit (run (Array.ofList files))
