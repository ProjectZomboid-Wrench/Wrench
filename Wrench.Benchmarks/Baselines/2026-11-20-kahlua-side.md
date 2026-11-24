# Kahlua side (2026-11-20)

Measured in Java against PZ's real Kahlua (`Color.getR` exposed, 5M iterations, after warm-up):

| Step | Time |
|---|---|
| `KahluaThread.tableget(color, "getR")` | 18 ns |
| `KahluaThread.pcall(invoker, args)` | 77 ns |
| `KahluaThread.call(invoker, args)` | 48 ns |
| `Method.invoke` (reflection) | 5.5 ns |
| direct `color.getR()` | 2.7 ns |
| `tableget` + `pcall` | ~98 ns |
| `WrenchLua.invoke0(METHOD, color, "getR")` before | ~110 ns |
| `WrenchLua.invoke0(METHOD, color, "getR")` after | ~101 ns |

The change: `WrenchLua` encodes `pcall`'s results from offset 1 instead of copying the array to drop the status slot, which takes its own overhead from 10–20 ns to about 4 ns.

Rejected:
- `call(int)` instead of `pcall` (about 30 ns less): it skips `pcall`'s error cleanup (`closeUpvalues`, `cleanCallFrames`, restoring the stack top) and rewraps exceptions as `RuntimeException("<class>: <message>")`, so a Lua error in a call from .NET could leave the coroutine inconsistent.
- Caching method lookups per class (about 13 ns less): Lua mods patch Java methods through `__classmetatables`, and a cache would bypass patches made after the first call.

End to end (same machine as `2026-11-20.md`) the change is within noise:


| Method                        | Mean     | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------------ |---------:|--------:|--------:|------:|-------:|----------:|------------:|
| color:getR() -> number      | 267.1 ns | 1.45 ns | 1.36 ns |  1.00 | 0.0010 |      56 B |        1.00 |
| color:darker(0.5) -> object | 406.7 ns | 2.75 ns | 2.58 ns |  1.52 | 0.0010 |      64 B |        1.14 |

| Method                          | Mean     | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |---------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| color.GetR()                    | 289.9 ns |  0.59 ns |  0.53 ns |  1.00 |    0.00 | 0.0010 |      56 B |        1.00 |
| color.Darker(0.5f) -> wrapper | 539.5 ns | 10.70 ns | 11.45 ns |  1.86 |    0.04 | 0.0020 |     144 B |        2.57 |
