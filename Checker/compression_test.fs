﻿module CompressionTests

open System.Runtime.InteropServices
open System.IO
open System.Text

#nowarn "51" // use of native pointers

module Crinkler =
    [<DllImport(@"Compressor.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern void InitCompressor()

    [<DllImport(@"Compressor.dll", CallingConvention = CallingConvention.Cdecl)>]
    extern single ApproximateModels4k(char* data, int datasize)


let testFiles = [
    "from-the-seas-to-the-stars.frag"
    "the_real_party_is_in_your_pocket.frag"
    "ed-209.frag"
    "valley_ball.glsl"
    "lunaquatic.frag"
    "slisesix.frag"
    "yx_long_way_from_home.frag"
    "oscars_chair.frag"
    "kinder_painter.frag"
    "ohanami.frag"
    "terrarium.frag"
    "leizex.frag"
    "elevated.hlsl"
    "buoy.frag"
    "orchard.frag"
    "robin.frag"
    "sult.frag"
    "../unit/loop.frag"
]

let writer = new StringWriter()

let log fmt =
    let logger str =
        printf "%s" str
        writer.Write(str)

    Printf.ksprintf logger fmt

let testFile (file: string) =
    let langArg = if file.EndsWith("hlsl") then [|"--hlsl"|] else [||]
    let extraArgs = [|"--format"; "text"|]
    Options.init(Array.append langArg extraArgs)
    let minified =
        use out = new StringWriter()
        let shaders, exportedNames = ShaderMinifier.minifyFiles [|"tests/real/" + file|]
        Formatter.print out shaders exportedNames Options.Text
        out.ToString().ToCharArray()

    let pointer = &&minified.[0]
    log "%-40s " file
    log "%5d " minified.Length
    let compressedSize = Crinkler.ApproximateModels4k(pointer, minified.Length)
    log "=> %8.3f\n" compressedSize
    minified.Length, float compressedSize

let run () =
    Crinkler.InitCompressor() // Platform must be set to x64

    writer.GetStringBuilder().Clear() |> ignore<StringBuilder>
    let sizes = List.map testFile testFiles
    let minifiedSum = List.sumBy fst sizes
    let compressedSum = List.sumBy snd sizes
    log "Total: %5d => %9.3f\n" minifiedSum compressedSum
    File.WriteAllText("tests/compression_results.log", writer.ToString())
