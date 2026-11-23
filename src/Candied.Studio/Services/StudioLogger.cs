using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Candied.Studio.Services;

/// <summary>
/// Thread-safe logger writing startup events and fatal exceptions to %APPDATA%/Candied/logs/studio.log.
/// </summary>
internal static class StudioLogger
{
    private static readonly object LockObj = new();

    public static string GetLogsDirectory()
    {
        string? overrideDir = Environment.GetEnvironmentVariable("CANDIED_USER_CONFIG_DIR");
        string baseDir;
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            baseDir = overrideDir;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }
            baseDir = Path.Combine(appData, "Candied");
        }

        string logDir = Path.Combine(baseDir, "logs");
        if (!Directory.Exists(logDir))
        {
            Directory.CreateDirectory(logDir);
        }
        return logDir;
    }

    public static string LogFilePath => Path.Combine(GetLogsDirectory(), "studio.log");

    public static void Log(string level, string message, Exception? ex = null)
    {
        try
        {
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
            string line = $"[{timestamp}] [{level}] [PID:{Environment.ProcessId}] {message}";
            if (ex != null)
            {
                line += Environment.NewLine + ex;
            }

            lock (LockObj)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never crash the application
        }
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Warning(string message) => Log("WARN", message);
    public static void Error(string message, Exception? ex = null) => Log("ERROR", message, ex);
    public static void Fatal(string message, Exception? ex = null) => Log("FATAL", message, ex);
}
