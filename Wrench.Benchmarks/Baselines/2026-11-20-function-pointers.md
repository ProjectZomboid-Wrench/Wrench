# After function pointers and UTF-16 strings (2026-11-20)

JNIBinding calls JNI through `delegate* unmanaged` fields, and `AsString`/`AsJString` copy UTF-16 directly. Includes the class-name cache. A subset of the suites; same machine as `2026-11-20.md`.

## JniSuite

| Method                                           | Mean       | Error     | StdDev    | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------------------------------- |-----------:|----------:|----------:|------:|-------:|----------:|------------:|
| static int call, raw function pointer          |  54.346 ns | 0.1005 ns | 0.0940 ns |  1.00 |      - |         - |          NA |
| static int call, Functions field               |  55.472 ns | 0.1440 ns | 0.1277 ns |  1.02 |      - |         - |          NA |
| static int call, JNIEnv (with exception check) |  59.057 ns | 0.1479 ns | 0.1383 ns |  1.09 |      - |         - |          NA |
| ExceptionCheck                                   |   5.203 ns | 0.0186 ns | 0.0174 ns |  0.10 |      - |         - |          NA |
| NewString + DeleteLocalRef                     |  56.297 ns | 0.2078 ns | 0.1944 ns |  1.04 |      - |         - |          NA |
| class name, uncached                           | 130.950 ns | 0.5093 ns | 0.4515 ns |  2.41 | 0.0010 |      56 B |          NA |

## TypedApiSuite

| Method                          | Mean       | Error   | StdDev  | Ratio | Gen0   | Allocated | Alloc Ratio |
|-------------------------------- |-----------:|--------:|--------:|------:|-------:|----------:|------------:|
| color.GetR()                    |   685.3 ns | 3.45 ns | 3.23 ns |  1.00 | 0.0068 |     376 B |        1.00 |
| color.Darker(0.5f) -> wrapper | 1,061.2 ns | 2.89 ns | 2.41 ns |  1.55 | 0.0078 |     464 B |        1.23 |

## JavaToDotnetSuite

| Method                                | Mean       | Error    | StdDev  | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------------------- |-----------:|---------:|--------:|------:|--------:|-------:|----------:|------------:|
| Lua -> .NET fn(number) -> number    |   661.7 ns |  2.67 ns | 2.37 ns |  1.00 |    0.00 | 0.0117 |     600 B |        1.00 |
| event -> empty mod handler          |   256.0 ns |  1.38 ns | 1.29 ns |  0.39 |    0.00 | 0.0054 |     272 B |        0.45 |
| event -> handler calling arg.GetR() | 1,133.5 ns | 10.11 ns | 9.46 ns |  1.71 |    0.02 | 0.0137 |     688 B |        1.15 |
