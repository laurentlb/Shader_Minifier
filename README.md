# Shader Minifier

[![Build status](https://ci.appveyor.com/api/projects/status/9sekrxuiway58su3?svg=true)](https://ci.appveyor.com/project/laurentlb/shader-minifier-r5gmt)

Shader Minifier is a tool that minifies and obfuscates shader code (GLSL and
HLSL) without affecting its behaviour. It is also suitable for reducing the size
of the shaders in other applications (e.g. webgl, games).

Its original use-case is for the
[demoscene](https://en.wikipedia.org/wiki/Demoscene), for optimizing 4k and 64k
intros. In the context of 4kB intros, Shader Minifier help developers maintain
and iterate on human-readable files, while shipping optimized code. Even when a
shader is minified by hand by experienced demosceners, Shader Minifier is often
able to optimize it further. See this [2010
report](https://www.ctrl-alt-test.fr/2010/glsl-minifier-smaller-and-smaller/).

To be notified of new releases, use the watch feature of GitHub.

Try the online version here: https://ctrl-alt-test.fr/minifier/

## Features

- Parsing and printing of GLSL or HLSL code.
- Generation of a file (such as a C header) that can be embedded in an application.
- A command-line interface that can fit in a build.
- [A web interface](https://ctrl-alt-test.fr/minifier/), for interactive workflows.
- Ability to minify multiple shaders at once, in a consistent way.

See the list of [transformations](TRANSFORMATIONS.md) for more information.
In brief:

- Strip spaces, remove comments, remove useless parens.
- Inline functions, variables and constant values.
- Simplify constant expressions: `5. * 2.` becomes `10.`.
- Group declarations: `float a=2.;float b;` becomes `float a=2.,b;`.
- Apply other tricks to reduce the file size.
- Simplify calls to vector constructors using vector swizzles.
- Rename variables, typically to one character.
- Remove unused local variables, unused functions and other dead code.

Other transformations try to make the code more compression friendly, e.g.

- Consistently rename vector fields (e.g. use `foo.xy` instead of `foo.rg`) to
help the compression.
- Reuse the variable names as much as possible: a local variable may have the
same name as a global variable not used in the function; two functions may have
the same name using function overloading.
- Analyze the context and make statistics to compute the variable name that will
be the most compression-friendly.


## Example output

```c
/* File generated with Shader Minifier 1.3
 * http://www.ctrl-alt-test.fr
 */
#ifndef HEART_FRAG_EXPECTED_
# define HEART_FRAG_EXPECTED_
# define VAR_mouse "f"
# define VAR_resolution "y"
# define VAR_time "v"

const char *heart_frag =
 "uniform float v;"
 "uniform vec2 y;"
 "uniform vec4 f;"
 "void main()"
 "{"
   "vec2 f=(2.*gl_FragCoord.xy-y)/y.y;"
   "float r=mod(v,2.)/2.,a=pow(r,.2)*.5+.5;"
   "a-=a*.2*sin(r*6.2831*5.)*exp(-r*6.);"
   "f*=vec2(.5,1.5)+a*vec2(.5,-.5);"
   "float m=atan(f.x,f.y)/3.141593,x=length(f),e=abs(m),o=(13.*e-22.*e*e+10.*e*e*e)/(6.-5.*e),n=step(x,o)*pow(1.-x/o,.25);"
   "gl_FragColor=vec4(n,0.,0.,1.);"
 "}";

#endif // HEART_FRAG_EXPECTED_
```

Multiple output formats are available.

## Usage

Download Shader Minifier here:
  https://github.com/laurentlb/Shader_Minifier/releases

It is a command-line tool. Without argument, it will show the usage. If you are
not on Windows, you will need mono:

```
$ shader_minifier.exe  # Windows
$ mono shader_minifier.exe  # Linux, Mac...
```

```
USAGE: Shader Minifier [--help] [-o <string>] [-v] [--hlsl]
                       [--format <text|indented|c-variables|c-array|js|nasm|rust>]
                       [--field-names <rgba|xyzw|stpq>] [--preserve-externals]
                       [--preserve-all-globals] [--no-inlining]
                       [--aggressive-inlining] [--no-renaming]
                       [--no-renaming-list <string>] [--no-sequence]
                       [--no-remove-unused]
                       [--move-declarations] [<filename>...]

FILENAMES:

    <filename>...         List of files to minify

OPTIONS:

    -o <string>           Set the output filename (default is shader_code.h)
    -v                    Verbose, display additional information
    --hlsl                Use HLSL (default is GLSL)
    --format <text|indented|c-variables|c-array|js|nasm|rust>
                          Choose to format the output (use 'text' if you want
                          just the shader)
    --field-names <rgba|xyzw|stpq>
                          Choose the field names for vectors: 'rgba', 'xyzw',
                          or 'stpq'
    --preserve-externals  Do not rename external values (e.g. uniform)
    --preserve-all-globals
                          Do not rename functions and global variables
    --no-inlining         Do not automatically inline variables, functions
                          and arguments
    --aggressive-inlining Aggressively inline constants. This can reduce output
                          size due to better constant folding. It can also
                          increase output size due to repeated inlined
                          constants, but this increased redundancy can be
                          beneficial to compression, leading to a smaller final
                          compressed size anyway. Does nothing if inlining is
                          disabled.
    --no-renaming         Do not rename anything
    --no-renaming-list <string>
                          Comma-separated list of functions to preserve
    --no-sequence         Do not use the comma operator trick
    --no-remove-unused    Do not remove unused code
    --move-declarations   Move declarations to group them
    --preprocess          Evaluate some of the file preprocessor directives
    --export-kkp-symbol-maps
                          Export kkpView symbol maps
    --help                display this list of options.
```

In short:

* List the shaders you want to minify on the command-line.

* Use `-o` to choose the output file (by default, it will use `shader_code.h`).
  If you pass `-` for the output, it will be printed on stdout.

* Use `--format` to control the output format. By default, it will create a C
  header. There are other options to get only the shader, or have it in a
  Javascript, Rust, or nasm file.

## Tips

### 4kB intros

4kB intros typically use a single shader file. The default flags should work well:

```
shader_minifier.exe -o shader_code.h fragment.glsl
```

We recommend that you frequently check the output of Shader Minifier to make
sure there are no obvious problems. Use [inlining](#inlining) where possible.

If you desperately need to save a few bytes, try another value of
`--field-names`. It can affect the size after compression.

### 64kB intros

The recommandation is to use:

```
shader_minifier.exe --format c-array *.frag -o shaders.h
```

Then, in your C or C++ code, include the file:

```c
const char* shaderSources[] = {
#define SHADER_MINIFIER_IMPL
#include shaders.h
};
```

Note that uniforms will be renamed consistently across all the files. The
`#define` lines will tell you how they were renamed. To disable this renaming,
use `--preserve-externals`.

### Javascript

Use `--format js`. It will define a variable for each shader, the variable name
being derived from the input file. We expect you to run a Javascript minifier on
the output file.

### Other applications

The simplest solution is to minify each file separately:

```
shader_minifier.exe --format text --preserve-externals file.glsl -o file_opt.glsl
```

The output may be used as a drop-in replacement for your original shaders.

### Debugging

To better understand what Shader Minifier changed and get a more readable
output, use the flags `--format indented --no-renaming`. This will add some
indentation to the output, instead of using overly long lines.

## Concepts

### Shader behaviour

Shader Minifer works by applying to the
[AST](https://en.wikipedia.org/wiki/Abstract_syntax_tree) modifications
that produce a transformed but semantically equivalent AST. The
resulting assembly may be different, but the minified shader should
have the same behaviour as the original one. Or at least that's the
intent.

Some of the [transformations](TRANSFORMATIONS.md) are not completely safe and may break in some corner
cases. If you observe differences, don't hesitate to [report a bug](#Feedback).

### Macros

Shader Minifier will preserve the preprocessor directives (the lines starting
with `#`), except that it strips the spaces.

If you define a macro, Shader Minifier will notice the name of the macro and
won't rename the occurrences of the macro. It also doesn't rename variables used
inside the macro. Clever macros are discouraged and can break the shader.

Avoid macros that contain references to other variables, or affect how the
code should be parsed. You may get a parse error in Shader Minifier, or get an
output that won't compile.

### Evaluation of preprocessor directives

With the flag `--preprocess`, Shader Minifier will try to evaluate some
preprocessing directives:

* `#ifdef` is evaluated based on macros defined in the file.
* `#else` is supported.
* Nested `#ifdef` are supported.
* Unknown directives will be preserved.

This is still experimental and there are some limitations:

* Macros declared with `#define` will be kept in the file, even if they are no
    longer needed.
* `#define` and `#undefine` that appear inside a `#if` region are not well
    handled.

### Verbatim

If you want to temporary turn off the minifier, use the `//[` and `//]`
comments. This can be useful as a workaround if you get a parser error.

Variables inside the region won't be renamed. Spaces will be stripped.

```glsl
//[
layout(local_size_x = 32) in;
//]
```

### Overloading

At this time, do not use overloaded functions (two functions with the same name
but different arguments) in the input. The output probably won't compile.

On the other hand, Shader Minifier will aggressively use function overloading in
the output. If two functions have a different number of arguments, they may have
the same name in the output. This reduces the number of identifiers used by the
shader and make it more compression friendly.

### kkpView symbol maps

Shader Minifier can export symbol files that map the minified code back to names
from the original source code. This lets you visualize shader size using
https://github.com/ConspiracyHu/rekkrunchy-with-analytics

### Shader Minifier performance

On my machine, it takes around 1s to minify a shader. A lot of that time comes
from the startup time of the binary. If you have many shaders, try to minify
them all at the same time by listing them all on the command-line.


## Transformations

- [Whitespace and comments removal](TRANSFORMATIONS.md#Whitespace-and-comments-removal)
- [Parentheses simplifications](TRANSFORMATIONS.md#Parentheses-simplifications)
- [Curly braces removal](TRANSFORMATIONS.md#Curly-braces-removal)
- [Literal numbers](TRANSFORMATIONS.md#Literal-numbers)
- [Constant arithmetic](TRANSFORMATIONS.md#Constant-arithmetic)
- [Conditionals evaluation](TRANSFORMATIONS.md#Conditionals-evaluation)
- [Commutative operators](TRANSFORMATIONS.md#Commutative-operators)
- [Comma operator](TRANSFORMATIONS.md#Comma-operator)
- [Useless else after return](TRANSFORMATIONS.md#Useless-else-after-return)
- [Ternary operator](TRANSFORMATIONS.md#Ternary-operator)
- [Loops](TRANSFORMATIONS.md#Loops)
- [Merge declarations](TRANSFORMATIONS.md#Merge-declarations)
- [Group declarations](TRANSFORMATIONS.md#Group-declarations)
- [Augmented operators](TRANSFORMATIONS.md#Augmented-operators)
- [Rename vector fields](TRANSFORMATIONS.md#Rename-vector-fields)
- [Automatic inlining](TRANSFORMATIONS.md#Automatic-inlining)
- [Aggressive inlining](TRANSFORMATIONS.md#Aggressive-inlining)
- [Explicit inlining](TRANSFORMATIONS.md#Explicit-inlining)
- [Vector constructors](TRANSFORMATIONS.md#Vector-constructors)
- [Function predeclarations](TRANSFORMATIONS.md#Function-predeclarations)
- [Unused local variables](TRANSFORMATIONS.md#Unused-local-variables)
- [Dead code removal](TRANSFORMATIONS.md#Dead-code-removal)
- [Unused function removal](TRANSFORMATIONS.md#Unused-function-removal)
- [Renaming](TRANSFORMATIONS.md#Renaming)



## Current limitations

- Macros can arbitrarily change the code. Shader Minifier does not try to
  take into account how the future effects of macro expansion interact
  with minification, and assumes that the source code is valid and can be
  understood and minified before macro expansion. This can result in
  minification breaking the code.

- Function overloading is not well supported yet, in particular when the
  number of arguments is not enough to disambiguate.

- In the context of demoscene, the compressed size of a shader is more
  important than the uncompressed size. As a result, Shader Minifier makes
  some assumptions that favor minimizing the compressed size of the minified
  shader. In some cases, the uncompressed size of the output the can end up
  larger than the input.

- Shader Minifier tries to support GLSL and HLSL. Supporting similar but
  different languages means that it's not always possible to take full
  advantage of all minification possibilities, and that the less often used
  corners of a language might have undetected bugs.


## Feedback

Please give feedback in the [bugtracker](https://github.com/laurentlb/Shader_Minifier/issues).
If something is blocking you, you can file a bug or update an existing bug. We
rely on your feedback to prioritize the work.

Feature requests are encouraged. Add upvotes or comments to existing feature
requests that are important to you.

Contributions are welcome. Please leave a message before implementing a
significant change, so that we can agree on the solution.

---

Created by Laurent Le Brun (LLB / Ctrl-Alt-Test) and
[other contributors](https://github.com/laurentlb/Shader_Minifier/graphs/contributors).

  http://laurent.le-brun.eu &mdash; http://ctrl-alt-test.fr
  