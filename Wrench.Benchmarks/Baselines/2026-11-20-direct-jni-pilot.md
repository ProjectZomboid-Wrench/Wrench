# Direct JNI pilot (2026-11-20)

Two typed calls through Kahlua (today's generated wrappers) and through `Wrench.Host`'s `Lua/DirectJava`: a cached method id, then one `CallFloatMethodA` or `CallObjectMethodA` with an exception check. "Wrapped by exact class" builds the `Color` wrapper the way `JavaTypes` does (class-name lookup, then the wrapper); "unwrapped" stops at the game object, which is roughly what a generated wrapper could do for a `final` class such as `Color`, whose exact type is known statically. Same machine as `2026-11-20.md`.

| Method                                              | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------------------------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| color.GetR() through Kahlua                       | 250.61 ns | 2.056 ns | 1.717 ns |  1.00 |    0.01 | 0.0010 |      56 B |        1.00 |
| color.GetR() direct                               |  81.21 ns | 0.026 ns | 0.023 ns |  0.32 |    0.00 |      - |         - |        0.00 |
| color.Darker(0.5f) through Kahlua                 | 486.64 ns | 6.415 ns | 6.864 ns |  1.94 |    0.03 | 0.0024 |     144 B |        2.57 |
| color.Darker(0.5f) direct, wrapped by exact class | 182.53 ns | 0.145 ns | 0.121 ns |  0.73 |    0.00 | 0.0010 |      56 B |        1.00 |
| color.Darker(0.5f) direct, unwrapped              |  94.57 ns | 0.042 ns | 0.033 ns |  0.38 |    0.00 | 0.0006 |      32 B |        0.57 |
