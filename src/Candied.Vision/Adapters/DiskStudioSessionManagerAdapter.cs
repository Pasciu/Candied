using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Candied.Vision.Hosting;
using Candied.Vision.Models;
using Candied.Vision.Ports;

namespace Candied.Vision.Adapters;

internal sealed class DiskStudioSessionManagerAdapter : IStudioSessionManager
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _sessionFilePath;

    public DiskStudioSessionManagerAdapter(string? customSessionFilePath = null)
    {
        _sessionFilePath = customSessionFilePath ?? VisionConfigPaths.GetStudioSessionPath();
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
            string json = File.ReadAllText(_sessionFilePath);
            var session = JsonSerializer.Deserialize<StudioSessionInfo>(json);
            if (session == null || session.StudioPid <= 0)
            {
                DeleteSession();
                return null;
            }

            try
            {
                var proc = Process.GetProcessById(session.StudioPid);
                if (proc.HasExited)
                {
                    DeleteSession();
                    return null;
                }
            }
            catch (ArgumentException)
            {
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

        string? dir = Path.GetDirectoryName(_sessionFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string json = JsonSerializer.Serialize(session, SerializerOptions);
        string tempPath = _sessionFilePath + ".tmp";
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
        }
    }
}
