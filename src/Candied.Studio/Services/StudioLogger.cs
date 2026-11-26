using System;
using System.Globalization;
using System.IO;
using System.Text;
using Candied.Vision.Hosting;

namespace Candied.Studio.Services;

internal static class StudioLogger
{
    private static readonly object LockObj = new();

    public static string GetLogsDirectory() => VisionConfigPaths.GetLogsDirectory();

    public static string LogFilePath => VisionConfigPaths.GetStudioLogPath();

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
        }
    }

    public static void Info(string message) => Log("INFO", message);
    public static void Warning(string message) => Log("WARN", message);
    public static void Error(string message, Exception? ex = null) => Log("ERROR", message, ex);
    public static void Fatal(string message, Exception? ex = null) => Log("FATAL", message, ex);
}
