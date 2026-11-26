using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Platforms.Unity.Shared.Contracts;
using Candied.Primitives;

namespace Candied.Platforms.Unity.Shared.Internal;

internal static partial class BepInExLogParser
{
    [GeneratedRegex(
        @"^(?:\[?(?<time>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?|\d{2}:\d{2}:\d{2}(?:\.\d+)?)\]?\s*)?\[(?<level>[A-Za-z]+)\s*:\s*(?<source>[^\]]+)\]\s*(?<message>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HeaderRegex();

    public static List<LogEntry> ParseLines(IReadOnlyList<string> lines)
    {
        var entries = new List<LogEntry>();
        LogEntryBuilder? current = null;

        foreach (var line in lines)
        {
            var match = HeaderRegex().Match(line);
            if (match.Success)
            {
                if (current != null)
                {
                    entries.Add(current.Build());
                }

                var level = MapLogLevel(match.Groups["level"].Value.Trim());
                string source = match.Groups["source"].Value.Trim();
                string message = match.Groups["message"].Value.Trim();
                var timestamp = ParseTimestamp(match.Groups["time"].Value);

                current = new LogEntryBuilder(timestamp, level, source, message);
            }
            else
            {
                if (current != null)
                {
                    current.AppendContinuation(line);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    current = new LogEntryBuilder(DateTimeOffset.UtcNow, LogLevel.Info, "Unknown", line.Trim());
                }
            }
        }

        if (current != null)
        {
            entries.Add(current.Build());
        }

        return entries;
    }

    public static LogLevel MapLogLevel(string levelStr)
    {
        return levelStr.ToUpperInvariant() switch
        {
            "DEBUG" => LogLevel.Debug,
            "INFO" => LogLevel.Info,
            "MESSAGE" => LogLevel.Info,
            "WARNING" => LogLevel.Warning,
            "WARN" => LogLevel.Warning,
            "ERROR" => LogLevel.Error,
            "FATAL" => LogLevel.Fatal,
            _ => LogLevel.Info
        };
    }

    public static DateTimeOffset ParseTimestamp(string timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr))
        {
            return DateTimeOffset.UtcNow;
        }

        if (DateTimeOffset.TryParse(timeStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto;
        }

        if (TimeSpan.TryParse(timeStr, CultureInfo.InvariantCulture, out var ts))
        {
            var today = DateTimeOffset.UtcNow.Date;
            return new DateTimeOffset(today.Add(ts), TimeSpan.Zero);
        }

        return DateTimeOffset.UtcNow;
    }

    internal sealed class LogEntryBuilder
    {
        public DateTimeOffset Timestamp { get; }
        public LogLevel Level { get; }
        public string Source { get; }
        public StringBuilder MessageBuilder { get; } = new();
        public StringBuilder? StackTraceBuilder { get; private set; }

        public LogEntryBuilder(DateTimeOffset timestamp, LogLevel level, string source, string message)
        {
            Timestamp = timestamp;
            Level = level;
            Source = source;
            MessageBuilder.Append(message);
        }

        public void AppendContinuation(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("at ", StringComparison.Ordinal) ||
                trimmed.StartsWith("---", StringComparison.Ordinal) ||
                trimmed.Contains("Exception", StringComparison.OrdinalIgnoreCase) ||
                StackTraceBuilder != null)
            {
                StackTraceBuilder ??= new StringBuilder();
                StackTraceBuilder.AppendLine(line);
            }
            else
            {
                MessageBuilder.AppendLine().Append(line);
            }
        }

        public LogEntry Build()
        {
            return new LogEntry(
                Timestamp: Timestamp,
                Level: Level,
                Source: Source,
                Message: MessageBuilder.ToString().Trim(),
                StackTrace: StackTraceBuilder?.ToString().Trim());
        }
    }
}

internal sealed class BepInExRuntimeLogCapability : IUnityRuntimeLogReader
{
    public string? ResolveDefaultLogPath(TargetDescriptor target)
    {
        string root = Directory.Exists(target.TargetPath)
            ? target.TargetPath
            : (Path.GetDirectoryName(target.TargetPath) ?? string.Empty);

        return string.IsNullOrEmpty(root)
            ? null
            : Path.Combine(root, "BepInEx", "LogOutput.log");
    }

    public async Task<Result<IReadOnlyList<LogEntry>, LogMonitorError>> ReadRecentLogsAsync(
        string logFilePath,
        int maxLines = 200,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
        {
            return Result.Fail<IReadOnlyList<LogEntry>, LogMonitorError>(
                new LogMonitorError("Log file path cannot be empty."));
        }

        if (!File.Exists(logFilePath))
        {
            return Result.Fail<IReadOnlyList<LogEntry>, LogMonitorError>(
                new LogMonitorError($"Log file does not exist: {logFilePath}"));
        }

        try
        {
            var lines = new List<string>();
            using (var fileStream = new FileStream(
                logFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(fileStream, Encoding.UTF8))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) != null)
                {
                    lines.Add(line);
                }
            }

            int effectiveMax = maxLines <= 0 ? 200 : maxLines;
            var tailLines = lines.Count > effectiveMax
                ? lines.Skip(lines.Count - effectiveMax).ToList()
                : lines;

            return Result.Ok<IReadOnlyList<LogEntry>, LogMonitorError>(BepInExLogParser.ParseLines(tailLines));
        }
        catch (OperationCanceledException)
        {
            return Result.Fail<IReadOnlyList<LogEntry>, LogMonitorError>(
                new LogMonitorError("Log reading operation was cancelled."));
        }
        catch (Exception ex)
        {
            return Result.Fail<IReadOnlyList<LogEntry>, LogMonitorError>(
                new LogMonitorError($"Failed to read log file: {ex.Message}"));
        }
    }
}
