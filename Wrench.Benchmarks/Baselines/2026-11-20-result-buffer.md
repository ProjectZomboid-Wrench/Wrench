# After reading results from a direct buffer (2026-11-20)

WrenchLua reports each value's kind and bits in a per-thread direct buffer, and returns only strings and objects. Includes everything before it. A subset of the suites; same machine as `2026-11-20.md`.

## LuaBridgeSuite

| Method                                  | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| color:getR() -> number                | 278.1 ns | 1.69 ns | 1.58 ns |  1.00 |    0.01 | 0.0029 |     168 B |        1.00 |
| color:darker(0.5) -> object           | 397.3 ns | 4.51 ns | 3.99 ns |  1.43 |    0.02 | 0.0034 |     176 B |        1.05 |
| color:changeHSBValue(1, 1, 1) -> void | 410.5 ns | 2.13 ns | 2.00 ns |  1.48 |    0.01 | 0.0020 |     112 B |        0.67 |
| color["getR"] (index only)            | 238.0 ns | 1.65 ns | 1.55 ns |  0.86 |    0.01 | 0.0061 |     312 B |        1.86 |
| GetGlobal("Color")                      | 212.6 ns | 1.22 ns | 1.14 ns |  0.76 |    0.01 | 0.0054 |     280 B |        1.67 |

## TypedApiSuite

| Method                          | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| color.GetR()                    | 310.1 ns | 1.12 ns | 0.88 ns |  1.00 | 0.0039 |     216 B |        1.00 |
| color.Darker(0.5f) -> wrapper | 500.9 ns | 1.79 ns | 1.59 ns |  1.62 | 0.0049 |     256 B |        1.19 |
| list[i] -> wrapper            | 529.6 ns | 3.67 ns | 3.43 ns |  1.71 | 0.0049 |     257 B |        1.19 |

## DynamicSuite

| Method                 | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| dynamic color.getR() | 283.4 ns | 4.31 ns | 4.04 ns |  1.00 |    0.02 | 0.0034 |     192 B |        1.00 |
