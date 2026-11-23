using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Ports;
using Candied.Primitives;

namespace Candied.Workspace.Adapters;

/// <summary>
/// Communicates with Candied Studio over local HTTP and manages detached process lifecycle.
/// </summary>
internal sealed class HttpStudioClientAdapter : IStudioClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IStudioSessionManager _sessionManager;
    private readonly HttpClient _httpClient;
    private bool _isDisposed;

    public HttpStudioClientAdapter(IStudioSessionManager sessionManager, HttpClient? httpClient = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task<StudioSessionInfo?> GetCurrentSessionAsync(CancellationToken ct = default)
    {
        var session = _sessionManager.GetActiveSession();
        if (session == null)
        {
            return null;
        }

        // Quick probe to ensure HTTP server is actually responding
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{session.StudioPort}/status");
            req.Headers.Add("X-Candied-Studio-Token", session.StudioToken);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            var resp = await _httpClient.SendAsync(req, cts.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _sessionManager.DeleteSession();
                return null;
            }

            return session;
        }
        catch
        {
            _sessionManager.DeleteSession();
            return null;
        }
    }

    public async Task<Result<StudioStatusInfo, string>> GetStatusAsync(CancellationToken ct = default)
    {
        var session = await GetCurrentSessionAsync(ct).ConfigureAwait(false);
        if (session == null)
        {
            return Result.Fail<StudioStatusInfo, string>("Candied Studio non è in esecuzione.");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{session.StudioPort}/status");
            req.Headers.Add("X-Candied-Studio-Token", session.StudioToken);

            var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return Result.Fail<StudioStatusInfo, string>($"Studio ha restituito codice HTTP {resp.StatusCode}.");
            }

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var status = JsonSerializer.Deserialize<StudioStatusDto>(json, JsonOptions);
            if (status == null)
            {
                return Result.Fail<StudioStatusInfo, string>("Impossibile deserializzare lo stato di Studio.");
            }

            StudioWindowInfo? winInfo = null;
            if (status.Window != null)
            {
                winInfo = new StudioWindowInfo(
                    Handle: status.Window.Handle,
                    IsVisible: status.Window.IsVisible,
                    WindowState: status.Window.WindowState,
                    Visibility: status.Window.Visibility,
                    RectLeft: status.Window.RectLeft,
                    RectTop: status.Window.RectTop,
                    RectRight: status.Window.RectRight,
                    RectBottom: status.Window.RectBottom,
                    Width: status.Window.Width,
                    Height: status.Window.Height,
                    IsWithinMonitor: status.Window.IsWithinMonitor,
                    IsResponding: status.Window.IsResponding
                );
            }

            return Result.Ok<StudioStatusInfo, string>(new StudioStatusInfo(
                StudioPid: session.StudioPid,
                IsRunning: status.IsRunning,
                ActiveGameName: status.ActiveGameName,
                GamePid: status.GamePid,
                BridgePort: status.BridgePort,
                EmbeddingMode: status.EmbeddingMode,
                IsVisionConnected: status.IsVisionConnected,
                Window: winInfo,
                GameWidth: status.GameWidth > 0 ? status.GameWidth : 1024,
                GameHeight: status.GameHeight > 0 ? status.GameHeight : 720,
                PanelWidth: status.PanelWidth,
                PanelHeight: status.PanelHeight
            ));
        }
        catch (Exception ex)
        {
            return Result.Fail<StudioStatusInfo, string>($"Errore di connessione a Studio: {ex.Message}");
        }
    }

    public async Task<Result<StudioSessionInfo, string>> EnsureStudioRunningAsync(CancellationToken ct = default)
    {
        var existingSession = await GetCurrentSessionAsync(ct).ConfigureAwait(false);
        if (existingSession != null)
        {
            return Result.Ok<StudioSessionInfo, string>(existingSession);
        }

        // Locate Studio executable
        string? studioExe = FindStudioExecutable();
        if (string.IsNullOrEmpty(studioExe) || !File.Exists(studioExe))
        {
            return Result.Fail<StudioSessionInfo, string>(
                "Impossibile individuare l'eseguibile di Candied Studio (Candied.Studio.exe). Compilare la soluzione con `dotnet build`.");
        }

        // Start Studio as detached independent process (survives MCP shutdown)
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo
            {
                FileName = studioExe,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = Path.GetDirectoryName(studioExe) ?? Directory.GetCurrentDirectory()
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = studioExe,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(studioExe) ?? Directory.GetCurrentDirectory()
            };
        }

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return Result.Fail<StudioSessionInfo, string>("Process.Start ha restituito null per Candied Studio.");
            }
        }
        catch (Exception ex)
        {
            return Result.Fail<StudioSessionInfo, string>($"Avvio di Candied Studio fallito: {ex.Message}");
        }

        // Poll for Studio session initialization (up to 15 seconds)
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 15000 && !ct.IsCancellationRequested)
        {
            await Task.Delay(300, ct).ConfigureAwait(false);

            var session = await GetCurrentSessionAsync(ct).ConfigureAwait(false);
            if (session != null)
            {
                return Result.Ok<StudioSessionInfo, string>(session);
            }
        }

        return Result.Fail<StudioSessionInfo, string>(
            "Timeout nell'attesa dell'inizializzazione del server HTTP di Candied Studio.");
    }

    public async Task<Result<StudioStatusInfo, string>> LaunchGameAsync(
        string gameName,
        string executablePath,
        CancellationToken ct = default)
    {
        var ensureRes = await EnsureStudioRunningAsync(ct).ConfigureAwait(false);
        if (ensureRes.IsFailure)
        {
            return Result.Fail<StudioStatusInfo, string>(ensureRes.Error);
        }

        var session = ensureRes.Value;

        try
        {
            var body = JsonSerializer.Serialize(new
            {
                gameName,
                executablePath
            });

            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{session.StudioPort}/launch");
            req.Headers.Add("X-Candied-Studio-Token", session.StudioToken);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            var content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                return Result.Fail<StudioStatusInfo, string>($"Errore avvio gioco da Studio ({resp.StatusCode}): {content}");
            }

            var status = JsonSerializer.Deserialize<StudioStatusDto>(content);
            if (status == null)
            {
                return Result.Fail<StudioStatusInfo, string>("Risposta di avvio non valida da Studio.");
            }

            return Result.Ok<StudioStatusInfo, string>(new StudioStatusInfo(
                StudioPid: session.StudioPid,
                IsRunning: status.IsRunning,
                ActiveGameName: status.ActiveGameName,
                GamePid: status.GamePid,
                BridgePort: status.BridgePort,
                EmbeddingMode: status.EmbeddingMode,
                IsVisionConnected: status.IsVisionConnected
            ));
        }
        catch (Exception ex)
        {
            return Result.Fail<StudioStatusInfo, string>($"Errore chiamata /launch su Studio: {ex.Message}");
        }
    }

    public async Task<Result<bool, string>> CloseGameAsync(CancellationToken ct = default)
    {
        var session = _sessionManager.GetActiveSession();
        if (session == null)
        {
            return Result.Ok<bool, string>(true);
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{session.StudioPort}/close");
            req.Headers.Add("X-Candied-Studio-Token", session.StudioToken);

            var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return Result.Fail<bool, string>($"Chiusura fallita da Studio: HTTP {resp.StatusCode}");
            }

            return Result.Ok<bool, string>(true);
        }
        catch (Exception ex)
        {
            return Result.Fail<bool, string>($"Errore chiamata /close su Studio: {ex.Message}");
        }
    }

    private static string? FindStudioExecutable()
    {
        // 1. Check AppContext.BaseDirectory
        string direct = Path.Combine(AppContext.BaseDirectory, "Candied.Studio.exe");
        if (File.Exists(direct)) return direct;

        // 2. Search upwards for repo root and check bin outputs
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 5 && dir != null; i++)
        {
            string releasePath = Path.Combine(dir, "src", "Candied.Studio", "bin", "Release", "net9.0-windows", "Candied.Studio.exe");
            if (File.Exists(releasePath)) return releasePath;

            string debugPath = Path.Combine(dir, "src", "Candied.Studio", "bin", "Debug", "net9.0-windows", "Candied.Studio.exe");
            if (File.Exists(debugPath)) return debugPath;

            dir = Directory.GetParent(dir)?.FullName;
        }

        // 3. Check current directory
        string curRel = Path.Combine(Directory.GetCurrentDirectory(), "src", "Candied.Studio", "bin", "Release", "net9.0-windows", "Candied.Studio.exe");
        if (File.Exists(curRel)) return curRel;

        string curDeb = Path.Combine(Directory.GetCurrentDirectory(), "src", "Candied.Studio", "bin", "Debug", "net9.0-windows", "Candied.Studio.exe");
        if (File.Exists(curDeb)) return curDeb;

        return null;
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _httpClient.Dispose();
            _isDisposed = true;
        }
    }

    private sealed record StudioWindowDto(
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

    private sealed record StudioStatusDto(
        bool IsRunning,
        string? ActiveGameName,
        int? GamePid,
        int? BridgePort,
        string? EmbeddingMode,
        bool IsVisionConnected,
        StudioWindowDto? Window,
        int GameWidth,
        int GameHeight,
        int PanelWidth,
        int PanelHeight
    );
}
