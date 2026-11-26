using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Candied.Primitives;
using Candied.Studio.Native;
using Candied.Vision.Controls;
using Candied.Vision.Hosting;
using Candied.Vision.Models;
using Candied.Vision.Protocol;

namespace Candied.Studio.Services;

internal sealed class TargetHostController : ITargetHostController
{
    public const int DefaultTargetWidth = 1280;
    public const int DefaultTargetHeight = 720;

    private readonly IWindowsJobObject _jobObject;
    private readonly IWindowLocator _locator;
    private readonly IWindowEmbedder _embedder;

    private Process? _process;
    private WindowLaunchSpec? _activeSpec;
    private CancellationTokenSource? _logCts;
    private CancellationTokenSource? _boundsCts;
    private volatile bool _isStopping;
    private bool _isDisposed;
    private IntPtr _containerHwnd = IntPtr.Zero;
    private int _lastPanelWidth;
    private int _lastPanelHeight;
    private int _lastTargetWidth;
    private int _lastTargetHeight;

    public WindowLaunchSpec? ActiveSpec => _activeSpec;
    public string? CurrentTargetPath => _activeSpec?.TargetPath;
    public string? CurrentRunnerPath => _activeSpec?.RunnerPath;
    public string? CurrentTargetName => _activeSpec?.TargetName;
    public bool IsRunning => _process != null && !_process.HasExited;
    public int? ActiveTargetProcessId => _process != null && !_process.HasExited ? _process.Id : null;
    public IntPtr EmbeddedTargetHwnd { get; private set; } = IntPtr.Zero;
    public string EmbeddingMode { get; private set; } = "Nessuno";
    public int? ActiveSensorPort { get; private set; }
    public string? ActiveSensorToken { get; private set; }
    public string? ActiveSensorBaseUrl => ActiveSensorPort.HasValue ? $"http://127.0.0.1:{ActiveSensorPort.Value}" : null;

    public int TargetWidth => _lastTargetWidth > 0 ? _lastTargetWidth : DefaultTargetWidth;
    public int TargetHeight => _lastTargetHeight > 0 ? _lastTargetHeight : DefaultTargetHeight;
    public int LastPanelWidth => _lastPanelWidth;
    public int LastPanelHeight => _lastPanelHeight;

    public event Action<string>? StatusChanged;
    public event Action<string>? LogReceived;
    public event Action<int>? ProcessExited;

    public TargetHostController(
        IWindowsJobObject jobObject,
        IWindowLocator locator,
        IWindowEmbedder embedder)
    {
        _jobObject = jobObject ?? throw new ArgumentNullException(nameof(jobObject));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _embedder = embedder ?? throw new ArgumentNullException(nameof(embedder));
    }

    public static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public Result<WindowLaunchSpec, string> SelectTarget(
        string targetPath,
        string? runnerPath = null,
        string? extraArguments = null)
    {
        var specRes = WindowLaunchProfileBuilder.Build(targetPath, runnerPath, extraArguments);
        if (specRes.IsFailure)
        {
            return specRes;
        }

        SelectLaunchSpec(specRes.Value);
        return specRes;
    }

    public void SelectLaunchSpec(WindowLaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _activeSpec = spec;
        string modeDesc = string.IsNullOrWhiteSpace(spec.RunnerPath)
            ? "Eseguibile diretto"
            : $"Runner: {Path.GetFileName(spec.RunnerPath)}";
        StatusChanged?.Invoke($"Target selezionato: {spec.TargetName} ({modeDesc})");
    }

    public async Task<bool> LaunchAsync(
        IntPtr containerHwnd,
        int containerPixelWidth,
        int containerPixelHeight,
        CancellationToken ct = default)
    {
        if (_activeSpec == null)
        {
            StatusChanged?.Invoke("Nessun target selezionato.");
            return false;
        }

        if (IsRunning)
        {
            StatusChanged?.Invoke("Il target è già in esecuzione.");
            return false;
        }

        if (containerHwnd == IntPtr.Zero)
        {
            StatusChanged?.Invoke("Handle del contenitore non valido (HWND nullo).");
            return false;
        }

        string exeToLaunch = !string.IsNullOrWhiteSpace(_activeSpec.RunnerPath)
            ? _activeSpec.RunnerPath!
            : _activeSpec.TargetPath;

        if (!File.Exists(exeToLaunch))
        {
            StatusChanged?.Invoke($"Eseguibile non trovato: {exeToLaunch}");
            return false;
        }

        _isStopping = false;
        _containerHwnd = containerHwnd;
        _lastPanelWidth = containerPixelWidth;
        _lastPanelHeight = containerPixelHeight;

        int launchW = DefaultTargetWidth;
        int launchH = DefaultTargetHeight;
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

        _lastTargetWidth = launchW;
        _lastTargetHeight = launchH;

        string args = WindowLaunchProfileBuilder.BuildEffectiveArguments(_activeSpec, containerHwnd, launchW, launchH);

        int port = GetAvailableTcpPort();
        string token = RandomNumberGenerator.GetHexString(32, lowercase: true);
        ActiveSensorPort = port;
        ActiveSensorToken = token;

        StatusChanged?.Invoke($"Avvio di {_activeSpec.TargetName}...");
        LogReceived?.Invoke($"[Host] Avvio processo: {exeToLaunch} {args}".Trim());
        LogReceived?.Invoke($"[Host] Canale SensorBridge in ascolto su 127.0.0.1:{port} (Tier 1 BaseWindow subito disponibile)");

        var psi = new ProcessStartInfo
        {
            FileName = exeToLaunch,
            Arguments = args,
            WorkingDirectory = _activeSpec.WorkingDirectory ?? Path.GetDirectoryName(exeToLaunch) ?? Directory.GetCurrentDirectory(),
            UseShellExecute = false
        };
        psi.Environment["CANDIED_PORT"] = port.ToString(CultureInfo.InvariantCulture);
        psi.Environment["CANDIED_TOKEN"] = token;

        try
        {
            _process = Process.Start(psi);
            if (_process == null)
            {
                StatusChanged?.Invoke("Errore: Process.Start ha restituito null.");
                return false;
            }

            _jobObject.AssignProcess(_process);
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            IntPtr childHwnd = IntPtr.Zero;

            if (_activeSpec.SupportsParentHwndFlag)
            {
                var pollStopwatch = Stopwatch.StartNew();
                while (pollStopwatch.ElapsedMilliseconds < 10000 && !ct.IsCancellationRequested)
                {
                    if (_process.HasExited)
                    {
                        break;
                    }

                    childHwnd = _locator.FindChildWindow(containerHwnd, _activeSpec.WindowClassHint);
                    if (childHwnd != IntPtr.Zero)
                    {
                        break;
                    }

                    await Task.Delay(200, ct).ConfigureAwait(false);
                }
            }

            if (childHwnd != IntPtr.Zero)
            {
                _ = _embedder.EmbedWindow(childHwnd, containerHwnd, containerPixelWidth, containerPixelHeight);
                EmbeddedTargetHwnd = childHwnd;
                EmbeddingMode = "Direct (-parentHWND)";
                StatusChanged?.Invoke($"Finestra incorporata (Diretto, HWND: {childHwnd})");
                LogReceived?.Invoke($"[Host] Embedding riuscito via -parentHWND su HWND {childHwnd}");
            }
            else
            {
                StatusChanged?.Invoke("Ricerca finestra processo e incorporamento Win32 (SetParent)...");
                var fallbackStopwatch = Stopwatch.StartNew();
                IntPtr topHwnd = IntPtr.Zero;

                while (fallbackStopwatch.ElapsedMilliseconds < 15000 && !ct.IsCancellationRequested)
                {
                    if (_process.HasExited)
                    {
                        break;
                    }

                    topHwnd = _locator.FindChildWindow(containerHwnd, _activeSpec.WindowClassHint);
                    if (topHwnd != IntPtr.Zero)
                    {
                        break;
                    }

                    topHwnd = _locator.FindWindowForProcessTree(_process.Id, _activeSpec.WindowClassHint);
                    if (topHwnd != IntPtr.Zero)
                    {
                        break;
                    }

                    await Task.Delay(250, ct).ConfigureAwait(false);
                }

                if (topHwnd != IntPtr.Zero)
                {
                    bool embedded = _embedder.EmbedWindow(topHwnd, containerHwnd, containerPixelWidth, containerPixelHeight);
                    if (embedded)
                    {
                        EmbeddedTargetHwnd = topHwnd;
                        EmbeddingMode = "SetParent (Win32)";
                        StatusChanged?.Invoke($"Finestra incorporata! (SetParent, HWND: {topHwnd})");
                        LogReceived?.Invoke($"[Host] Finestra {topHwnd} incorporata con SetParent.");
                    }
                    else
                    {
                        StatusChanged?.Invoke("Errore: impossibile incorporare la finestra con SetParent.");
                        LogReceived?.Invoke("[Host] Errore: SetParent ha fallito.");
                        return false;
                    }
                }
                else
                {
                    StatusChanged?.Invoke("Errore: nessuna finestra grafica rilevata per il processo avviato.");
                    LogReceived?.Invoke("[Host] Timeout: nessuna finestra visibile trovata nell'albero di processi.");
                    return false;
                }
            }

            Resize(containerPixelWidth, containerPixelHeight, ViewportCalculator.ReferenceDesignWidth, ViewportCalculator.ReferenceDesignHeight);
            StartBoundsStabilizationLoop();
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

        if (_process == null)
        {
            return;
        }

        try
        {
            StatusChanged?.Invoke("Chiusura del target in corso...");
            LogReceived?.Invoke("[Host] Richiesta chiusura del processo...");

            if (!_process.HasExited)
            {
                try
                {
                    _process.CloseMainWindow();
                    bool exited = _process.WaitForExit(3000);
                    if (!exited)
                    {
                        _process.Kill(true);
                    }
                }
                catch
                {
                    try
                    {
                        _process.Kill(true);
                    }
                    catch
                    {
                    }
                }
            }
        }
        finally
        {
            EmbeddedTargetHwnd = IntPtr.Zero;
            _containerHwnd = IntPtr.Zero;
            EmbeddingMode = "Nessuno";
            ActiveSensorPort = null;
            ActiveSensorToken = null;
            _process = null;
            StatusChanged?.Invoke("Target terminato.");
            LogReceived?.Invoke("[Host] Processo terminato con successo.");
        }

        await Task.CompletedTask.ConfigureAwait(false);
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
        Resize(containerPixelWidth, containerPixelHeight, DefaultTargetWidth, DefaultTargetHeight);
    }

    public void Resize(int containerPixelWidth, int containerPixelHeight, int targetWidth, int targetHeight)
    {
        _lastPanelWidth = containerPixelWidth;
        _lastPanelHeight = containerPixelHeight;
        _lastTargetWidth = targetWidth > 0 ? targetWidth : containerPixelWidth;
        _lastTargetHeight = targetHeight > 0 ? targetHeight : containerPixelHeight;

        if (EmbeddedTargetHwnd != IntPtr.Zero)
        {
            if (targetWidth > 0 && targetHeight > 0)
            {
                var (x, y, w, h) = ViewportCalculator.CalculateLetterbox(containerPixelWidth, containerPixelHeight, targetWidth, targetHeight);
                _embedder.ResizeEmbeddedWindow(EmbeddedTargetHwnd, x, y, w, h);
            }
            else
            {
                int w = Math.Max(1, containerPixelWidth);
                int h = Math.Max(1, containerPixelHeight);
                _embedder.ResizeEmbeddedWindow(EmbeddedTargetHwnd, 0, 0, w, h);
            }
        }
    }

    public byte[] CaptureExternalScreenshot()
    {
        if (EmbeddedTargetHwnd == IntPtr.Zero)
        {
            return Array.Empty<byte>();
        }

        byte[] bytes = Win32Native.CaptureWindowPng(EmbeddedTargetHwnd);
        if (bytes.Length == 0)
        {
            IntPtr parent = Win32Native.GetParent(EmbeddedTargetHwnd);
            if (parent != IntPtr.Zero)
            {
                bytes = Win32Native.CaptureWindowPng(parent);
            }
        }

        return bytes;
    }

    public VisionInputResponse DispatchBaseWindowInput(VisionInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (EmbeddedTargetHwnd == IntPtr.Zero)
        {
            return new VisionInputResponse
            {
                Success = false,
                Channel = request.Channel,
                Action = request.Action,
                Message = "No embedded target window is active."
            };
        }

        string channel = (request.Channel ?? VisionConstants.ChannelPointer).Trim().ToLowerInvariant();
        string action = (request.Action ?? "click").Trim().ToLowerInvariant();

        if (channel == VisionConstants.ChannelKeyboard)
        {
            if (!VirtualKeyMapper.TryResolveKeyVk(request.Key, out int vk))
            {
                return new VisionInputResponse
                {
                    Success = false,
                    Channel = VisionConstants.ChannelKeyboard,
                    Action = action,
                    Message = $"Unrecognized keyboard key '{request.Key}'."
                };
            }

            bool ok = action switch
            {
                "down" => Win32Native.SendKeyState(EmbeddedTargetHwnd, vk, isDown: true),
                "up" => Win32Native.SendKeyState(EmbeddedTargetHwnd, vk, isDown: false),
                _ => Win32Native.SendKeyTap(EmbeddedTargetHwnd, vk, Math.Max(30, request.HoldFrames * 16))
            };

            return new VisionInputResponse
            {
                Success = ok,
                Channel = VisionConstants.ChannelKeyboard,
                Action = action,
                Message = $"[BaseWindow] Win32 key '{request.Key}' (VK 0x{vk:X2}) {action} dispatched."
            };
        }

        if (channel == VisionConstants.ChannelGamepad)
        {
            if (!VirtualKeyMapper.TryResolveGamepadFallbackVk(request.GamepadButton, out int vk))
            {
                return new VisionInputResponse
                {
                    Success = false,
                    Channel = VisionConstants.ChannelGamepad,
                    Action = action,
                    Message = $"Gamepad button '{request.GamepadButton}' requires a Tier 2 sensor or keyboard mapping in controls.json."
                };
            }

            bool ok = action switch
            {
                "down" => Win32Native.SendKeyState(EmbeddedTargetHwnd, vk, isDown: true),
                "up" => Win32Native.SendKeyState(EmbeddedTargetHwnd, vk, isDown: false),
                _ => Win32Native.SendKeyTap(EmbeddedTargetHwnd, vk, Math.Max(30, request.HoldFrames * 16))
            };

            return new VisionInputResponse
            {
                Success = ok,
                Channel = VisionConstants.ChannelGamepad,
                Action = action,
                Message = $"[BaseWindow] Gamepad '{request.GamepadButton}' fallback key (VK 0x{vk:X2}) {action} dispatched."
            };
        }

        Win32Native.GetClientRect(EmbeddedTargetHwnd, out var clientRect);
        int clientW = Math.Max(1, clientRect.Right - clientRect.Left);
        int clientH = Math.Max(1, clientRect.Bottom - clientRect.Top);

        int clientX = request.X.HasValue ? Math.Clamp((int)Math.Round(request.X.Value), 0, clientW - 1) : clientW / 2;
        int clientY = request.Y.HasValue ? Math.Clamp((int)Math.Round(request.Y.Value), 0, clientH - 1) : clientH / 2;

        if (action == "move")
        {
            Win32Native.SendMouseMove(EmbeddedTargetHwnd, clientX, clientY);
        }
        else if (action != "isolate" && action != "release_isolation")
        {
            Win32Native.SendMouseClick(EmbeddedTargetHwnd, clientX, clientY, request.Button);
        }

        return new VisionInputResponse
        {
            Success = true,
            Channel = VisionConstants.ChannelPointer,
            Action = action,
            HitPointX = clientX,
            HitPointY = clientY,
            Message = $"[BaseWindow] Win32 mouse '{action}' dispatched at ({clientX}, {clientY})."
        };
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
        catch
        {
        }

        EmbeddedTargetHwnd = IntPtr.Zero;
        EmbeddingMode = "Nessuno";
        ActiveSensorPort = null;
        ActiveSensorToken = null;
        _process = null;
        StopBoundsStabilizationLoop();
        StopLogMonitoring();

        bool isCrash = !_isStopping && exitCode != 0;
        string status = _isStopping
            ? $"Target arrestato (Codice di uscita: {exitCode})"
            : (isCrash
                ? $"Target crashato (Codice di uscita: {exitCode})"
                : $"Target chiuso regolarmente (Codice di uscita: {exitCode})");

        StatusChanged?.Invoke(status);
        LogReceived?.Invoke($"[Host] Processo terminato con codice {exitCode}.");
        ProcessExited?.Invoke(exitCode);
    }

    private void StartLogMonitoring()
    {
        StopLogMonitoring();

        string? logPath = _activeSpec?.LogFilePath;
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
        {
            return;
        }

        _logCts = new CancellationTokenSource();
        var token = _logCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                LogReceived?.Invoke($"[Host] Tailing attivo su {logPath}");
                using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
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
            catch (OperationCanceledException)
            {
            }
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
                    if (token.IsCancellationRequested || !IsRunning)
                    {
                        break;
                    }

                    EnforceEmbeddedBoundsIfDrifted();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }, token);
    }

    private void EnforceEmbeddedBoundsIfDrifted()
    {
        IntPtr child = EmbeddedTargetHwnd;
        IntPtr parent = _containerHwnd;
        int panelW = _lastPanelWidth;
        int panelH = _lastPanelHeight;

        if (child == IntPtr.Zero || parent == IntPtr.Zero || panelW <= 0 || panelH <= 0)
        {
            return;
        }

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
                _lastTargetWidth = targetW;
                _lastTargetHeight = targetH;
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
                try
                {
                    _process.Kill(true);
                }
                catch
                {
                }

                _process.Dispose();
                _process = null;
            }

            _jobObject.Dispose();
            _isDisposed = true;
        }
    }
}
