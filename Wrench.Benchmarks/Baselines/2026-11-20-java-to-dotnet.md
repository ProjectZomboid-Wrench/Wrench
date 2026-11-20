# Java → .NET calls (2026-11-20)

Events and Lua calls into .NET read their arguments from the value buffer, write a function's result back through it, identify events by an int id, build scope origins only when needed, and bind delegates through a compiled invoker cached per method instead of GetParameters and DynamicInvoke. Same machine as `2026-11-20.md`.

## Before

| Method                                  | Mean       | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------------------------- |-----------:|--------:|--------:|------:|-------:|----------:|------------:|
| Lua -> .NET fn(number) -> number      |   658.6 ns | 2.36 ns | 2.09 ns |  1.00 | 0.0117 |     600 B |        1.00 |
| Lua -> .NET fn(Color)                 |   673.0 ns | 5.90 ns | 5.23 ns |  1.02 | 0.0078 |     408 B |        0.68 |
| Lua -> .NET fn(Color) => color.GetR() | 1,557.7 ns | 4.82 ns | 4.51 ns |  2.36 | 0.0156 |     800 B |        1.33 |
| event -> empty mod handler            |   261.6 ns | 0.61 ns | 0.51 ns |  0.40 | 0.0054 |     272 B |        0.45 |
| event -> handler calling arg.GetR()   |   819.1 ns | 2.62 ns | 2.45 ns |  1.24 | 0.0088 |     480 B |        0.80 |

## After

| Method                                  | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| Lua -> .NET fn(number) -> number      | 163.94 ns | 3.140 ns | 3.084 ns |  1.00 |    0.03 | 0.0046 |     240 B |        1.00 |
| Lua -> .NET fn(Color)                 | 239.04 ns | 4.738 ns | 5.267 ns |  1.46 |    0.04 | 0.0037 |     184 B |        0.77 |
| Lua -> .NET fn(Color) => color.GetR() | 761.17 ns | 5.475 ns | 5.122 ns |  4.64 |    0.09 | 0.0078 |     440 B |        1.83 |
| event -> empty mod handler            |  64.13 ns | 0.336 ns | 0.314 ns |  0.39 |    0.01 | 0.0032 |     160 B |        0.67 |
| event -> handler calling arg.GetR()   | 601.91 ns | 2.120 ns | 1.771 ns |  3.67 |    0.07 | 0.0068 |     352 B |        1.47 |
