using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Candied.Core.Models;

namespace Candied.Workspace.Parsing;

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
                var source = match.Groups["source"].Value.Trim();
                var message = match.Groups["message"].Value.Trim();
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

    public static (bool IsHeader, LogEntry? Entry, LogEntryBuilder? NewBuilder) TryParseHeader(string line)
    {
        var match = HeaderRegex().Match(line);
        if (match.Success)
        {
            var level = MapLogLevel(match.Groups["level"].Value.Trim());
            var source = match.Groups["source"].Value.Trim();
            var message = match.Groups["message"].Value.Trim();
            var timestamp = ParseTimestamp(match.Groups["time"].Value);

            var builder = new LogEntryBuilder(timestamp, level, source, message);
            return (true, null, builder);
        }

        return (false, null, null);
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

    public sealed class LogEntryBuilder
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
            var trimmed = line.Trim();
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
                StackTrace: StackTraceBuilder?.ToString().Trim()
            );
        }
    }
}
