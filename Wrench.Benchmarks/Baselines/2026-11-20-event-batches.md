# Batched events (2026-11-20)

1000 squares (1 in 10 solid), each fired as `LoadGridsquare`-style events. "Batch" opens and closes a batch boundary around them, as the advice on `IsoChunk.doLoadGridsquare` does, and the handler gets one `LuaEventBatch`. "Where in Java" runs `isSolidTrans` on every square in one host call through `GameObjectBatch.Where`. Times are per square. Same machine as `2026-11-20.md`.

| Method                                          | Mean      | Error    | StdDev   | Gen0   | Allocated |
|------------------------------------------------ |----------:|---------:|---------:|-------:|----------:|
| square per event, isSolidTrans from C#        | 290.81 ns | 2.048 ns | 1.915 ns | 0.0039 |     216 B |
| square batch, isSolidTrans from C# per square | 255.81 ns | 1.516 ns | 1.419 ns | 0.0015 |      88 B |
| square batch, Where(isSolidTrans) in Java     |  77.19 ns | 0.162 ns | 0.143 ns |      - |         - |
