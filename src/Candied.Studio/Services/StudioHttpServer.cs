using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Candied.Core.Ports;
using Candied.Studio.Native;
using Candied.Studio.ViewModels;
using Candied.Studio.Views;

namespace Candied.Studio.Services;

/// <summary>
/// Lightweight local HTTP server in Candied Studio for MCP automation and remote control.
/// </summary>
public sealed class StudioHttpServer : IDisposable
{
    private readonly IStudioSessionManager _sessionManager;
    private readonly MainWindowViewModel _viewModel;
    private readonly IUnityGameHostController _controller;

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
        MainWindowViewModel viewModel,
        IUnityGameHostController controller)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));

        _controller.ProcessExited += OnGameProcessExited;
    }

    public void Start()
    {
        if (IsListening) return;

        _port = UnityGameHostController.GetAvailableTcpPort();
        _token = Guid.NewGuid().ToString("N");
        _cts = new CancellationTokenSource();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
        _listener.Start();

        // Record active session outside repo
        _sessionManager.WriteSession(new StudioSessionInfo(
            StudioPid: Environment.ProcessId,
            StudioPort: _port,
            StudioToken: _token,
            ActiveGameName: null,
            GamePid: null,
            BridgePort: null,
            BridgeToken: null
        ));

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
                if (ct.IsCancellationRequested) break;
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var req = context.Request;
        var resp = context.Response;

        try
        {
            // Verify secret token (never logged)
            string? tokenHeader = req.Headers["X-Candied-Studio-Token"];
            if (string.IsNullOrEmpty(tokenHeader) || !string.Equals(tokenHeader, _token, StringComparison.Ordinal))
            {
                resp.StatusCode = (int)HttpStatusCode.Unauthorized;
                resp.Close();
                return;
            }

            var path = req.Url?.AbsolutePath.ToLowerInvariant() ?? string.Empty;

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

            resp.StatusCode = (int)HttpStatusCode.NotFound;
            resp.Close();
        }
        catch (Exception ex)
        {
            try
            {
                resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                var errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = ex.Message }));
                resp.ContentType = "application/json";
                resp.ContentLength64 = errBytes.Length;
                await resp.OutputStream.WriteAsync(errBytes, ct).ConfigureAwait(false);
                resp.Close();
            }
            catch { }
        }
    }

    private async Task HandleGetStatusAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        var windowInfo = GetWindowInfo();

        var statusDto = new
        {
            isRunning = _controller.IsRunning,
            activeGameName = _controller.CurrentGameName,
            gamePid = _controller.ActiveGameProcessId,
            bridgePort = _controller.ActiveVisionBridgePort,
            embeddingMode = _controller.EmbeddingMode,
            isVisionConnected = _viewModel.IsVisionConnected,
            gameWidth = _controller.TargetGameWidth,
            gameHeight = _controller.TargetGameHeight,
            panelWidth = _controller.LastPanelWidth,
            panelHeight = _controller.LastPanelHeight,
            window = windowInfo
        };

        var json = JsonSerializer.Serialize(statusDto);
        var bytes = Encoding.UTF8.GetBytes(json);

        resp.StatusCode = (int)HttpStatusCode.OK;
        resp.ContentType = "application/json";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        resp.Close();
    }

    private static StudioWindowInfoDto GetWindowInfo()
    {
        if (Application.Current == null)
        {
            return new StudioWindowInfoDto(0, false, "None", "Hidden", 0, 0, 0, 0, 0, 0, false, false);
        }

        return Application.Current.Dispatcher.Invoke(() =>
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null)
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
                isResponding
            );
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

    private async Task HandlePostLaunchAsync(HttpListenerRequest req, HttpListenerResponse resp, CancellationToken ct)
    {
        using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
        var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        string? gameName = root.TryGetProperty("gameName", out var gn) ? gn.GetString() : null;
        string? executablePath = root.TryGetProperty("executablePath", out var ep) ? ep.GetString() : null;

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            resp.StatusCode = (int)HttpStatusCode.BadRequest;
            var errBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { error = "executablePath is required." }));
            resp.ContentType = "application/json";
            resp.ContentLength64 = errBytes.Length;
            await resp.OutputStream.WriteAsync(errBytes, ct).ConfigureAwait(false);
            resp.Close();
            return;
        }

        // Execute launch on UI thread
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            // Ensure window is visible and active on screen
            if (Application.Current.MainWindow is MainWindow mw)
            {
                mw.EnsureVisibleAndActive();
            }

            // If already running with same game: return current status (idempotence)
            if (_controller.IsRunning &&
                string.Equals(_controller.CurrentGameName, gameName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Stop any currently running game
            if (_controller.IsRunning)
            {
                await _viewModel.StopGameAsync();
            }

            // Wait briefly for container HWND to become available if app just launched
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 5000)
            {
                if (_viewModel.GetContainerHwndFunc != null && _viewModel.GetContainerHwndFunc() != IntPtr.Zero)
                {
                    break;
                }
                await Task.Delay(100);
            }

            await _viewModel.SelectGamePathAsync(executablePath);
            await _viewModel.StartGameAsync();
        });

        // Update session state
        _sessionManager.WriteSession(new StudioSessionInfo(
            StudioPid: Environment.ProcessId,
            StudioPort: _port,
            StudioToken: _token,
            ActiveGameName: _controller.CurrentGameName,
            GamePid: _controller.ActiveGameProcessId,
            BridgePort: _controller.ActiveVisionBridgePort,
            BridgeToken: _controller.ActiveVisionBridgeToken
        ));

        await HandleGetStatusAsync(resp, ct).ConfigureAwait(false);
    }

    private async Task HandlePostCloseAsync(HttpListenerResponse resp, CancellationToken ct)
    {
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await _viewModel.StopGameAsync();
        });

        _sessionManager.WriteSession(new StudioSessionInfo(
            StudioPid: Environment.ProcessId,
            StudioPort: _port,
            StudioToken: _token,
            ActiveGameName: null,
            GamePid: null,
            BridgePort: null,
            BridgeToken: null
        ));

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { status = "Closed" }));
        resp.StatusCode = (int)HttpStatusCode.OK;
        resp.ContentType = "application/json";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes, ct).ConfigureAwait(false);
        resp.Close();
    }

    private void OnGameProcessExited(int exitCode)
    {
        _sessionManager.WriteSession(new StudioSessionInfo(
            StudioPid: Environment.ProcessId,
            StudioPort: _port,
            StudioToken: _token,
            ActiveGameName: null,
            GamePid: null,
            BridgePort: null,
            BridgeToken: null
        ));
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _controller.ProcessExited -= OnGameProcessExited;

            _cts?.Cancel();
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }

            _sessionManager.DeleteSession();
            _isDisposed = true;
        }
    }
}
