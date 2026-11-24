# After cached method names and fixed-arity calls (2026-11-20)

Method names are cached global refs, and calls with up to 4 arguments use `WrenchLua.call<N>`/`callMethod<N>` with numbers as raw doubles. Includes the earlier class-name cache and function pointers. A subset of the suites; same machine as `2026-11-20.md`.

## LuaBridgeSuite

| Method                                  | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| color:getR() -> number                | 425.4 ns | 0.94 ns | 0.88 ns |  1.00 | 0.0029 |     168 B |        1.00 |
| color:darker(0.5) -> object           | 583.4 ns | 1.50 ns | 1.40 ns |  1.37 | 0.0029 |     176 B |        1.05 |
| color:changeHSBValue(1, 1, 1) -> void | 456.1 ns | 1.27 ns | 1.13 ns |  1.07 | 0.0024 |     136 B |        0.81 |

## TypedApiSuite

| Method                          | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| color.GetR()                    | 447.1 ns | 1.97 ns | 1.84 ns |  1.00 | 0.0039 |     216 B |        1.00 |
| color.Darker(0.5f) -> wrapper | 732.9 ns | 1.35 ns | 1.20 ns |  1.64 | 0.0049 |     256 B |        1.19 |
| color.ChangeHSBValue(1, 1, 1) | 456.1 ns | 1.37 ns | 1.28 ns |  1.02 | 0.0049 |     256 B |        1.19 |

## DynamicSuite

| Method                      | Mean     | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------- |---------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| dynamic color.getR()      | 416.1 ns | 2.81 ns | 2.35 ns |  1.00 |    0.01 | 0.0034 |     192 B |        1.00 |
| dynamic color.darker(0.5) | 592.2 ns | 9.78 ns | 9.15 ns |  1.42 |    0.02 | 0.0039 |     232 B |        1.21 |
