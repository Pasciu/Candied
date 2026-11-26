using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Primitives;
using Candied.Vision.Models;
using Candied.Vision.Protocol;

namespace Candied.Vision.Ports;

public interface IStudioSessionManager
{
    string SessionFilePath { get; }
    StudioSessionInfo? GetActiveSession();
    void WriteSession(StudioSessionInfo session);
    void DeleteSession();
}

public interface IStudioHostClient
{
    Task<StudioSessionInfo?> GetCurrentSessionAsync(CancellationToken ct = default);
    Task<Result<StudioStatusInfo, string>> GetStatusAsync(CancellationToken ct = default);
    Task<Result<StudioSessionInfo, string>> EnsureStudioRunningAsync(CancellationToken ct = default);
    Task<Result<StudioStatusInfo, string>> LaunchTargetAsync(WindowLaunchSpec spec, CancellationToken ct = default);
    Task<Result<bool, string>> CloseTargetAsync(CancellationToken ct = default);
    Task<Result<VisionFrame, VisionError>> CaptureScreenshotAsync(CancellationToken ct = default);
    Task<Result<VisionInputResponse, VisionError>> SendInputAsync(VisionInputRequest request, CancellationToken ct = default);
}

public interface IRecentTargetsStore
{
    Task<IReadOnlyList<RecentTargetEntry>> ListRecentAsync(CancellationToken ct = default);
    Task RecordRecentAsync(RecentTargetEntry entry, CancellationToken ct = default);
}
