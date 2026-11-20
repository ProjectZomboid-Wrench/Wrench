# Allocations (2026-11-20)

LuaBridge calls through fixed-arity guarded helpers instead of Guard closures and params arrays, pools Temps per thread with a lazily created list, and Wrench.Game.Java/Wrench.Game.Lua convert exact matches and Lua numbers without reflection or boxing. Same machine as `2026-11-20.md`.

## Before

| Method                                | Mean     | Error    | StdDev   | Gen0   | Allocated |
|-------------------------------------- |---------:|---------:|---------:|-------:|----------:|
| Lua -> .NET fn(Color)               | 236.1 ns |  3.30 ns |  3.09 ns | 0.0034 |     184 B |
| event -> handler calling arg.GetR() | 526.2 ns | 10.32 ns | 11.04 ns | 0.0068 |     352 B |

| Method                          | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| table.field (literal key)     | 134.8 ns | 0.23 ns | 0.20 ns |  0.46 | 0.0049 |     248 B |        2.07 |
| table.field = 2 (literal key) | 294.9 ns | 0.32 ns | 0.28 ns |  1.00 | 0.0088 |     448 B |        3.73 |
| color:getR() -> number        | 295.8 ns | 0.91 ns | 0.85 ns |  1.00 | 0.0020 |     120 B |        1.00 |
| GetGlobal("Color")              | 122.8 ns | 0.10 ns | 0.08 ns |  0.42 | 0.0042 |     216 B |        1.80 |

| Method                          | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| color.GetR()                    | 287.1 ns | 1.19 ns | 1.11 ns |  1.00 | 0.0029 |     168 B |        1.00 |
| color.Darker(0.5f) -> wrapper | 522.3 ns | 1.38 ns | 1.29 ns |  1.82 | 0.0039 |     208 B |        1.24 |
| color.ChangeHSBValue(1, 1, 1) | 508.2 ns | 2.57 ns | 2.40 ns |  1.77 | 0.0029 |     184 B |        1.10 |
| list[i] -> wrapper            | 498.6 ns | 1.10 ns | 1.03 ns |  1.74 | 0.0039 |     208 B |        1.24 |

## After

| Method                                | Mean     | Error   | StdDev  | Gen0   | Allocated |
|-------------------------------------- |---------:|--------:|--------:|-------:|----------:|
| Lua -> .NET fn(Color)               | 231.2 ns | 2.98 ns | 2.79 ns | 0.0037 |     184 B |
| event -> handler calling arg.GetR() | 499.1 ns | 3.16 ns | 2.80 ns | 0.0039 |     240 B |

| Method                          | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| table.field (literal key)     | 128.6 ns | 0.06 ns | 0.04 ns |  0.46 | 0.0002 |      24 B |        0.43 |
| table.field = 2 (literal key) | 257.0 ns | 1.47 ns | 1.38 ns |  0.92 | 0.0005 |      48 B |        0.86 |
| color:getR() -> number        | 280.5 ns | 0.72 ns | 0.60 ns |  1.00 | 0.0010 |      56 B |        1.00 |
| GetGlobal("Color")              | 116.6 ns | 0.11 ns | 0.09 ns |  0.42 | 0.0006 |      32 B |        0.57 |

| Method                          | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| color.GetR()                    | 270.7 ns | 0.25 ns | 0.22 ns |  1.00 | 0.0010 |      56 B |        1.00 |
| color.Darker(0.5f) -> wrapper | 522.6 ns | 2.20 ns | 2.06 ns |  1.93 | 0.0020 |     144 B |        2.57 |
| color.ChangeHSBValue(1, 1, 1) | 491.7 ns | 1.75 ns | 1.64 ns |  1.82 | 0.0020 |     120 B |        2.14 |
| list[i] -> wrapper            | 501.0 ns | 1.49 ns | 1.25 ns |  1.85 | 0.0020 |     144 B |        2.57 |
