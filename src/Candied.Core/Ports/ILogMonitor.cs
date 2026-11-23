using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Primitives;

namespace Candied.Core.Ports;

public interface ILogMonitor
{
    Task<Result<IReadOnlyList<LogEntry>, LogMonitorError>> ReadRecentLogsAsync(
        string logFilePath,
        int maxLines = 200,
        CancellationToken ct = default);

    IAsyncEnumerable<LogEntry> StreamLogsAsync(
        string logFilePath,
        CancellationToken ct = default);
}
