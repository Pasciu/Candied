using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.Studio.Native;

namespace Candied.Studio.Services;

public sealed class UnityGameHostController : IUnityGameHostController
{
    private readonly IGameDetector _detector;
    private readonly IWindowsJobObject _jobObject;
    private readonly IUnityWindowLocator _locator;
    private readonly IWindowEmbedder _embedder;

    private Process? _process;
    private GameEnvironment? _activeGame;
    private CancellationTokenSource? _logCts;
    private CancellationTokenSource? _boundsCts;
    private volatile bool _isStopping;
    private bool _isDisposed;
    private IntPtr _containerHwnd = IntPtr.Zero;
    private int _lastPanelWidth;
    private int _lastPanelHeight;
    private int _lastGameWidth;
    private int _lastGameHeight;

    public string? CurrentGamePath { get; private set; }
    public string? CurrentGameName => _activeGame?.GameName;
    public bool IsRunning => _process != null && !_process.HasExited;
    public int? ActiveGameProcessId => _process != null && !_process.HasExited ? _process.Id : null;
    public IntPtr EmbeddedGameHwnd { get; private set; } = IntPtr.Zero;
    public string EmbeddingMode { get; private set; } = "Nessuno";
    public int? ActiveVisionBridgePort { get; private set; }
    public string? ActiveVisionBridgeToken { get; private set; }
    public string? ActiveVisionBridgeBaseUrl => ActiveVisionBridgePort.HasValue ? $"http://127.0.0.1:{ActiveVisionBridgePort.Value}" : null;

    public event Action<string>? StatusChanged;
    public event Action<string>? LogReceived;
    public event Action<int>? ProcessExited;

    public UnityGameHostController(
        IGameDetector detector,
        IWindowsJobObject jobObject,
        IUnityWindowLocator locator,
        IWindowEmbedder embedder)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _jobObject = jobObject ?? throw new ArgumentNullException(nameof(jobObject));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
    }

    public const int DefaultGameWidth = 1280;
    public const int DefaultGameHeight = 720;

    public int TargetGameWidth => _lastGameWidth > 0 ? _lastGameWidth : DefaultGameWidth;
    public int TargetGameHeight => _lastGameHeight > 0 ? _lastGameHeight : DefaultGameHeight;
    public int LastPanelWidth => _lastPanelWidth;
    public int LastPanelHeight => _lastPanelHeight;

    public static string BuildLaunchArguments(IntPtr containerHwnd, int gameWidth = DefaultGameWidth, int gameHeight = DefaultGameHeight)
    {
        int w = Math.Max(320, gameWidth > 0 ? gameWidth : DefaultGameWidth);
        int h = Math.Max(240, gameHeight > 0 ? gameHeight : DefaultGameHeight);
        return $"-parentHWND {containerHwnd.ToInt64()} -popupwindow -screen-fullscreen 0 -screen-width {w} -screen-height {h}";
    }

    public static int GetAvailableTcpPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task<Result<GameEnvironment, string>> ValidateAndSelectGameAsync(
        string executablePath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return Result.Fail<GameEnvironment, string>("Il file eseguibile selezionato non esiste.");
        }

        var dir = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrEmpty(dir))
        {
            return Result.Fail<GameEnvironment, string>("Impossibile determinare la cartella del gioco.");
        }

        var detectResult = await _detector.DetectGameAsync(dir, ct).ConfigureAwait(false);
        if (detectResult.IsFailure)
        {
            return Result.Fail<GameEnvironment, string>(detectResult.Error.Message);
        }

        var game = detectResult.Value;

        // Strictly reject IL2CPP per requirement
        if (game.EngineType == UnityEngineType.Il2Cpp)
        {
            return Result.Fail<GameEnvironment, string>(
                "Il gioco selezionato utilizza il runtime Unity IL2CPP. Candied supporta esclusivamente giochi Unity Mono.");
        }

        _activeGame = game;
        CurrentGamePath = executablePath;
        StatusChanged?.Invoke($"Gioco selezionato: {game.GameName} (Unity Mono, {game.Architecture})");
        return Result.Ok<GameEnvironment, string>(game);
    }

    public async Task<bool> LaunchAsync(
        IntPtr containerHwnd,
        int containerPixelWidth,
        int containerPixelHeight,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(CurrentGamePath) || _activeGame == null)
        {
            StatusChanged?.Invoke("Nessun gioco selezionato.");
            return false;
        }

        if (IsRunning)
        {
            StatusChanged?.Invoke("Il gioco è già in esecuzione.");
            return false;
        }

        if (containerHwnd == IntPtr.Zero)
        {
            StatusChanged?.Invoke("Handle del contenitore non valido (HWND nullo).");
            return false;
        }

        StatusChanged?.Invoke($"Avvio del gioco con -parentHWND {containerHwnd}...");
        LogReceived?.Invoke($"[Host] Avvio processo: {CurrentGamePath}");
        _isStopping = false;
        _containerHwnd = containerHwnd;
        _lastPanelWidth = containerPixelWidth;
        _lastPanelHeight = containerPixelHeight;

        int launchW = DefaultGameWidth;
        int launchH = DefaultGameHeight;
        if (containerPixelWidth >= 320 && containerPixelHeight >= 240)
        {
            var (_, _, fitW, fitH) = ViewportCalculator.CalculateLetterbox(
                containerPixelWidth,
                containerPixelHeight,
                ViewportCalculator.ReferenceDesignWidth,
                ViewportCalculator.ReferenceDesignHeight);
            launchW = Math.Max(320, fitW);
            launchH = Math.Max(240, fitH);
        }

        _lastGameWidth = launchW;
        _lastGameHeight = launchH;

        // 1. Prepare launch arguments with 16:9 target resolution matching the stage bezel
        string args = BuildLaunchArguments(containerHwnd, launchW, launchH);

        int port = GetAvailableTcpPort();
        string token = Guid.NewGuid().ToString("N");
        ActiveVisionBridgePort = port;
        ActiveVisionBridgeToken = token;

        Environment.SetEnvironmentVariable("CANDIED_PORT", port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable("CANDIED_TOKEN", token);
        LogReceived?.Invoke($"[Host] VisionBridge configurato su 127.0.0.1:{port}");

        var psi = new ProcessStartInfo
        {
            FileName = CurrentGamePath,
            Arguments = args,
            WorkingDirectory = _activeGame.GameRootPath,
            UseShellExecute = false
        };
        psi.Environment["CANDIED_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        psi.Environment["CANDIED_TOKEN"] = token;

        try
        {
            _process = Process.Start(psi);
            if (_process == null)
            {
                StatusChanged?.Invoke("Errore: Process.Start ha restituito null.");
                return false;
            }

            // 2. Assign to Job Object for automatic tree termination on exit/crash
            _jobObject.AssignProcess(_process);

            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            // 3. Poll for child window (-parentHWND)
            var pollStopwatch = Stopwatch.StartNew();
            IntPtr childHwnd = IntPtr.Zero;

            while (pollStopwatch.ElapsedMilliseconds < 15000 && !ct.IsCancellationRequested)
            {
                if (_process.HasExited) break;

                childHwnd = _locator.FindChildWindow(containerHwnd);
                if (childHwnd != IntPtr.Zero)
                {
                    break;
                }

                await Task.Delay(200, ct).ConfigureAwait(false);
            }

            if (childHwnd != IntPtr.Zero)
            {
                _ = _embedder.EmbedWindow(childHwnd, containerHwnd, containerPixelWidth, containerPixelHeight);
                EmbeddedGameHwnd = childHwnd;
                EmbeddingMode = "Direct (-parentHWND)";
                StatusChanged?.Invoke($"Gioco incorporato con successo! (Diretto, HWND: {childHwnd})");
                LogReceived?.Invoke($"[Host] Embedding riuscito via -parentHWND su HWND {childHwnd}");
            }
            else
            {
                // 4. Fallback search for Unity window
                StatusChanged?.Invoke("Timeout -parentHWND: avvio ricerca fallback (SetParent)...");
                LogReceived?.Invoke("[Host] Timeout -parentHWND scaduto. Ricerca della finestra UnityWndClass...");

                var fallbackStopwatch = Stopwatch.StartNew();
                IntPtr unityHwnd = IntPtr.Zero;

                while (fallbackStopwatch.ElapsedMilliseconds < 15000 && !ct.IsCancellationRequested)
                {
                    if (_process.HasExited) break;

                    unityHwnd = _locator.FindUnityWindowForProcessTree(_process.Id);
                    if (unityHwnd != IntPtr.Zero)
                    {
                        break;
                    }

                    await Task.Delay(250, ct).ConfigureAwait(false);
                }

                if (unityHwnd != IntPtr.Zero)
                {
                    bool embedded = _embedder.EmbedWindow(unityHwnd, containerHwnd, containerPixelWidth, containerPixelHeight);
                    if (embedded)
                    {
                        EmbeddedGameHwnd = unityHwnd;
                        EmbeddingMode = "Fallback (SetParent)";
                        StatusChanged?.Invoke($"Gioco incorporato con successo! (Fallback SetParent, HWND: {unityHwnd})");
                        LogReceived?.Invoke($"[Host] Fallback riuscito: finestra {unityHwnd} incorporata con SetParent");
                    }
                    else
                    {
                        StatusChanged?.Invoke("Fallback fallito: impossibile associare SetParent.");
                        LogReceived?.Invoke("[Host] Errore: SetParent ha fallito.");
                        return false;
                    }
                }
                else
                {
                    StatusChanged?.Invoke("Errore: nessuna finestra Unity individuata.");
                    LogReceived?.Invoke("[Host] Timeout: nessuna finestra con classe UnityWndClass trovata.");
                    return false;
                }
            }

            // Ensure sizing matches letterboxed container and start continuous bounds stabilization
            Resize(containerPixelWidth, containerPixelHeight, ViewportCalculator.ReferenceDesignWidth, ViewportCalculator.ReferenceDesignHeight);
            StartBoundsStabilizationLoop();

            // 5. Start log tailing if BepInEx log exists
            StartLogMonitoring();

            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Errore durante l'avvio: {ex.Message}");
            LogReceived?.Invoke($"[Host] Errore eccezione: {ex}");
            return false;
        }
    }

    public async Task StopAsync()
    {
        _isStopping = true;
        StopBoundsStabilizationLoop();
        StopLogMonitoring();

        if (_process == null) return;

        try
        {
            StatusChanged?.Invoke("Chiusura del gioco in corso...");
            LogReceived?.Invoke("[Host] Richiesta chiusura del processo gioco...");

            if (!_process.HasExited)
            {
                try
                {
                    _process.CloseMainWindow();
                    var exited = _process.WaitForExit(3000);
                    if (!exited)
                    {
                        _process.Kill(true);
                    }
                }
                catch
                {
                    try { _process.Kill(true); } catch { }
                }
            }
        }
        finally
        {
            EmbeddedGameHwnd = IntPtr.Zero;
            _containerHwnd = IntPtr.Zero;
            EmbeddingMode = "Nessuno";
            ActiveVisionBridgePort = null;
            ActiveVisionBridgeToken = null;
            _process = null;
            StatusChanged?.Invoke("Gioco terminato.");
            LogReceived?.Invoke("[Host] Gioco terminato con successo.");
        }

        await Task.CompletedTask;
    }

    public async Task<bool> RestartAsync(
        IntPtr containerHwnd,
        int containerPixelWidth,
        int containerPixelHeight,
        CancellationToken ct = default)
    {
        await StopAsync().ConfigureAwait(false);
        await Task.Delay(500, ct).ConfigureAwait(false);
        return await LaunchAsync(containerHwnd, containerPixelWidth, containerPixelHeight, ct).ConfigureAwait(false);
    }

    public void Resize(int containerPixelWidth, int containerPixelHeight)
    {
        Resize(containerPixelWidth, containerPixelHeight, DefaultGameWidth, DefaultGameHeight);
    }

    public void Resize(int containerPixelWidth, int containerPixelHeight, int gameWidth, int gameHeight)
    {
        _lastPanelWidth = containerPixelWidth;
        _lastPanelHeight = containerPixelHeight;
        _lastGameWidth = gameWidth > 0 ? gameWidth : containerPixelWidth;
        _lastGameHeight = gameHeight > 0 ? gameHeight : containerPixelHeight;

        if (EmbeddedGameHwnd != IntPtr.Zero)
        {
            if (gameWidth > 0 && gameHeight > 0)
            {
                var (x, y, w, h) = ViewportCalculator.CalculateLetterbox(containerPixelWidth, containerPixelHeight, gameWidth, gameHeight);
                _embedder.ResizeEmbeddedWindow(EmbeddedGameHwnd, x, y, w, h);
            }
            else
            {
                int w = Math.Max(1, containerPixelWidth);
                int h = Math.Max(1, containerPixelHeight);
                _embedder.ResizeEmbeddedWindow(EmbeddedGameHwnd, 0, 0, w, h);
            }
        }
    }

    public byte[] CaptureExternalScreenshot()
    {
        if (EmbeddedGameHwnd == IntPtr.Zero) return Array.Empty<byte>();
        byte[] bytes = Win32Native.CaptureWindowPng(EmbeddedGameHwnd);
        if (bytes.Length == 0)
        {
            IntPtr parent = Win32Native.GetParent(EmbeddedGameHwnd);
            if (parent != IntPtr.Zero)
            {
                bytes = Win32Native.CaptureWindowPng(parent);
            }
        }
        return bytes;
    }

    public bool SendInputClick(int clientX, int clientY)
    {
        if (EmbeddedGameHwnd == IntPtr.Zero) return false;

        if (_lastGameWidth > 0 && _lastGameHeight > 0)
        {
            var (boxX, boxY, boxW, boxH) = ViewportCalculator.CalculateLetterbox(
                _lastPanelWidth, _lastPanelHeight, _lastGameWidth, _lastGameHeight);
            var (gameX, gameY, inBounds) = ViewportCalculator.PanelToGameCoordinates(
                clientX, clientY, boxX, boxY, boxW, boxH, _lastGameWidth, _lastGameHeight);

            if (inBounds)
            {
                Win32Native.SendMouseClick(EmbeddedGameHwnd, gameX, gameY);
                return true;
            }
        }

        IntPtr targetHwnd = _containerHwnd != IntPtr.Zero ? _containerHwnd : EmbeddedGameHwnd;
        Win32Native.SendMouseClick(targetHwnd, clientX, clientY);
        return true;
    }

    public bool SendInputGameClick(int gameX, int gameY)
    {
        if (EmbeddedGameHwnd == IntPtr.Zero) return false;
        Win32Native.SendMouseClick(EmbeddedGameHwnd, gameX, gameY);
        return true;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        int exitCode = -1;
        try
        {
            if (_process != null)
            {
                exitCode = _process.ExitCode;
            }
        }
        catch { }

        EmbeddedGameHwnd = IntPtr.Zero;
        EmbeddingMode = "Nessuno";
        ActiveVisionBridgePort = null;
        ActiveVisionBridgeToken = null;
        _process = null;
        StopBoundsStabilizationLoop();
        StopLogMonitoring();

        bool isCrash = !_isStopping && exitCode != 0;
        var status = _isStopping
            ? $"Gioco arrestato (Codice di uscita: {exitCode})"
            : (isCrash
                ? $"Gioco crashato (Codice di uscita: {exitCode})"
                : $"Gioco chiuso regolarmente (Codice di uscita: {exitCode})");

        StatusChanged?.Invoke(status);
        LogReceived?.Invoke($"[Host] Processo terminato con codice {exitCode}.");
        ProcessExited?.Invoke(exitCode);
    }

    private void StartLogMonitoring()
    {
        StopLogMonitoring();

        if (_activeGame == null) return;
        var logPath = Path.Combine(_activeGame.GameRootPath, "BepInEx", "LogOutput.log");
        if (!File.Exists(logPath))
        {
            LogReceived?.Invoke("[Host] Nessun file BepInEx LogOutput.log presente.");
            return;
        }

        _logCts = new CancellationTokenSource();
        var token = _logCts.Token;

        Task.Run(async () =>
        {
            try
            {
                LogReceived?.Invoke($"[Host] Tailing attivo su {logPath}");
                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                // Seek to end to only capture new logs
                fs.Seek(0, SeekOrigin.End);

                while (!token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line != null)
                    {
                        LogReceived?.Invoke(line);
                    }
                    else
                    {
                        await Task.Delay(250, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[Host] Errore lettura log: {ex.Message}");
            }
        }, token);
    }

    private void StopLogMonitoring()
    {
        if (_logCts != null)
        {
            _logCts.Cancel();
            _logCts.Dispose();
            _logCts = null;
        }
    }

    private void StartBoundsStabilizationLoop()
    {
        StopBoundsStabilizationLoop();
        _boundsCts = new CancellationTokenSource();
        var token = _boundsCts.Token;

        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                while (!token.IsCancellationRequested && IsRunning)
                {
                    int delayMs = sw.ElapsedMilliseconds < 20000 ? 250 : 1000;
                    await Task.Delay(delayMs, token).ConfigureAwait(false);
                    if (token.IsCancellationRequested || !IsRunning) break;

                    EnforceEmbeddedBoundsIfDrifted();
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }, token);
    }

    private void EnforceEmbeddedBoundsIfDrifted()
    {
        IntPtr child = EmbeddedGameHwnd;
        IntPtr parent = _containerHwnd;
        int panelW = _lastPanelWidth;
        int panelH = _lastPanelHeight;

        if (child == IntPtr.Zero || parent == IntPtr.Zero || panelW <= 0 || panelH <= 0)
        {
            return;
        }

        // If the parent container HWND was resized in Win32, sync panelW/panelH from parent ClientRect
        if (Win32Native.GetClientRect(parent, out var parentClient))
        {
            int pcW = parentClient.Right - parentClient.Left;
            int pcH = parentClient.Bottom - parentClient.Top;
            if (pcW > 0 && pcH > 0)
            {
                panelW = pcW;
                panelH = pcH;
                _lastPanelWidth = pcW;
                _lastPanelHeight = pcH;
            }
        }

        var (targetX, targetY, targetW, targetH) = ViewportCalculator.CalculateLetterbox(
            panelW,
            panelH,
            ViewportCalculator.ReferenceDesignWidth,
            ViewportCalculator.ReferenceDesignHeight);

        if (Win32Native.GetWindowRect(child, out var childRect) &&
            Win32Native.GetWindowRect(parent, out var parentRect))
        {
            int actualX = childRect.Left - parentRect.Left;
            int actualY = childRect.Top - parentRect.Top;
            int actualW = childRect.Right - childRect.Left;
            int actualH = childRect.Bottom - childRect.Top;

            if (Math.Abs(actualX - targetX) > 1 ||
                Math.Abs(actualY - targetY) > 1 ||
                Math.Abs(actualW - targetW) > 1 ||
                Math.Abs(actualH - targetH) > 1)
            {
                _ = _embedder.EmbedWindow(child, parent, targetW, targetH);
                _ = _embedder.ResizeEmbeddedWindow(child, targetX, targetY, targetW, targetH);
                _lastGameWidth = targetW;
                _lastGameHeight = targetH;
            }
        }
    }

    private void StopBoundsStabilizationLoop()
    {
        if (_boundsCts != null)
        {
            _boundsCts.Cancel();
            _boundsCts.Dispose();
            _boundsCts = null;
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            StopBoundsStabilizationLoop();
            StopLogMonitoring();
            if (_process != null && !_process.HasExited)
            {
                try { _process.Kill(true); } catch { }
                _process.Dispose();
                _process = null;
            }
            _jobObject.Dispose();
            _isDisposed = true;
        }
    }
}
