using System.IO;
using System.Runtime.CompilerServices;

namespace SomethingNeedDoing.Utils;

/// <summary>
/// A logger that automatically prefixes messages with the calling class name.
/// </summary>
public static class FrameworkLogger
{
    /// <summary>
    /// Dalamud 根層會不會收 Verbose。ECommons 把插件層級設成 Verbose，所以 <c>Svc.Log.MinimumLogLevel</c> 恆真、不能當閘門；
    /// 呼叫端的內插字串在進 <see cref="Verbose"/> 之前就組好了，每幀／每則訊息的路徑要先判這個。
    /// </summary>
    public static bool VerboseEnabled
        => global::Dalamud.EntryPoint.LogLevelSwitch.MinimumLevel <= Serilog.Events.LogEventLevel.Verbose;

    /// <summary>
    /// Logs an informational message with the calling class name as prefix.
    /// </summary>
    /// <param name="message">The message to log.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Info(string message, [CallerFilePath] string path = "")
    {
        Svc.Log.Information($"[{Path.GetFileNameWithoutExtension(path)}] {message}");
    }

    /// <summary>
    /// Logs a debug message with the calling member name as prefix.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="path">The calling member name (automatically provided by the compiler).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Debug(string message, [CallerFilePath] string path = "")
    {
        Svc.Log.Debug($"[{Path.GetFileNameWithoutExtension(path)}] {message}");
    }

    /// <summary>
    /// Logs a verbose message with the calling member name as prefix.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="path">The calling member name (automatically provided by the compiler).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Verbose(string message, [CallerFilePath] string path = "")
    {
        // 根層丟掉的 Verbose 在 Dalamud 那邊仍會先組前綴、建 LogEvent；寫出與否和不判時相同。
        if (!VerboseEnabled)
            return;
        Svc.Log.Verbose($"[{Path.GetFileNameWithoutExtension(path)}] {message}");
    }

    /// <summary>
    /// Logs a warning message with the calling member name as prefix.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="path">The calling member name (automatically provided by the compiler).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Warning(string message, [CallerFilePath] string path = "")
    {
        Svc.Log.Warning($"[{Path.GetFileNameWithoutExtension(path)}] {message}");
    }

    /// <summary>
    /// Logs an error message with the calling member name as prefix.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="path">The calling member name (automatically provided by the compiler).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Error(string message, [CallerFilePath] string path = "")
    {
        Svc.Log.Error($"[{Path.GetFileNameWithoutExtension(path)}] {message}");
    }

    /// <summary>
    /// Logs an error message with an exception and the calling member name as prefix.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="path">The calling member name (automatically provided by the compiler).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Error(string message, Exception exception, [CallerFilePath] string path = "")
    {
        Svc.Log.Error(exception, $"[{Path.GetFileNameWithoutExtension(path)}] {message}");
    }

    public static void Error(Exception ex, string message, [CallerFilePath] string path = "") => Error(message, ex, path);

    /// <summary>
    /// Logs a fatal error message with the calling member name as prefix.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="path">The calling member name (automatically provided by the compiler).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fatal(string message, [CallerFilePath] string path = "")
    {
        Svc.Log.Fatal($"[{Path.GetFileNameWithoutExtension(path)}] {message}");
    }

    /// <summary>
    /// Logs a fatal error message with an exception and the calling member name as prefix.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="path">The calling member name (automatically provided by the compiler).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Fatal(string message, Exception exception, [CallerFilePath] string path = "")
    {
        Svc.Log.Fatal(exception, $"[{Path.GetFileNameWithoutExtension(path)}] {message}");
    }
}
