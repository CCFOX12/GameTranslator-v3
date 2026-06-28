using System.IO;

namespace GameTranslator.Services;

public static class Logger
{
    private static readonly string LogPath;
    private static readonly object _lock = new();

    static Logger()
    {
        LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.log");
    }

    public static void Info(string format, params object[] args) { Write("INFO", format, args); }
    public static void Error(string format, params object[] args) { Write("ERROR", format, args); }
    public static void Warn(string format, params object[] args) { Write("WARN", format, args); }

    private static void Write(string level, string format, params object[] args)
    {
        try
        {
            var message = args.Length > 0 ? string.Format(format, args) : format;
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            lock (_lock) { File.AppendAllText(LogPath, line + Environment.NewLine); }
        }
        catch { }
    }

    public static void Clear() { try { if (File.Exists(LogPath)) File.Delete(LogPath); } catch { } }
    public static string GetPath() => LogPath;
}
