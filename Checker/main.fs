open OpenTK.Graphics.OpenGL
open Options.Globals
open System
open System.Diagnostics
open System.IO

open Argu

type CliArguments =
    | Update_Golden
    | Skip_GLSL_Compile
    | Skip_Performance_Tests

    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Update_Golden _ -> "Update the golden tests"
            | Skip_GLSL_Compile _ -> "Skip the GLSL compilation of shaders"
            | Skip_Performance_Tests _ -> "Skip the tests of performance"

let cliArgs = ArgumentParser.Create<CliArguments>().ParseCommandLine()
//let cliArgs = ArgumentParser.Create<CliArguments>().ParseCommandLine([|"--update-golden"|])

let initOpenTK () =
    // OpenTK requires a GameWindow
    if not (cliArgs.Contains(Skip_GLSL_Compile)) then
        new OpenTK.GameWindow() |> ignore

// Return true if the file can be compiled as a GLSL shader.
let canBeCompiled content =
    if cliArgs.Contains(Skip_GLSL_Compile) then
        true
    else
        let fragmentShader = GL.CreateShader(ShaderType.FragmentShader)
        GL.ShaderSource(fragmentShader, content)
        GL.CompileShader(fragmentShader)
        let info = GL.GetShaderInfoLog(fragmentShader)
        GL.DeleteShader(fragmentShader)
        if info = "" then
            true
        else
            printfn "compilation failed: %s" info
            false

let testFloatToS () =
    let input = "1. 10. 100. 1000. 10000. 1000000. 1e+123 0.1 0.01 0.001 0.0001 0.000001 1e-123 -1. -10. -100. -1000. -10000. -1000000. -1e+123 -0.1 -0.01 -0.001 -0.0001 -0.000001 -1e-123".Split(" ")
    let expected = "1. 10. 100. 1000. 10000. 1e6 1e123 .1 .01 .001 .0001 1e-6 1e-123 -1. -10. -100. -1000. -10000. -1e6 -1e123 -.1 -.01 -.001 -.0001 -1e-6 -1e-123"
    //let expected = "1. 10. 1e2 1e3 1e4 1e6 1e123 .1 .01 1e-3 1e-4 1e-6 1e-123 -1. -10. -1e2 -1e3 -1e4 -1e6 -1e123 -.1 -.01 -1e-3 -1e-4 -1e-6 -1e-123"
    // TODO: Make a better floatToS.
    let actual = input |> Array.map float
                 |> Array.map Printer.floatToS
    let a = String.Join(" ", actual)
    if a = expected then
        printfn "%s" "Success: floatToS"
        0
    else
        printfn "Failure.\nExpected: %s\nActual:   %s" expected a
        1

let doMinify content =
    let arr = Main.minify [|"input", content|] |> fst |> Array.map (fun s -> s.code)
    Printer.printText arr.[0]

let testMinifyAndCompile (file: string) =
    try
        let content = File.ReadAllText file
        if not (canBeCompiled content) then
            printfn "Invalid input file '%s'" file
            false
        else
            let minified = doMinify content + "\n"
            if not (canBeCompiled minified) then
                printfn "Minification broke the file '%s'" file
                printfn "%s" minified
                false
            else
                printfn "Success: %s" file
                true
    with :? IO.FileNotFoundException as e ->
        printfn "%A" e
        false

let testPerformance files =
    printfn "Running performance tests..."
    let contents = files |> Array.map File.ReadAllText
    let stopwatch = Stopwatch.StartNew()
    for str in contents do
        doMinify str |> ignore
    let time = stopwatch.Elapsed
    printfn "%i files minified in %f seconds." files.Length time.TotalSeconds

let runCommand argv =
    let cleanString (s: string) = s.Replace("\r\n", "\n").Trim()
    if not(options.init(argv)) then failwith "init failed"
    let expected =
        try File.ReadAllText options.outputName |> cleanString
        with _ when cliArgs.Contains(Update_Golden) -> ""
           | _ -> reraise ()
    let result =
        use out = new StringWriter()
        let shaders, exportedNames = Main.minifyFiles options.filenames
        Formatter.print out shaders exportedNames options.outputFormat
        out.ToString() |> cleanString
    if result = expected then
        printfn "Success: %s" options.outputName
        0
    else
        printfn "Fail: %A" argv
        if cliArgs.Contains(Update_Golden) then
            File.WriteAllText(options.outputName, result + "\n")
        else
            printfn "Got %d: %A" result.Length result
            printfn "Expected %d: %A" expected.Length expected
        1

let testGolden () =
    let commands = File.ReadAllLines "tests/commands.txt" |> Array.choose (fun line ->
        let line = line.Trim()
        if line.Length = 0 || line.[0] = '#' then 
            None
        else
            Some (line.Split([|' '|]))
    )
    commands |> Array.sumBy runCommand

[<EntryPoint>]
let main argv =
    //ignore(runCommand("--no-renaming --format c-array -o tests/unit/minus-zero.expected tests/unit/minus-zero.frag".Split([|' '|]))); exit 0
    initOpenTK()

    printfn "%s" "Unit tests:"
    let mutable failures = testFloatToS()

    printfn "%s" "Golden tests:"
    failures <- failures + testGolden()

    printfn "%s" "GL Compile tests:"
    if not(options.init([|"--format"; "text"; "fake.frag"|])) then failwith "init failed"
    let unitTests = Directory.GetFiles("tests/unit", "*.frag")
    let realTests = Directory.GetFiles("tests/real", "*.frag");
    for f in unitTests do
        if not (testMinifyAndCompile f) then
            failures <- failures + 1

    printfn "%s" "Performance tests:"
    testPerformance (Seq.concat [realTests; unitTests] |> Seq.toArray)
    if failures = 0 then
        printfn "All good."
    else
        printfn "%d failures." failures

    //System.Console.ReadLine() |> ignore
    if failures = 0 then 0 else 1
