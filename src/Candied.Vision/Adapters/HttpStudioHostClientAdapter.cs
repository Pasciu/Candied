using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Candied.Primitives;
using Candied.Vision.Models;
using Candied.Vision.Ports;
using Candied.Vision.Protocol;

namespace Candied.Vision.Adapters;

internal sealed class HttpStudioHostClientAdapter : IStudioHostClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IStudioSessionManager _sessionManager;
    private readonly HttpClient _httpClient;
    private bool _isDisposed;

    public HttpStudioHostClientAdapter(IStudioSessionManager sessionManager, HttpClient? httpClient = null)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<StudioSessionInfo?> GetCurrentSessionAsync(CancellationToken ct = default)
    {
        var session = _sessionManager.GetActiveSession();
        if (session == null)
        {
            return null;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{session.StudioPort}/status");
            req.Headers.Add("X-Candied-Studio-Token", session.StudioToken);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            using var resp = await _httpClient.SendAsync(req, cts.Token).ConfigureAwait(false);
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
            return Result.Fail<StudioStatusInfo, string>("Candied Studio is not running.");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{session.StudioPort}/status");
            req.Headers.Add("X-Candied-Studio-Token", session.StudioToken);

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return Result.Fail<StudioStatusInfo, string>($"Studio returned HTTP {resp.StatusCode}.");
            }

            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var dto = JsonSerializer.Deserialize<StudioStatusWireDto>(json, JsonOptions);
            if (dto == null)
            {
                return Result.Fail<StudioStatusInfo, string>("Failed to deserialize Studio status.");
            }

            return Result.Ok<StudioStatusInfo, string>(MapStatus(session.StudioPid, dto));
        }
        catch (Exception ex)
        {
            return Result.Fail<StudioStatusInfo, string>($"Studio connection error: {ex.Message}");
        }
    }

    public async Task<Result<StudioSessionInfo, string>> EnsureStudioRunningAsync(CancellationToken ct = default)
    {
        var existingSession = await GetCurrentSessionAsync(ct).ConfigureAwait(false);
        if (existingSession != null)
        {
            return Result.Ok<StudioSessionInfo, string>(existingSession);
        }

        string? studioExe = FindStudioExecutable();
        if (string.IsNullOrEmpty(studioExe) || !File.Exists(studioExe))
        {
            return Result.Fail<StudioSessionInfo, string>(
                "Could not locate Candied.Studio.exe. Build the solution with `dotnet build`.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = studioExe,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
            WorkingDirectory = Path.GetDirectoryName(studioExe) ?? Directory.GetCurrentDirectory()
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                return Result.Fail<StudioSessionInfo, string>("Process.Start returned null for Candied Studio.");
            }
        }
        catch (Exception ex)
        {
            return Result.Fail<StudioSessionInfo, string>($"Failed to start Candied Studio: {ex.Message}");
        }

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

        return Result.Fail<StudioSessionInfo, string>("Timed out waiting for Candied Studio HTTP server initialization.");
    }

    public async Task<Result<StudioStatusInfo, string>> LaunchTargetAsync(
        WindowLaunchSpec spec,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var ensureRes = await EnsureStudioRunningAsync(ct).ConfigureAwait(false);
        if (ensureRes.IsFailure)
        {
            return Result.Fail<StudioStatusInfo, string>(ensureRes.Error);
        }

        var session = ensureRes.Value;

        try
        {
            string body = JsonSerializer.Serialize(spec, JsonOptions);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{session.StudioPort}/launch");
            req.Headers.Add("X-Candied-Studio-Token", session.StudioToken);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            string content = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                return Result.Fail<StudioStatusInfo, string>($"Studio launch failed ({resp.StatusCode}): {content}");
            }

            var dto = JsonSerializer.Deserialize<StudioStatusWireDto>(content, JsonOptions);
            if (dto == null)
            {
                return Result.Fail<StudioStatusInfo, string>("Invalid launch response from Studio.");
            }

            return Result.Ok<StudioStatusInfo, string>(MapStatus(session.StudioPid, dto));
        }
        catch (Exception ex)
        {
            return Result.Fail<StudioStatusInfo, string>($"Error calling /launch on Studio: {ex.Message}");
        }
    }

    public async Task<Result<bool, string>> CloseTargetAsync(CancellationToken ct = default)
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

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return Result.Fail<bool, string>($"Studio close failed: HTTP {resp.StatusCode}");
            }

            return Result.Ok<bool, string>(true);
        }
        catch (Exception ex)
        {
            return Result.Fail<bool, string>($"Error calling /close on Studio: {ex.Message}");
        }
    }

    public async Task<Result<VisionFrame, VisionError>> CaptureScreenshotAsync(CancellationToken ct = default)
    {
        var session = await GetCurrentSessionAsync(ct).ConfigureAwait(false);
        if (session == null)
        {
            return Result.Fail<VisionFrame, VisionError>(new VisionError(
                "STUDIO_NOT_RUNNING",
                "Candied Studio is not running.",
                "Launch a target first via 'game-launch'."));
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{session.StudioPort}/screenshot");
            req.Headers.Add("X-Candied-Studio-Token", session.StudioToken);

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string errText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return Result.Fail<VisionFrame, VisionError>(new VisionError(
                    $"STUDIO_HTTP_{(int)resp.StatusCode}",
                    $"Failed to capture frame from Studio: {errText}",
                    "Ensure a game or emulator window is running inside Studio."));
            }

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            int width = 0;
            int height = 0;
            if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            }

            var tier = VisionTier.BaseWindow;
            if (resp.Headers.TryGetValues("X-Candied-Vision-Tier", out var tierValues))
            {
                foreach (var tv in tierValues)
                {
                    if (Enum.TryParse<VisionTier>(tv, ignoreCase: true, out var parsedTier))
                    {
                        tier = parsedTier;
                        break;
                    }
                }
            }

            return Result.Ok<VisionFrame, VisionError>(new VisionFrame(bytes, false, width, height, tier));
        }
        catch (Exception ex)
        {
            return Result.Fail<VisionFrame, VisionError>(new VisionError(
                "STUDIO_CAPTURE_ERROR",
                ex.Message,
                "Verify Studio host responsiveness."));
        }
    }

    public async Task<Result<VisionInputResponse, VisionError>> SendInputAsync(
        VisionInputRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await GetCurrentSessionAsync(ct).ConfigureAwait(false);
        if (session == null)
        {
            return Result.Fail<VisionInputResponse, VisionError>(new VisionError(
                "STUDIO_NOT_RUNNING",
                "Candied Studio is not running.",
                "Launch a target first via 'game-launch'."));
        }

        try
        {
            string body = JsonLite.Serialize(request);
            using var req = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{session.StudioPort}/input");
            req.Headers.Add("X-Candied-Studio-Token", session.StudioToken);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return Result.Fail<VisionInputResponse, VisionError>(new VisionError(
                    $"STUDIO_HTTP_{(int)resp.StatusCode}",
                    $"Studio input dispatch failed: {text}",
                    "Ensure a game or emulator window is embedded in Studio."));
            }

            return Result.Ok<VisionInputResponse, VisionError>(JsonLite.DeserializeInputResponse(text));
        }
        catch (Exception ex)
        {
            return Result.Fail<VisionInputResponse, VisionError>(new VisionError(
                "STUDIO_INPUT_ERROR",
                ex.Message,
                "Verify Studio host responsiveness."));
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _httpClient.Dispose();
            _isDisposed = true;
        }
    }

    private static StudioStatusInfo MapStatus(int studioPid, StudioStatusWireDto dto)
    {
        StudioWindowInfo? winInfo = null;
        if (dto.Window != null)
        {
            winInfo = new StudioWindowInfo(
                Handle: dto.Window.Handle,
                IsVisible: dto.Window.IsVisible,
                WindowState: dto.Window.WindowState,
                Visibility: dto.Window.Visibility,
                RectLeft: dto.Window.RectLeft,
                RectTop: dto.Window.RectTop,
                RectRight: dto.Window.RectRight,
                RectBottom: dto.Window.RectBottom,
                Width: dto.Window.Width,
                Height: dto.Window.Height,
                IsWithinMonitor: dto.Window.IsWithinMonitor,
                IsResponding: dto.Window.IsResponding);
        }

        var tier = dto.ActiveVisionTier;
        if (tier == VisionTier.None && dto.IsRunning)
        {
            tier = dto.IsSensorConnected ? VisionTier.SensorBridge : VisionTier.BaseWindow;
        }

        return new StudioStatusInfo(
            StudioPid: studioPid,
            IsRunning: dto.IsRunning,
            ActiveTargetName: dto.ActiveTargetName,
            ActiveTargetPath: dto.ActiveTargetPath,
            ActiveRunnerPath: dto.ActiveRunnerPath,
            TargetPid: dto.TargetPid,
            SensorPort: dto.SensorPort,
            EmbeddingMode: dto.EmbeddingMode,
            ActiveVisionTier: tier,
            IsSensorConnected: dto.IsSensorConnected,
            Window: winInfo,
            TargetWidth: dto.TargetWidth > 0 ? dto.TargetWidth : 1280,
            TargetHeight: dto.TargetHeight > 0 ? dto.TargetHeight : 720,
            PanelWidth: dto.PanelWidth,
            PanelHeight: dto.PanelHeight);
    }

    private static string? FindStudioExecutable()
    {
        string direct = Path.Combine(AppContext.BaseDirectory, "Candied.Studio.exe");
        if (File.Exists(direct))
        {
            return direct;
        }

        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 5 && dir != null; i++)
        {
            string releasePath = Path.Combine(dir, "src", "Candied.Studio", "bin", "Release", "net9.0-windows", "Candied.Studio.exe");
            if (File.Exists(releasePath)) return releasePath;

            string debugPath = Path.Combine(dir, "src", "Candied.Studio", "bin", "Debug", "net9.0-windows", "Candied.Studio.exe");
            if (File.Exists(debugPath)) return debugPath;

            dir = Directory.GetParent(dir)?.FullName;
        }

        string curRel = Path.Combine(Directory.GetCurrentDirectory(), "src", "Candied.Studio", "bin", "Release", "net9.0-windows", "Candied.Studio.exe");
        if (File.Exists(curRel)) return curRel;

        string curDeb = Path.Combine(Directory.GetCurrentDirectory(), "src", "Candied.Studio", "bin", "Debug", "net9.0-windows", "Candied.Studio.exe");
        if (File.Exists(curDeb)) return curDeb;

        return null;
    }

    private sealed record StudioWindowWireDto(
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

    private sealed record StudioStatusWireDto(
        bool IsRunning,
        string? ActiveTargetName,
        string? ActiveTargetPath,
        string? ActiveRunnerPath,
        int? TargetPid,
        int? SensorPort,
        string? EmbeddingMode,
        VisionTier ActiveVisionTier,
        bool IsSensorConnected,
        StudioWindowWireDto? Window,
        int TargetWidth,
        int TargetHeight,
        int PanelWidth,
        int PanelHeight
    );
}
