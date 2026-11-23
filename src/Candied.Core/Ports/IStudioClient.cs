using System.Threading;
using System.Threading.Tasks;
using Candied.Primitives;

namespace Candied.Core.Ports;

/// <summary>
/// Window metrics reported by Candied Studio for visibility and monitor clamping verification.
/// </summary>
public sealed record StudioWindowInfo(
    long Handle,
    bool IsVisible,
    string WindowState,
    string Visibility,
    int RectLeft,
    int RectTop,
    int RectRight,
    int RectBottom,
    int Width,
    int Height,
    bool IsWithinMonitor,
    bool IsResponding
);

/// <summary>
/// Status reported by Candied Studio HTTP server.
/// </summary>
public sealed record StudioStatusInfo(
    int StudioPid,
    bool IsRunning,
    string? ActiveGameName,
    int? GamePid,
    int? BridgePort,
    string? EmbeddingMode,
    bool IsVisionConnected,
    StudioWindowInfo? Window = null,
    int GameWidth = 1024,
    int GameHeight = 720,
    int PanelWidth = 0,
    int PanelHeight = 0
);

/// <summary>
/// Port communicating with Candied Studio via its local HTTP server.
/// </summary>
public interface IStudioClient
{
    Task<StudioSessionInfo?> GetCurrentSessionAsync(CancellationToken ct = default);
    Task<Result<StudioStatusInfo, string>> GetStatusAsync(CancellationToken ct = default);
    Task<Result<StudioSessionInfo, string>> EnsureStudioRunningAsync(CancellationToken ct = default);
    Task<Result<StudioStatusInfo, string>> LaunchGameAsync(string gameName, string executablePath, CancellationToken ct = default);
    Task<Result<bool, string>> CloseGameAsync(CancellationToken ct = default);
}
