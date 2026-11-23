namespace Candied.Core.Ports;

/// <summary>
/// Information regarding an active Candied Studio process session.
/// </summary>
public sealed record StudioSessionInfo(
    int StudioPid,
    int StudioPort,
    string StudioToken,
    string? ActiveGameName = null,
    int? GamePid = null,
    int? BridgePort = null,
    string? BridgeToken = null
);

/// <summary>
/// Port managing the ephemeral Studio session file outside the repository.
/// </summary>
public interface IStudioSessionManager
{
    string SessionFilePath { get; }
    StudioSessionInfo? GetActiveSession();
    void WriteSession(StudioSessionInfo session);
    void DeleteSession();
}
