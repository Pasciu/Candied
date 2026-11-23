using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Core.UseCases;

public sealed class ReadLogsUseCase
{
    private readonly ILogMonitor _logMonitor;

    public ReadLogsUseCase(ILogMonitor logMonitor)
    {
        _logMonitor = logMonitor;
    }

    public async Task<Result<IReadOnlyList<LogEntry>, DomainError>> ExecuteAsync(
        string logFilePath,
        int maxLines = 200,
        LogLevel? minLevel = null,
        CancellationToken ct = default)
    {
        var result = await _logMonitor.ReadRecentLogsAsync(logFilePath, maxLines, ct).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return Result.Fail<IReadOnlyList<LogEntry>, DomainError>(result.Error);
        }

        var entries = result.Value;
        if (minLevel.HasValue)
        {
            entries = entries.Where(e => e.Level >= minLevel.Value).ToList();
        }

        return Result.Ok<IReadOnlyList<LogEntry>, DomainError>(entries);
    }
}
