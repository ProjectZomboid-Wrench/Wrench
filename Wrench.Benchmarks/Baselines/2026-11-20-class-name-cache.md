# After caching class names (2026-11-20)

`WrenchClasses` ids with the host's `JavaClassNames` cache; the suites it affects. Same machine as `2026-11-20.md`.

## JniSuite

| Method                                                 | Mean      | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------------------------------------------------- |----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| static int call, raw function pointer                |  54.64 ns |  0.172 ns |  0.161 ns |  1.00 |    0.00 |      - |         - |          NA |
| static int call, JNIEnv (delegate + exception check) |  79.18 ns |  0.350 ns |  0.327 ns |  1.45 |    0.01 |      - |         - |          NA |
| ExceptionCheck                                         |  17.96 ns |  0.056 ns |  0.049 ns |  0.33 |    0.00 |      - |         - |          NA |
| NewString + DeleteLocalRef                           |  87.14 ns |  0.650 ns |  0.608 ns |  1.59 |    0.01 |      - |         - |          NA |
| box double + DeleteLocalRef                          | 109.70 ns |  0.324 ns |  0.303 ns |  2.01 |    0.01 | 0.0004 |      24 B |          NA |
| TryUnbox, Double                                     | 115.57 ns |  0.626 ns |  0.585 ns |  2.11 |    0.01 | 0.0004 |      24 B |          NA |
| TryUnbox, game object (miss)                         | 217.73 ns |  2.146 ns |  2.007 ns |  3.98 |    0.04 |      - |         - |          NA |
| class name, uncached                                 | 797.64 ns | 23.790 ns | 64.319 ns | 14.60 |    1.17 | 0.0020 |     136 B |          NA |
| class name (GameObject.TypeName)                     |  88.08 ns |  0.187 ns |  0.175 ns |  1.61 |    0.01 |      - |         - |          NA |

## TypedApiSuite

| Method                           | Mean       | Error   | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|--------------------------------- |-----------:|--------:|--------:|------:|--------:|-------:|----------:|------------:|
| color.GetR()                     |   899.5 ns | 6.62 ns | 8.61 ns |  1.00 |    0.01 | 0.0068 |     376 B |        1.00 |
| color.Darker(0.5f) -> wrapper  | 1,424.9 ns | 6.16 ns | 5.77 ns |  1.58 |    0.02 | 0.0078 |     464 B |        1.23 |
| color.ChangeHSBValue(1, 1, 1)  | 1,353.1 ns | 3.23 ns | 3.02 ns |  1.50 |    0.01 | 0.0117 |     648 B |        1.72 |
| list.Count                       |   896.6 ns | 2.04 ns | 1.81 ns |  1.00 |    0.01 | 0.0068 |     377 B |        1.00 |
| list[i] -> wrapper             | 1,371.1 ns | 6.45 ns | 5.03 ns |  1.52 |    0.02 | 0.0078 |     465 B |        1.24 |
| foreach over list, per element | 1,404.4 ns | 9.78 ns | 8.67 ns |  1.56 |    0.02 | 0.0078 |     470 B |        1.25 |

## JavaToDotnetSuite

| Method                                  | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|---------------------------------------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| Lua -> .NET fn(number) -> number      |   806.9 ns |  3.08 ns |  2.73 ns |  1.00 |    0.00 | 0.0117 |     600 B |        1.00 |
| Lua -> .NET fn(Color)                 |   818.2 ns |  3.65 ns |  3.24 ns |  1.01 |    0.01 | 0.0078 |     408 B |        0.68 |
| Lua -> .NET fn(Color) => color.GetR() | 2,305.8 ns |  5.62 ns |  5.26 ns |  2.86 |    0.01 | 0.0195 |    1008 B |        1.68 |
| event -> empty mod handler            | 1,048.2 ns |  2.12 ns |  1.88 ns |  1.30 |    0.00 | 0.0059 |     344 B |        0.57 |
| event -> handler calling arg.GetR()   | 2,180.0 ns | 11.23 ns | 10.51 ns |  2.70 |    0.02 | 0.0117 |     760 B |        1.27 |
