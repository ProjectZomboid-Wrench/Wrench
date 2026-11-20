# After one-call statics and globals (2026-11-20)

`ILuaHost.CallStatic`/`CallGlobal`/`GetStatic` (Wrench.Api and Wrench.Game.Java 0.1.1) resolve the function in Java through `WrenchLua.invoke<N>`'s modes, with table and function names cached. The three-step rows are the old path, measured in the same run. Same machine as `2026-11-20.md`.

## LuaBridgeSuite

| Method                                             | Mean     | Error   | StdDev  | Gen0   | Allocated |
|--------------------------------------------------- |---------:|--------:|--------:|-------:|----------:|
| Color.HSBtoRGB(...) via GetGlobal + Index + Call | 950.1 ns | 2.77 ns | 2.59 ns | 0.0146 |     744 B |
| Color.HSBtoRGB(...) via CallStatic               | 414.3 ns | 1.09 ns | 0.96 ns | 0.0024 |     128 B |
| benchGlobal() via GetGlobal + Call               | 450.2 ns | 1.27 ns | 1.12 ns | 0.0083 |     424 B |
| benchGlobal() via CallGlobal                     | 225.7 ns | 0.37 ns | 0.33 ns | 0.0022 |     120 B |

## TypedApiSuite

| Method                                        | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|---------------------------------------------- |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| color.GetR()                                  | 347.2 ns | 2.11 ns | 1.87 ns |  1.00 | 0.0029 |     168 B |        1.00 |
| Color.HSBtoRGB(0.5f, 0.5f, 0.5f) -> wrapper | 658.0 ns | 0.96 ns | 0.80 ns |  1.90 | 0.0049 |     272 B |        1.62 |
| new Color(0.5f, 0.25f, 0.125f, 1f)          | 633.0 ns | 1.02 ns | 0.85 ns |  1.82 | 0.0059 |     304 B |        1.81 |
