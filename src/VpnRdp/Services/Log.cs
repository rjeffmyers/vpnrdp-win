namespace VpnRdp.Services;

/// <summary>Tiny append-only file logger. One file per day under %LOCALAPPDATA%\VpnRdp\logs.</summary>
public static class Log
{
    private static readonly object Gate = new();

    public static event Action<string>? LineWritten;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path.Combine(AppPaths.LogDir, $"vpnrdp-{DateTime.Now:yyyyMMdd}.log"),
                    line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
        LineWritten?.Invoke(line);
    }
}
