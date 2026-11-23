using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Candied.Core.Ports;

namespace Candied.Workspace.Adapters;

/// <summary>
/// Manages ephemeral Candied Studio session files with automatic stale process cleanup.
/// </summary>
internal sealed class DiskStudioSessionManagerAdapter : IStudioSessionManager
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _sessionFilePath;

    public DiskStudioSessionManagerAdapter(string? customSessionFilePath = null)
    {
        _sessionFilePath = customSessionFilePath ?? CandiedUserConfigPaths.GetStudioSessionPath();
    }

    public string SessionFilePath => _sessionFilePath;

    public StudioSessionInfo? GetActiveSession()
    {
        if (!File.Exists(_sessionFilePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_sessionFilePath);
            var session = JsonSerializer.Deserialize<StudioSessionInfo>(json);
            if (session == null || session.StudioPid <= 0)
            {
                DeleteSession();
                return null;
            }

            // Verify process liveness
            try
            {
                var proc = Process.GetProcessById(session.StudioPid);
                if (proc.HasExited)
                {
                    // Stale session
                    DeleteSession();
                    return null;
                }
            }
            catch (ArgumentException)
            {
                // Process does not exist: stale session
                DeleteSession();
                return null;
            }

            return session;
        }
        catch
        {
            DeleteSession();
            return null;
        }
    }

    public void WriteSession(StudioSessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var dir = Path.GetDirectoryName(_sessionFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(session, SerializerOptions);
        var tempPath = _sessionFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _sessionFilePath, overwrite: true);
    }

    public void DeleteSession()
    {
        try
        {
            if (File.Exists(_sessionFilePath))
            {
                File.Delete(_sessionFilePath);
            }
        }
        catch
        {
            // Ignore file deletion errors during teardown
        }
    }
}
