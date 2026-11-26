using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Candied.Studio.Native;
using Candied.Studio.ViewModels;
using Candied.Studio.Views;
using Candied.Vision.Hosting;
using Candied.Vision.Models;
using Candied.Vision.Ports;
using Candied.Vision.Protocol;

namespace Candied.Studio.Services;

/// <summary>
/// Local HTTP server inside Candied Studio exposing 2-tier Vision &amp; 3-channel Input endpoints
/// for any hosted PC game or emulator window.
/// </summary>
internal sealed class StudioHttpServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly IStudioSessionManager _sessionManager;
    private readonly IVisionSensorClient _sensorClient;
    private readonly MainWindowViewModel _viewModel;
    private readonly ITargetHostController _controller;

    private HttpListener? _listener;
    private int _port;
    private string _token = string.Empty;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private bool _isDisposed;

    public int Port => _port;
    public bool IsListening => _listener != null && _listener.IsListening;

    public StudioHttpServer(
        IStudioSessionManager sessionManager,
        IVisionSensorClient sensorClient,
        MainWindowViewModel viewModel,
        ITargetHostController controller)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _sensorClient = sensorClient ?? throw new ArgumentNullException(nameof(sensorClient));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

        _controller.ProcessExited += OnTargetProcessExited;
    }

    public void Start()
    {
        if (IsListening)
        {
            return;
        }

        _port = TargetHostController.GetAvailableTcpPort();
        _token = RandomNumberGenerator.GetHexString(32, lowercase: true);
        _cts = new CancellationTokenSource();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();

        WriteCurrentSession();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var req = context.Request;
        var resp = context.Response;

        try
        {
            string? hostHeader = req.Headers["Host"];
            if (string.IsNullOrEmpty(hostHeader) || !hostHeader.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                resp.StatusCode = (int)HttpStatusCode.Forbidden;
                resp.Close();
                return;
            }

            string? originHeader = req.Headers["Origin"];
            if (!string.IsNullOrEmpty(originHeader) && !originHeader.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                resp.StatusCode = (int)HttpStatusCode.Forbidden;
                resp.Close();
                return;
            }

            string? tokenHeader = req.Headers["X-Candied-Studio-Token"];
            if (string.IsNullOrEmpty(tokenHeader) || !FixedTimeTokenEquals(tokenHeader, _token))
            {
                resp.StatusCode = (int)HttpStatusCode.Unauthorized;
                resp.Close();
                return;
            }

            string path = req.Url?.AbsolutePath.ToLowerInvariant() ?? string.Empty;

            if (req.HttpMethod == "GET" && path == "/status")
            {
                await HandleGetStatusAsync(resp, ct).ConfigureAwait(false);
                return;
            }

            if (req.HttpMethod == "POST" && path == "/launch")
            {
                await HandlePostLaunchAsync(req, resp, ct).ConfigureAwait(false);
                return;
            }

            if (req.HttpMethod == "POST" && path == "/close")
            {
                await HandlePostCloseAsync(resp, ct).ConfigureAwait(false);
                return;
            }

            if (req.HttpMethod == "GET" && path == "/screenshot")
            {
                await HandleGetScreenshotAsync(resp, ct).ConfigureAwait(false);
                return;
            }

            if (req.HttpMethod == "POST" && path == "/input")
            {
                await HandlePostInputAsync(req, resp, ct).ConfigureAwait(false);
                return;
            }

            resp.StatusCode = (int)HttpStatusCode.NotFound;
            resp.Close();
        }
        catch (Exception ex)
        {
            try
            {
                resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = ex.Message }, JsonOptions));
                resp.ContentType = "application/json";
                resp.ContentLength64 = errBytes.Length;
                await resp.OutputStream.WriteAsync(errBytes, ct).ConfigureAwait(false);
                resp.Close();
            }
            catch
            {
            }
        }
    }

    private async Task HandleGetStatusAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        var windowInfo = GetWindowInfo();
        var activeTier = !_controller.IsRunning
            ? VisionTier.None
            : (_viewModel.IsSensorConnected ? VisionTier.SensorBridge : VisionTier.BaseWindow);

        var statusDto = new
        {
            isRunning = _controller.IsRunning,
            activeTargetName = _controller.CurrentTargetName,
            activeTargetPath = _controller.CurrentTargetPath,
            activeRunnerPath = _controller.CurrentRunnerPath,
            targetPid = _controller.ActiveTargetProcessId,
            sensorPort = _controller.ActiveSensorPort,
            embeddingMode = _controller.EmbeddingMode,
            activeVisionTier = activeTier,
            isSensorConnected = _viewModel.IsSensorConnected,
            window = windowInfo,
            targetWidth = _controller.TargetWidth,
            targetHeight = _controller.TargetHeight,
            panelWidth = _controller.LastPanelWidth,
            panelHeight = _controller.LastPanelHeight
        };

        string json = JsonSerializer.Serialize(statusDto, JsonOptions);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        resp.StatusCode = (int)HttpStatusCode.OK;
        resp.ContentType = "application/json";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        resp.Close();
    }

    private async Task HandlePostLaunchAsync(HttpListenerRequest req, HttpListenerResponse resp, CancellationToken ct)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        string body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        WindowLaunchSpec? incomingSpec = null;
        try
        {
            incomingSpec = JsonSerializer.Deserialize<WindowLaunchSpec>(body, JsonOptions);
        }
        catch
        {
        }

        WindowLaunchSpec resolvedSpec;
        if (incomingSpec != null &&
            !string.IsNullOrWhiteSpace(incomingSpec.TargetName) &&
            !string.IsNullOrWhiteSpace(incomingSpec.TargetPath) &&
            !string.IsNullOrWhiteSpace(incomingSpec.WorkingDirectory))
        {
            resolvedSpec = incomingSpec;
        }
        else
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string? targetPath = root.TryGetProperty("targetPath", out var tp) ? tp.GetString() : null;
            string? runnerPath = root.TryGetProperty("runnerPath", out var rp) ? rp.GetString() : null;
            string? extraArgs = root.TryGetProperty("extraArguments", out var ea) ? ea.GetString() : null;

            var buildRes = WindowLaunchProfileBuilder.Build(targetPath ?? string.Empty, runnerPath, extraArgs);
            if (buildRes.IsFailure)
            {
                resp.StatusCode = (int)HttpStatusCode.BadRequest;
                byte[] errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = buildRes.Error }, JsonOptions));
                resp.ContentType = "application/json";
                resp.ContentLength64 = errBytes.Length;
                await resp.OutputStream.WriteAsync(errBytes, ct).ConfigureAwait(false);
                resp.Close();
                return;
            }

            resolvedSpec = buildRes.Value;
        }

        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.EnsureVisibleAndActive();
            }

            if (_controller.IsRunning &&
                string.Equals(_controller.CurrentTargetPath, resolvedSpec.TargetPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(_controller.CurrentRunnerPath ?? string.Empty, resolvedSpec.RunnerPath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (_controller.IsRunning)
            {
                await _viewModel.StopTargetAsync();
            }

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5000)
            {
                if (_viewModel.GetContainerHwndFunc != null && _viewModel.GetContainerHwndFunc() != IntPtr.Zero)
                {
                    break;
                }

                await Task.Delay(100);
            }

            await _viewModel.ApplyLaunchSpecAsync(resolvedSpec);
            await _viewModel.StartTargetAsync();
        });

        WriteCurrentSession();
        await HandleGetStatusAsync(resp, ct).ConfigureAwait(false);
    }

    private async Task HandlePostCloseAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await _viewModel.StopTargetAsync();
        });

        WriteCurrentSession();

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { status = "Closed" }, JsonOptions));
        resp.StatusCode = (int)HttpStatusCode.OK;
        resp.ContentType = "application/json";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        resp.Close();
    }

    private async Task HandleGetScreenshotAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        if (!_controller.IsRunning)
        {
            resp.StatusCode = (int)HttpStatusCode.Conflict;
            byte[] err = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = "No target window is currently running." }, JsonOptions));
            resp.ContentType = "application/json";
            resp.ContentLength64 = err.Length;
            await resp.OutputStream.WriteAsync(err, ct).ConfigureAwait(false);
            resp.Close();
            return;
        }

        string? baseUrl = _controller.ActiveSensorBaseUrl;
        string? token = _controller.ActiveSensorToken;

        if (_viewModel.IsSensorConnected && !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(token))
        {
            var sensorShot = await _sensorClient.CaptureScreenshotAsync(baseUrl, token, ct).ConfigureAwait(false);
            if (sensorShot.IsSuccess && sensorShot.Value.PngBytes.Length > 0)
            {
                resp.StatusCode = (int)HttpStatusCode.OK;
                resp.ContentType = "image/png";
                resp.Headers["X-Candied-Vision-Tier"] = VisionTier.SensorBridge.ToString();
                resp.ContentLength64 = sensorShot.Value.PngBytes.Length;
                await resp.OutputStream.WriteAsync(sensorShot.Value.PngBytes, ct).ConfigureAwait(false);
                resp.Close();
                return;
            }
        }

        byte[] extPng = _controller.CaptureExternalScreenshot();
        if (extPng.Length == 0)
        {
            resp.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            byte[] err = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = "Failed to capture external window PNG." }, JsonOptions));
            resp.ContentType = "application/json";
            resp.ContentLength64 = err.Length;
            await resp.OutputStream.WriteAsync(err, ct).ConfigureAwait(false);
            resp.Close();
            return;
        }

        resp.StatusCode = (int)HttpStatusCode.OK;
        resp.ContentType = "image/png";
        resp.Headers["X-Candied-Vision-Tier"] = VisionTier.BaseWindow.ToString();
        resp.ContentLength64 = extPng.Length;
        await resp.OutputStream.WriteAsync(extPng, ct).ConfigureAwait(false);
        resp.Close();
    }

    private async Task HandlePostInputAsync(HttpListenerRequest req, HttpListenerResponse resp, CancellationToken ct)
    {
        if (!_controller.IsRunning)
        {
            resp.StatusCode = (int)HttpStatusCode.Conflict;
            byte[] err = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = "No target window is currently running." }, JsonOptions));
            resp.ContentType = "application/json";
            resp.ContentLength64 = err.Length;
            await resp.OutputStream.WriteAsync(err, ct).ConfigureAwait(false);
            resp.Close();
            return;
        }

        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        string body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        var inputReq = JsonLite.DeserializeInputRequest(body);

        VisionInputResponse inputResp;
        string? baseUrl = _controller.ActiveSensorBaseUrl;
        string? token = _controller.ActiveSensorToken;

        if (_viewModel.IsSensorConnected && !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(token))
        {
            var sensorRes = await _sensorClient.SendInputAsync(baseUrl, token, inputReq, ct).ConfigureAwait(false);
            if (sensorRes.IsSuccess)
            {
                inputResp = sensorRes.Value;
            }
            else
            {
                inputResp = _controller.DispatchBaseWindowInput(inputReq);
            }
        }
        else
        {
            inputResp = _controller.DispatchBaseWindowInput(inputReq);
        }

        byte[] outBytes = Encoding.UTF8.GetBytes(JsonLite.Serialize(inputResp));
        resp.StatusCode = inputResp.Success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
        resp.ContentType = "application/json";
        resp.ContentLength64 = outBytes.Length;
        await resp.OutputStream.WriteAsync(outBytes, ct).ConfigureAwait(false);
        resp.Close();
    }

    private void WriteCurrentSession()
    {
        _sessionManager.WriteSession(new StudioSessionInfo(
            StudioPid: Environment.ProcessId,
            StudioPort: _port,
            StudioToken: _token,
            ActiveTargetName: _controller.IsRunning ? _controller.CurrentTargetName : null,
            TargetPid: _controller.ActiveTargetProcessId,
            SensorPort: _controller.ActiveSensorPort,
            SensorToken: _controller.ActiveSensorToken));
    }

    private static bool FixedTimeTokenEquals(string actual, string expected)
    {
        byte[] a = Encoding.UTF8.GetBytes(actual);
        byte[] b = Encoding.UTF8.GetBytes(expected);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private void OnTargetProcessExited(int exitCode)
    {
        WriteCurrentSession();
    }

    private static StudioWindowInfoDto GetWindowInfo()
    {
        if (Application.Current == null)
        {
            return new StudioWindowInfoDto(0, false, "None", "Hidden", 0, 0, 0, 0, 0, 0, false, false);
        }

        return Application.Current.Dispatcher.Invoke(() =>
        {
            if (Application.Current.MainWindow is not MainWindow mainWindow)
            {
                return new StudioWindowInfoDto(0, false, "None", "Hidden", 0, 0, 0, 0, 0, 0, false, false);
            }

            var helper = new WindowInteropHelper(mainWindow);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero)
            {
                return new StudioWindowInfoDto(0, false, mainWindow.WindowState.ToString(), mainWindow.Visibility.ToString(), 0, 0, 0, 0, 0, 0, false, false);
            }

            bool isVis = Win32Native.IsWindowVisible(hwnd);
            Win32Native.GetWindowRect(hwnd, out var r);
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;

            int screenLeft = (int)SystemParameters.VirtualScreenLeft;
            int screenTop = (int)SystemParameters.VirtualScreenTop;
            int screenWidth = (int)SystemParameters.VirtualScreenWidth;
            int screenHeight = (int)SystemParameters.VirtualScreenHeight;

            bool isWithinMonitor = r.Right > screenLeft &&
                                   r.Left < screenLeft + screenWidth &&
                                   r.Bottom > screenTop &&
                                   r.Top < screenTop + screenHeight &&
                                   w > 100 && h > 100;

            bool isResponding = !Win32Native.IsHungAppWindow(hwnd);

            return new StudioWindowInfoDto(
                (long)hwnd,
                isVis,
                mainWindow.WindowState.ToString(),
                mainWindow.Visibility.ToString(),
                r.Left,
                r.Top,
                r.Right,
                r.Bottom,
                w,
                h,
                isWithinMonitor,
                isResponding);
        });
    }

    private sealed record StudioWindowInfoDto(
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

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _controller.ProcessExited -= OnTargetProcessExited;
            _cts?.Cancel();
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
            }

            _sessionManager.DeleteSession();
            _isDisposed = true;
        }
    }
}
