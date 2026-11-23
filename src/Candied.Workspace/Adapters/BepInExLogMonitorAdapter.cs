using System.Runtime.CompilerServices;
using System.Text;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.Workspace.Parsing;

namespace Candied.Workspace.Adapters;

internal sealed class BepInExLogMonitorAdapter : ILogMonitor
{
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

            var effectiveMax = maxLines <= 0 ? 200 : maxLines;
            var tailLines = lines.Count > effectiveMax
                ? lines.Skip(lines.Count - effectiveMax).ToList()
                : lines;

            var entries = BepInExLogParser.ParseLines(tailLines);
            return Result.Ok<IReadOnlyList<LogEntry>, LogMonitorError>(entries);
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

    public async IAsyncEnumerable<LogEntry> StreamLogsAsync(
        string logFilePath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(logFilePath) || !File.Exists(logFilePath))
        {
            yield break;
        }

        using var fileStream = new FileStream(
            logFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fileStream, Encoding.UTF8);

        // Position at the end of the file for live streaming
        fileStream.Seek(0, SeekOrigin.End);

        BepInExLogParser.LogEntryBuilder? current = null;

        while (!ct.IsCancellationRequested)
        {
            string? line = null;
            try
            {
                line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }

            if (line == null)
            {
                if (current != null)
                {
                    yield return current.Build();
                    current = null;
                }

                try
                {
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                continue;
            }

            var (isHeader, _, newBuilder) = BepInExLogParser.TryParseHeader(line);
            if (isHeader)
            {
                if (current != null)
                {
                    yield return current.Build();
                }

                current = newBuilder;
            }
            else
            {
                if (current != null)
                {
                    current.AppendContinuation(line);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    current = new BepInExLogParser.LogEntryBuilder(DateTimeOffset.UtcNow, LogLevel.Info, "Unknown", line.Trim());
                }
            }
        }
    }

    internal static List<LogEntry> ParseLines(IReadOnlyList<string> lines) => BepInExLogParser.ParseLines(lines);
}
