using System;
using System.IO;
using Candied.Core.Ports;
using Candied.Workspace.Adapters;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class StudioSessionManagerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sessionFilePath;

    public StudioSessionManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_SessionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sessionFilePath = Path.Combine(_tempDir, "studio_session.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void GetActiveSession_WhenNoFileExists_ReturnsNull()
    {
        var manager = new DiskStudioSessionManagerAdapter(_sessionFilePath);
        var session = manager.GetActiveSession();
        Assert.Null(session);
    }

    [Fact]
    public void WriteAndReadSession_WithLiveCurrentProcessPid_Succeeds()
    {
        var manager = new DiskStudioSessionManagerAdapter(_sessionFilePath);
        var original = new StudioSessionInfo(
            StudioPid: Environment.ProcessId,
            StudioPort: 12345,
            StudioToken: "test_token_secret",
            ActiveGameName: "TestGame",
            GamePid: 99999,
            BridgePort: 54321,
            BridgeToken: "bridge_token"
        );

        manager.WriteSession(original);

        var retrieved = manager.GetActiveSession();
        Assert.NotNull(retrieved);
        Assert.Equal(Environment.ProcessId, retrieved.StudioPid);
        Assert.Equal(12345, retrieved.StudioPort);
        Assert.Equal("test_token_secret", retrieved.StudioToken);
        Assert.Equal("TestGame", retrieved.ActiveGameName);
    }

    [Fact]
    public void GetActiveSession_WhenPidIsDead_CleansUpStaleSessionFileAndReturnsNull()
    {
        // Requirement 14: "Gestisci sessioni orfane: file di sessione con PID morto = stantio, ripulito, non 'connesso'."
        var manager = new DiskStudioSessionManagerAdapter(_sessionFilePath);

        // Find a dead PID (e.g. 9999999 or non-existent)
        int deadPid = 9999999;
        var staleSession = new StudioSessionInfo(
            StudioPid: deadPid,
            StudioPort: 12345,
            StudioToken: "stale_token"
        );

        manager.WriteSession(staleSession);
        Assert.True(File.Exists(_sessionFilePath));

        // Reading stale session should detect dead PID, remove file, and return null
        var session = manager.GetActiveSession();

        Assert.Null(session);
        Assert.False(File.Exists(_sessionFilePath), "Stale session file must be automatically purged.");
    }

    [Fact]
    public void DeleteSession_RemovesSessionFile()
    {
        var manager = new DiskStudioSessionManagerAdapter(_sessionFilePath);
        manager.WriteSession(new StudioSessionInfo(Environment.ProcessId, 12345, "token"));
        Assert.True(File.Exists(_sessionFilePath));

        manager.DeleteSession();
        Assert.False(File.Exists(_sessionFilePath));
    }
}
