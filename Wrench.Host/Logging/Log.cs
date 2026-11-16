using JNIBinding.Diagnostics;

namespace Wrench.Host.Logging;

/// <summary>
/// Host and mod log output. Lines go to stdout, which PZ's launcher captures in ~/Zomboid/projectzomboid.sh.log;
/// set <c>WRENCH_LOG_LEVEL</c> (Trace, Debug, Info, Warn, Error) to change the threshold.
/// </summary>
internal static class Log
{
    public static LogLevel MinimumLevel { get; set; } =
        Enum.TryParse<LogLevel>(Environment.GetEnvironmentVariable("WRENCH_LOG_LEVEL"), ignoreCase: true, out var level)
            ? level
            : LogLevel.Info;

    public static Logger For(string source) => new(source);

    /// <summary>Routes JNIBinding's <see cref="JniLog"/> output into this log.</summary>
    public static void CaptureJniBinding() =>
        JniLog.Sink = (level, source, message) => Write(source, level switch
        {
            JniLogLevel.Trace => LogLevel.Trace,
            JniLogLevel.Debug => LogLevel.Debug,
            JniLogLevel.Info => LogLevel.Info,
            JniLogLevel.Warn => LogLevel.Warn,
            _ => LogLevel.Error,
        }, message);

    public static void Write(string source, LogLevel level, string message, Exception? exception = null)
    {
        if (level < MinimumLevel)
            return;
        var line = $"{DateTime.Now:HH:mm:ss.fff} [{source}] {level.ToString().ToUpperInvariant()}: {message}";
        Console.Out.WriteLine(exception is null ? line : line + Environment.NewLine + exception);
    }
}

internal sealed class Logger(string source)
{
    public void Debug(string message) => Log.Write(source, LogLevel.Debug, message);

    public void Info(string message) => Log.Write(source, LogLevel.Info, message);

    public void Warn(string message, Exception? exception = null) => Log.Write(source, LogLevel.Warn, message, exception);

    public void Error(string message, Exception? exception = null) => Log.Write(source, LogLevel.Error, message, exception);
}
