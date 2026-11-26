using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Candied.Studio.Services;
using Candied.Vision.Analysis;
using Candied.Vision.Controls;
using Candied.Vision.Hosting;
using Candied.Vision.Models;
using Candied.Vision.Ports;
using Candied.Vision.Protocol;
using Microsoft.Win32;

namespace Candied.Studio.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ITargetHostController _controller;
    private readonly IVisionSensorClient _sensorClient;
    private readonly IGameControlProfileStore? _controlStore;
    private readonly IRecentTargetsStore? _recentTargetsStore;

    private string _selectedTargetPath = string.Empty;
    private string _selectedRunnerPath = string.Empty;
    private string _targetName = "Nessun target selezionato";
    private string _statusText = "Seleziona un target recente, sfoglia un eseguibile/ROM o avvia via Candied.Agent.";
    private string _embeddingMode = "Nessuno";
    private bool _isRunning;
    private bool _isBusy;
    private bool _isSidebarVisible = true;

    private RecentTargetEntry? _selectedRecentTarget;
    private ViewportPresetOption _selectedViewportPreset = ViewportPresetOption.DefaultPresets[0];
    private string _viewportResolutionBadge = "1280×720 • 16:9 (1080p Aspect)";
    private string _activeSceneBadge = "Scena: -";

    private string _visionStatusText = "Inattivo (Avvia un gioco o emulatore)";
    private string _visionCapabilitiesText = "Tier 1 BaseWindow (Win32) + Tier 2 SensorBridge automatico.";
    private bool _isSensorConnected;
    private bool _isVirtualMouseIsolated;
    private List<string> _activeCapabilities = new();

    private bool _hasScreenshot;
    private ImageSource? _screenshotImage;
    private bool _isUniformImage;
    private string _uiInspectionSummary = "Nessuna ispezione UI eseguita.";
    private string _hoverCoordinatesText = "Passa il mouse sullo screenshot per leggere le coordinate (Frame & 1080p)";
    private bool _sendClickOnImageTap = true;
    private string _uiSearchFilter = string.Empty;
    private byte[]? _lastScreenshotBytes;
    private int _lastScreenshotPixelWidth = 1280;
    private int _lastScreenshotPixelHeight = 720;
    private IReadOnlyList<VisionUiElementDto> _lastUiElements = Array.Empty<VisionUiElementDto>();

    private string _sceneFilterText = string.Empty;
    private string _sceneSummaryText = "Premi 'Ispeziona Scena' quando un sensore Tier 2 è connesso.";

    private GameControlProfile? _activeControlProfile;
    private string _controlsSummaryText = "Azioni semantiche standard pronte (Confirm, Cancel, Pause, D-Pad).";
    private string _customKeyInput = "Enter";

    private readonly List<StudioLogEntry> _allStructuredLogs = new();
    private string _logSearchText = string.Empty;
    private bool _onlyWarningsAndErrors;

    private CancellationTokenSource? _visionMonitorCts;
    private int _targetInternalWidth = TargetHostController.DefaultTargetWidth;
    private int _targetInternalHeight = TargetHostController.DefaultTargetHeight;

    public Func<IntPtr>? GetContainerHwndFunc { get; set; }
    public Func<(int width, int height)>? GetContainerPixelSizeFunc { get; set; }
    public event Action? ViewportPresetChanged;

    public ObservableCollection<RecentTargetEntry> RecentTargets { get; } = new();
    public IReadOnlyList<ViewportPresetOption> ViewportPresets { get; } = ViewportPresetOption.DefaultPresets;

    public ObservableCollection<UiElementItemViewModel> UiElementItems { get; } = new();
    public ObservableCollection<SceneCameraItemViewModel> SceneCameras { get; } = new();
    public ObservableCollection<SceneNodeItemViewModel> SceneNodes { get; } = new();
    public ObservableCollection<ControlActionItemViewModel> ControlActions { get; } = new();

    public ObservableCollection<string> LogEntries { get; } = new();
    public ObservableCollection<StudioLogEntry> FilteredLogEntries { get; } = new();

    public RecentTargetEntry? SelectedRecentTarget
    {
        get => _selectedRecentTarget;
        set
        {
            if (ReferenceEquals(_selectedRecentTarget, value))
            {
                return;
            }

            _selectedRecentTarget = value;
            OnPropertyChanged();
            if (value != null && !string.Equals(SelectedTargetPath, value.TargetPath, StringComparison.OrdinalIgnoreCase))
            {
                _ = SelectTargetAsync(value.TargetPath, value.RunnerPath, value.ExtraArguments);
            }
        }
    }

    public ViewportPresetOption SelectedViewportPreset
    {
        get => _selectedViewportPreset;
        set
        {
            if (value == null || ReferenceEquals(_selectedViewportPreset, value))
            {
                return;
            }

            _selectedViewportPreset = value;
            OnPropertyChanged();
            ViewportPresetChanged?.Invoke();
        }
    }

    public bool IsSidebarVisible
    {
        get => _isSidebarVisible;
        set
        {
            if (_isSidebarVisible == value)
            {
                return;
            }

            _isSidebarVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SidebarToggleButtonText));
            ViewportPresetChanged?.Invoke();
        }
    }

    public string SidebarToggleButtonText => IsSidebarVisible ? "Nascondi Inspector ▸" : "◂ Mostra Inspector";

    public string ViewportResolutionBadge
    {
        get => _viewportResolutionBadge;
        set { _viewportResolutionBadge = value; OnPropertyChanged(); }
    }

    public string ActiveSceneBadge
    {
        get => _activeSceneBadge;
        set { _activeSceneBadge = value; OnPropertyChanged(); }
    }

    public string SelectedTargetPath
    {
        get => _selectedTargetPath;
        set { _selectedTargetPath = value; OnPropertyChanged(); UpdateCommandStates(); }
    }

    public string SelectedRunnerPath
    {
        get => _selectedRunnerPath;
        set
        {
            _selectedRunnerPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRunnerPath));
            UpdateCommandStates();
        }
    }

    public bool HasRunnerPath => !string.IsNullOrWhiteSpace(_selectedRunnerPath);

    public string TargetName
    {
        get => _targetName;
        set { _targetName = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public string EmbeddingMode
    {
        get => _embeddingMode;
        set { _embeddingMode = value; OnPropertyChanged(); }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            _isRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisionTierBadgeText));
            UpdateCommandStates();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); UpdateCommandStates(); }
    }

    public string VisionStatusText
    {
        get => _visionStatusText;
        set { _visionStatusText = value; OnPropertyChanged(); }
    }

    public string VisionCapabilitiesText
    {
        get => _visionCapabilitiesText;
        set { _visionCapabilitiesText = value; OnPropertyChanged(); }
    }

    public bool IsSensorConnected
    {
        get => _isSensorConnected;
        set
        {
            _isSensorConnected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisionTierBadgeText));
            UpdateCommandStates();
        }
    }

    public string VisionTierBadgeText => !IsRunning
        ? "Tier: Inattivo"
        : (IsSensorConnected ? "🟢 Tier 2: SensorBridge" : "🔵 Tier 1: BaseWindow (Win32)");

    public bool IsVirtualMouseIsolated
    {
        get => _isVirtualMouseIsolated;
        set
        {
            _isVirtualMouseIsolated = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InputIsolationBadgeText));
            OnPropertyChanged(nameof(InputIsolationButtonText));
        }
    }

    public string InputIsolationBadgeText => IsVirtualMouseIsolated
        ? "🤖 Input: Virtual Pointer (Isolato)"
        : "🖱️ Input: Fisico / Win32";

    public string InputIsolationButtonText => IsVirtualMouseIsolated
        ? "🖱️ Usa Mouse Fisico"
        : "🤖 Isola Puntatore AI";

    public bool HasScreenshot
    {
        get => _hasScreenshot;
        set { _hasScreenshot = value; OnPropertyChanged(); }
    }

    public ImageSource? ScreenshotImage
    {
        get => _screenshotImage;
        set { _screenshotImage = value; OnPropertyChanged(); }
    }

    public bool IsUniformImage
    {
        get => _isUniformImage;
        set { _isUniformImage = value; OnPropertyChanged(); }
    }

    public string UiInspectionSummary
    {
        get => _uiInspectionSummary;
        set { _uiInspectionSummary = value; OnPropertyChanged(); }
    }

    public string HoverCoordinatesText
    {
        get => _hoverCoordinatesText;
        set { _hoverCoordinatesText = value; OnPropertyChanged(); }
    }

    public bool SendClickOnImageTap
    {
        get => _sendClickOnImageTap;
        set { _sendClickOnImageTap = value; OnPropertyChanged(); }
    }

    public string UiSearchFilter
    {
        get => _uiSearchFilter;
        set
        {
            _uiSearchFilter = value;
            OnPropertyChanged();
            RebuildUiElementsList();
        }
    }

    public string SceneFilterText
    {
        get => _sceneFilterText;
        set { _sceneFilterText = value; OnPropertyChanged(); }
    }

    public string SceneSummaryText
    {
        get => _sceneSummaryText;
        set { _sceneSummaryText = value; OnPropertyChanged(); }
    }

    public string ControlsSummaryText
    {
        get => _controlsSummaryText;
        set { _controlsSummaryText = value; OnPropertyChanged(); }
    }

    public string CustomKeyInput
    {
        get => _customKeyInput;
        set { _customKeyInput = value; OnPropertyChanged(); }
    }

    public string LogSearchText
    {
        get => _logSearchText;
        set
        {
            _logSearchText = value;
            OnPropertyChanged();
            RebuildFilteredLogs();
        }
    }

    public bool OnlyWarningsAndErrors
    {
        get => _onlyWarningsAndErrors;
        set
        {
            _onlyWarningsAndErrors = value;
            OnPropertyChanged();
            RebuildFilteredLogs();
        }
    }

    public int LastScreenshotPixelWidth => _lastScreenshotPixelWidth;
    public int LastScreenshotPixelHeight => _lastScreenshotPixelHeight;

    public bool CanStart => !IsRunning && !IsBusy && !string.IsNullOrWhiteSpace(SelectedTargetPath);
    public bool CanStop => IsRunning && !IsBusy;
    public bool CanRestart => IsRunning && !IsBusy;
    public bool CanCaptureScreenshot => IsRunning && !IsBusy;
    public bool CanDispatchInput => IsRunning && !IsBusy;
    public bool CanInspectUi => IsRunning && !IsBusy && IsSensorConnected;
    public bool CanInspectScene => IsRunning && !IsBusy && IsSensorConnected;
    public bool CanToggleInputIsolation => IsRunning && !IsBusy && IsSensorConnected;

    public ICommand BrowseTargetCommand { get; }
    public ICommand BrowseRunnerCommand { get; }
    public ICommand ClearRunnerCommand { get; }
    public ICommand RefreshRecentTargetsCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand ToggleInputIsolationCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand CaptureScreenshotCommand { get; }
    public ICommand InspectUiElementsCommand { get; }
    public ICommand ClickUiElementCommand { get; }
    public ICommand InspectSceneCommand { get; }
    public ICommand ReloadControlsCommand { get; }
    public ICommand TriggerActionCommand { get; }
    public ICommand SendCustomKeyCommand { get; }

    public MainWindowViewModel(
        ITargetHostController controller,
        IVisionSensorClient sensorClient,
        IGameControlProfileStore? controlStore = null,
        IRecentTargetsStore? recentTargetsStore = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _sensorClient = sensorClient ?? throw new ArgumentNullException(nameof(sensorClient));
        _controlStore = controlStore;
        _recentTargetsStore = recentTargetsStore;

        _controller.StatusChanged += OnControllerStatusChanged;
        _controller.LogReceived += OnControllerLogReceived;
        _controller.ProcessExited += OnControllerProcessExited;

        BrowseTargetCommand = new RelayCommand(_ => BrowseTarget());
        BrowseRunnerCommand = new RelayCommand(_ => BrowseRunner());
        ClearRunnerCommand = new RelayCommand(_ =>
        {
            SelectedRunnerPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(SelectedTargetPath))
            {
                _ = SelectTargetAsync(SelectedTargetPath, null);
            }
        });
        RefreshRecentTargetsCommand = new RelayCommand(async _ => await LoadRecentTargetsAsync());
        StartCommand = new RelayCommand(async _ => await StartTargetAsync(), _ => CanStart);
        StopCommand = new RelayCommand(async _ => await StopTargetAsync(), _ => CanStop);
        RestartCommand = new RelayCommand(async _ => await RestartTargetAsync(), _ => CanRestart);
        ToggleSidebarCommand = new RelayCommand(_ => IsSidebarVisible = !IsSidebarVisible);
        ToggleInputIsolationCommand = new RelayCommand(async _ => await SetInputIsolationAsync(!IsVirtualMouseIsolated), _ => CanToggleInputIsolation);
        ClearLogsCommand = new RelayCommand(_ => ClearLogs());
        CopyLogsCommand = new RelayCommand(_ => CopyLogsToClipboard());
        CaptureScreenshotCommand = new RelayCommand(async _ => await CaptureScreenshotAsync(), _ => CanCaptureScreenshot);
        InspectUiElementsCommand = new RelayCommand(async _ => await InspectUiElementsAsync(), _ => CanInspectUi);
        ClickUiElementCommand = new RelayCommand(async param =>
        {
            if (param is UiElementItemViewModel item)
            {
                await ClickTargetPointAsync(item.CenterX, item.CenterY, item.DisplayLabel);
            }
        }, _ => CanInspectUi);
        InspectSceneCommand = new RelayCommand(async _ => await InspectSceneAsync(), _ => CanInspectScene);
        ReloadControlsCommand = new RelayCommand(async _ => await LoadControlProfileAsync());
        TriggerActionCommand = new RelayCommand(async param =>
        {
            if (param is string actionName && !string.IsNullOrWhiteSpace(actionName))
            {
                await TriggerSemanticActionAsync(actionName);
            }
        }, _ => CanDispatchInput);
        SendCustomKeyCommand = new RelayCommand(async _ => await SendCustomKeyAsync(), _ => CanDispatchInput);

        PopulateDefaultControlActions();

        if (_recentTargetsStore != null)
        {
            _ = LoadRecentTargetsAsync();
        }
    }

    public async Task LoadRecentTargetsAsync()
    {
        if (_recentTargetsStore == null)
        {
            return;
        }

        try
        {
            var items = await _recentTargetsStore.ListRecentAsync().ConfigureAwait(false);
            void ApplyRecent()
            {
                RecentTargets.Clear();
                foreach (var item in items)
                {
                    RecentTargets.Add(item);
                }
            }

            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(ApplyRecent);
            }
            else
            {
                ApplyRecent();
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Host] Impossibile caricare i target recenti: {ex.Message}");
        }
    }

    public void BrowseTarget()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Giochi & ROM (*.exe;*.iso;*.chd;*.bin;*.cue;*.gba;*.nds;*.sfc;*.nes)|*.exe;*.iso;*.chd;*.bin;*.cue;*.gba;*.nds;*.sfc;*.nes|Tutti i file (*.*)|*.*",
            Title = "Seleziona l'eseguibile del gioco o la ROM/ISO"
        };

        if (dialog.ShowDialog() == true)
        {
            string? runner = string.IsNullOrWhiteSpace(SelectedRunnerPath) ? null : SelectedRunnerPath;
            _ = SelectTargetAsync(dialog.FileName, runner);
        }
    }

    public void BrowseRunner()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Eseguibile Emulatore / Runner (*.exe)|*.exe|Tutti i file (*.*)|*.*",
            Title = "Seleziona l'emulatore o runner opzionale (es. pcsx2-qt.exe, dolphin.exe)"
        };

        if (dialog.ShowDialog() == true)
        {
            SelectedRunnerPath = dialog.FileName;
            if (!string.IsNullOrWhiteSpace(SelectedTargetPath))
            {
                _ = SelectTargetAsync(SelectedTargetPath, SelectedRunnerPath);
            }
        }
    }

    public async Task SelectTargetAsync(string targetPath, string? runnerPath = null, string? extraArguments = null)
    {
        SelectedTargetPath = targetPath;
        SelectedRunnerPath = runnerPath ?? string.Empty;

        var res = _controller.SelectTarget(targetPath, runnerPath, extraArguments);
        if (res.IsSuccess)
        {
            TargetName = res.Value.TargetName;
            StatusText = $"Pronto: {res.Value.TargetName}";
            AddLog($"[Host] Target preparato: {res.Value.TargetName} ({res.Value.TargetPath})");
            await LoadControlProfileAsync();
        }
        else
        {
            TargetName = "Non valido";
            StatusText = res.Error;
            AddLog($"[Host] Errore selezione target: {res.Error}");
        }
    }

    public async Task ApplyLaunchSpecAsync(WindowLaunchSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        SelectedTargetPath = spec.TargetPath;
        SelectedRunnerPath = spec.RunnerPath ?? string.Empty;
        TargetName = spec.TargetName;
        _controller.SelectLaunchSpec(spec);
        await LoadControlProfileAsync();
    }

    public async Task StartTargetAsync()
    {
        if (GetContainerHwndFunc == null || GetContainerPixelSizeFunc == null)
        {
            return;
        }

        if (_controller.ActiveSpec == null && !string.IsNullOrWhiteSpace(SelectedTargetPath))
        {
            var sel = _controller.SelectTarget(SelectedTargetPath, string.IsNullOrWhiteSpace(SelectedRunnerPath) ? null : SelectedRunnerPath);
            if (sel.IsFailure)
            {
                StatusText = sel.Error;
                return;
            }

            TargetName = sel.Value.TargetName;
        }

        IsBusy = true;
        ViewportPresetChanged?.Invoke();
        IntPtr hwnd = GetContainerHwndFunc();
        var (pxW, pxH) = GetContainerPixelSizeFunc();

        bool success = await _controller.LaunchAsync(hwnd, pxW, pxH);
        IsRunning = success;
        EmbeddingMode = _controller.EmbeddingMode;
        UpdateViewportBadge(pxW, pxH);
        IsBusy = false;

        if (success)
        {
            VisionStatusText = "Tier 1 BaseWindow attivo (Cattura Win32 pronta • Ricerca SensorBridge...)";
            ViewportPresetChanged?.Invoke();
            StartSensorMonitoring();

            if (_recentTargetsStore != null && _controller.ActiveSpec != null)
            {
                var spec = _controller.ActiveSpec;
                await _recentTargetsStore.RecordRecentAsync(new RecentTargetEntry(
                    spec.TargetName,
                    spec.TargetPath,
                    spec.RunnerPath,
                    spec.ExtraArguments,
                    DateTimeOffset.UtcNow));
                await LoadRecentTargetsAsync();
            }
        }
    }

    public async Task StopTargetAsync()
    {
        IsBusy = true;
        StopSensorMonitoring();
        await _controller.StopAsync();
        IsRunning = false;
        EmbeddingMode = "Nessuno";
        VisionStatusText = "Inattivo (Target fermato)";
        VisionCapabilitiesText = "-";
        ActiveSceneBadge = "Scena: -";
        IsSensorConnected = false;
        IsVirtualMouseIsolated = false;
        _activeCapabilities.Clear();
        IsBusy = false;
    }

    public async Task RestartTargetAsync()
    {
        if (GetContainerHwndFunc == null || GetContainerPixelSizeFunc == null)
        {
            return;
        }

        IsBusy = true;
        StopSensorMonitoring();
        IntPtr hwnd = GetContainerHwndFunc();
        var (pxW, pxH) = GetContainerPixelSizeFunc();

        bool success = await _controller.RestartAsync(hwnd, pxW, pxH);
        IsRunning = success;
        EmbeddingMode = _controller.EmbeddingMode;
        UpdateViewportBadge(pxW, pxH);
        IsBusy = false;

        if (success)
        {
            ViewportPresetChanged?.Invoke();
            StartSensorMonitoring();
        }
    }

    public void OnHostResized(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }

        UpdateViewportBadge(pixelWidth, pixelHeight);

        if (!IsRunning)
        {
            return;
        }

        _controller.Resize(
            pixelWidth,
            pixelHeight,
            ViewportCalculator.ReferenceDesignWidth,
            ViewportCalculator.ReferenceDesignHeight);
    }

    private void UpdateViewportBadge(int pxW, int pxH)
    {
        int w = pxW > 0 ? pxW : _targetInternalWidth;
        int h = pxH > 0 ? pxH : _targetInternalHeight;
        ViewportResolutionBadge = $"{w}×{h}px • 16:9 (Ref 1920×1080)";
    }

    public async Task LoadControlProfileAsync()
    {
        if (_controlStore == null || string.IsNullOrWhiteSpace(TargetName) || TargetName == "Nessun target selezionato")
        {
            PopulateDefaultControlActions();
            return;
        }

        string profilePath = VisionConfigPaths.ResolveControlProfilePath(TargetName);
        var loaded = await _controlStore.LoadProfileAsync(profilePath);
        _activeControlProfile = loaded;

        if (loaded != null && loaded.Actions.Count > 0)
        {
            ControlActions.Clear();
            foreach (var entry in loaded.Actions)
            {
                ControlActions.Add(ControlActionItemViewModel.FromEntry(entry));
            }

            ControlsSummaryText = $"Profilo caricato ({loaded.Actions.Count} azioni) da {Path.GetFileName(profilePath)}.";
        }
        else
        {
            PopulateDefaultControlActions();
            ControlsSummaryText = "Nessun controls.json personalizzato trovato: attivi i fallback standard.";
        }
    }

    private void PopulateDefaultControlActions()
    {
        ControlActions.Clear();
        ControlActions.Add(new ControlActionItemViewModel("Confirm", "Confirm / Accetta", "builtin", "Key: Enter | Pad: South"));
        ControlActions.Add(new ControlActionItemViewModel("Cancel", "Cancel / Indietro", "builtin", "Key: Escape | Pad: East"));
        ControlActions.Add(new ControlActionItemViewModel("Pause", "Pause / Menu", "builtin", "Key: Escape | Pad: Start"));
    }

    public async Task TriggerSemanticActionAsync(string actionName)
    {
        if (!IsRunning)
        {
            return;
        }

        var resolved = ControlActionResolver.ResolveAction(
            actionName,
            _activeControlProfile,
            _activeCapabilities,
            _lastScreenshotPixelWidth,
            _lastScreenshotPixelHeight);

        if (resolved.IsFailure)
        {
            StatusText = resolved.Error.Message;
            AddLog($"[Controls] {resolved.Error.Code}: {resolved.Error.Message}");
            return;
        }

        await DispatchVisionInputAsync(resolved.Value, $"Azione '{actionName}'");
    }

    public async Task SendCustomKeyAsync()
    {
        if (!IsRunning || string.IsNullOrWhiteSpace(CustomKeyInput))
        {
            return;
        }

        var req = new VisionInputRequest
        {
            Channel = VisionConstants.ChannelKeyboard,
            Action = "press",
            Key = CustomKeyInput.Trim(),
            HoldFrames = 6
        };

        await DispatchVisionInputAsync(req, $"Tasto '{CustomKeyInput.Trim()}'");
    }

    public async Task DispatchVisionInputAsync(VisionInputRequest request, string label)
    {
        ArgumentNullException.ThrowIfNull(request);

        IsBusy = true;
        string? baseUrl = _controller.ActiveSensorBaseUrl;
        string? token = _controller.ActiveSensorToken;

        if (IsSensorConnected && !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(token))
        {
            var res = await _sensorClient.SendInputAsync(baseUrl, token, request);
            if (res.IsSuccess)
            {
                if (request.Channel == VisionConstants.ChannelPointer && request.Action == "click")
                {
                    IsVirtualMouseIsolated = true;
                }

                StatusText = $"{label} inviato via SensorBridge: {res.Value.Message}";
                AddLog($"[SensorBridge] {res.Value.Message}");
                await Task.Delay(200);
                IsBusy = false;
                await CaptureScreenshotAsync();
                return;
            }
        }

        var fallbackResp = await Task.Run(() => _controller.DispatchBaseWindowInput(request));
        StatusText = fallbackResp.Success
            ? $"{label} inviato via Tier 1 BaseWindow."
            : $"Errore invio {label}: {fallbackResp.Message}";
        AddLog($"[Host] {fallbackResp.Message}");
        await Task.Delay(180);
        IsBusy = false;
        if (fallbackResp.Success)
        {
            await CaptureScreenshotAsync();
        }
    }

    public async Task SetInputIsolationAsync(bool isolate)
    {
        string? baseUrl = _controller.ActiveSensorBaseUrl;
        string? token = _controller.ActiveSensorToken;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
        {
            return;
        }

        IsBusy = true;
        var req = new VisionInputRequest
        {
            Channel = VisionConstants.ChannelPointer,
            Action = isolate ? "isolate" : "release_isolation",
            HoldFrames = 1
        };

        var res = await _sensorClient.SendInputAsync(baseUrl, token, req);
        if (res.IsSuccess)
        {
            IsVirtualMouseIsolated = isolate;
            StatusText = isolate
                ? "Puntatore Virtuale AI isolato (il mouse fisico non interferisce col gioco)."
                : "Mouse Fisico riattivato.";
            AddLog($"[SensorBridge] {res.Value.Message}");
        }
        else
        {
            StatusText = $"Impossibile cambiare modalità puntatore: {res.Error.Message}";
            AddLog($"[SensorBridge] Errore isolamento input: {res.Error.Message}");
        }

        IsBusy = false;
    }

    public void UpdateHoverCoordinates(double controlX, double controlY, double controlWidth, double controlHeight)
    {
        if (!HasScreenshot)
        {
            HoverCoordinatesText = "Cattura uno screenshot per ispezionare le coordinate.";
            return;
        }

        var (frameX, frameY, refX, refY, inBounds) = ViewportCalculator.ImageControlToFrameCoordinates(
            controlX,
            controlY,
            controlWidth,
            controlHeight,
            _lastScreenshotPixelWidth,
            _lastScreenshotPixelHeight);

        if (!inBounds)
        {
            HoverCoordinatesText = "Cursore fuori dall'area del frame.";
            return;
        }

        HoverCoordinatesText = $"Frame ({_lastScreenshotPixelWidth}×{_lastScreenshotPixelHeight}): X={frameX}, Y={frameY}  •  1080p Ref: X={refX}, Y={refY}";
    }

    public async Task ClickOnScreenshotPreviewAsync(double controlX, double controlY, double controlWidth, double controlHeight)
    {
        if (!HasScreenshot)
        {
            return;
        }

        var (frameX, frameY, refX, refY, inBounds) = ViewportCalculator.ImageControlToFrameCoordinates(
            controlX,
            controlY,
            controlWidth,
            controlHeight,
            _lastScreenshotPixelWidth,
            _lastScreenshotPixelHeight);

        if (!inBounds)
        {
            return;
        }

        try
        {
            Clipboard.SetText($"{frameX}, {frameY}");
        }
        catch
        {
        }

        if (!SendClickOnImageTap)
        {
            StatusText = $"Coordinate copiate negli appunti: Frame ({frameX}, {frameY}) | 1080p ({refX}, {refY})";
            return;
        }

        await ClickTargetPointAsync(frameX, frameY, $"Frame ({frameX}, {frameY})");
    }

    public async Task ClickTargetPointAsync(float frameX, float frameY, string targetDescription)
    {
        var req = new VisionInputRequest
        {
            Channel = VisionConstants.ChannelPointer,
            Action = "click",
            X = frameX,
            Y = frameY,
            Button = 0,
            HoldFrames = 6
        };

        await DispatchVisionInputAsync(req, $"Click su {targetDescription}");
    }

    public async Task CaptureScreenshotAsync()
    {
        if (!IsRunning)
        {
            StatusText = "Nessuna finestra attiva da catturare.";
            return;
        }

        IsBusy = true;
        string? baseUrl = _controller.ActiveSensorBaseUrl;
        string? token = _controller.ActiveSensorToken;

        if (IsSensorConnected && !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(token))
        {
            StatusText = "Cattura frame via Tier 2 SensorBridge...";
            var result = await _sensorClient.CaptureScreenshotAsync(baseUrl, token);
            if (result.IsSuccess)
            {
                ApplyCapturedFrame(result.Value.PngBytes, result.Value.IsUniform, VisionTier.SensorBridge);
                IsBusy = false;
                return;
            }
        }

        StatusText = "Cattura frame finestra via Tier 1 BaseWindow (Win32)...";
        byte[] extBytes = await Task.Run(() => _controller.CaptureExternalScreenshot());
        if (extBytes.Length > 0)
        {
            bool uniform = false;
            try
            {
                uniform = PurePngDecoder.IsUniformColor(extBytes);
            }
            catch
            {
            }

            ApplyCapturedFrame(extBytes, uniform, VisionTier.BaseWindow);
        }
        else
        {
            StatusText = "Impossibile catturare il frame della finestra ospitata.";
            AddLog("[Host] Cattura finestra Win32 ha restituito 0 byte.");
        }

        IsBusy = false;
    }

    private void ApplyCapturedFrame(byte[] pngBytes, bool isUniform, VisionTier tier)
    {
        _lastScreenshotBytes = pngBytes;
        IsUniformImage = isUniform;

        try
        {
            var bmp = LoadBitmapFromBytes(pngBytes);
            _lastScreenshotPixelWidth = bmp.PixelWidth > 0 ? bmp.PixelWidth : 1280;
            _lastScreenshotPixelHeight = bmp.PixelHeight > 0 ? bmp.PixelHeight : 720;
            ScreenshotImage = bmp;
            HasScreenshot = true;
            UpdateViewportBadge(_lastScreenshotPixelWidth, _lastScreenshotPixelHeight);

            string tierName = tier == VisionTier.SensorBridge ? "Tier 2 Sensor" : "Tier 1 BaseWindow";
            StatusText = $"Frame catturato ({_lastScreenshotPixelWidth}×{_lastScreenshotPixelHeight}px • {tierName})" +
                         (isUniform ? " [ATTENZIONE: Frame uniforme!]" : string.Empty);
            AddLog($"[Host] Screenshot {tierName}: {_lastScreenshotPixelWidth}×{_lastScreenshotPixelHeight}px ({pngBytes.Length} byte)");
        }
        catch (Exception ex)
        {
            StatusText = $"Errore decodifica PNG: {ex.Message}";
            AddLog($"[Host] Errore decodifica PNG: {ex.Message}");
        }
    }

    public async Task InspectUiElementsAsync()
    {
        string? baseUrl = _controller.ActiveSensorBaseUrl;
        string? token = _controller.ActiveSensorToken;

        if (!IsSensorConnected || string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
        {
            StatusText = "Sensore Tier 2 non attivo per l'ispezione UI.";
            return;
        }

        IsBusy = true;
        StatusText = "Ispezione albero UI e cattura frame in corso...";

        var shotResult = await _sensorClient.CaptureScreenshotAsync(baseUrl, token);
        if (shotResult.IsSuccess)
        {
            ApplyCapturedFrame(shotResult.Value.PngBytes, shotResult.Value.IsUniform, VisionTier.SensorBridge);
        }

        var elementsResult = await _sensorClient.GetUiElementsAsync(baseUrl, token);
        if (elementsResult.IsSuccess)
        {
            var elements = elementsResult.Value;
            _lastUiElements = elements;
            RebuildUiElementsList();

            UiInspectionSummary = $"Rilevati {elements.Count} elementi UI ({UiElementItems.Count} nel filtro).";
            AddLog($"[SensorBridge] Rilevati {elements.Count} elementi interattivi UI.");

            if (_lastScreenshotBytes != null && ScreenshotImage is BitmapSource baseBitmap)
            {
                try
                {
                    ScreenshotImage = DrawUiOverlay(baseBitmap, elements);
                    HasScreenshot = true;
                }
                catch (Exception ex)
                {
                    AddLog($"[Host] Errore disegno overlay UI: {ex.Message}");
                }
            }

            StatusText = $"Ispezione UI completata: {elements.Count} elementi tracciati.";
        }
        else
        {
            StatusText = $"Errore ispezione UI: {elementsResult.Error.Message}";
            AddLog($"[SensorBridge] Ispezione UI fallita: {elementsResult.Error.Code} - {elementsResult.Error.Message}");
        }

        IsBusy = false;
    }

    private void RebuildUiElementsList()
    {
        UiElementItems.Clear();
        foreach (var dto in _lastUiElements)
        {
            if (!dto.Visible)
            {
                continue;
            }

            var vm = UiElementItemViewModel.FromDto(dto, _lastScreenshotPixelWidth, _lastScreenshotPixelHeight);
            if (!string.IsNullOrWhiteSpace(_uiSearchFilter))
            {
                string filter = _uiSearchFilter.Trim();
                if (!vm.DisplayLabel.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                    !vm.Type.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            UiElementItems.Add(vm);
        }
    }

    public async Task InspectSceneAsync()
    {
        string? baseUrl = _controller.ActiveSensorBaseUrl;
        string? token = _controller.ActiveSensorToken;

        if (!IsSensorConnected || string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
        {
            StatusText = "Sensore Tier 2 non attivo per l'ispezione scena.";
            return;
        }

        IsBusy = true;
        StatusText = "Ispezione grafo scena e telecamere in corso...";

        var sceneInfoResult = await _sensorClient.GetSceneInfoAsync(baseUrl, token);
        if (sceneInfoResult.IsSuccess)
        {
            var s = sceneInfoResult.Value;
            ActiveSceneBadge = $"Scena: {s.ActiveSceneName} ({s.RootCount} root)";
            if (s.ScreenWidth > 0 && s.ScreenHeight > 0)
            {
                UpdateViewportBadge(s.ScreenWidth, s.ScreenHeight);
            }
        }

        string? filter = string.IsNullOrWhiteSpace(_sceneFilterText) ? null : _sceneFilterText.Trim();
        var graphResult = await _sensorClient.InspectSceneAsync(baseUrl, token, new SceneGraphQuery(NameFilter: filter));
        if (graphResult.IsSuccess)
        {
            var data = graphResult.Value;
            SceneCameras.Clear();
            foreach (var cam in data.Cameras)
            {
                SceneCameras.Add(SceneCameraItemViewModel.FromDto(cam));
            }

            SceneNodes.Clear();
            foreach (var node in data.Nodes)
            {
                SceneNodes.Add(SceneNodeItemViewModel.FromDto(node));
            }

            SceneSummaryText = $"Telecamere: {SceneCameras.Count}  •  Nodi scena: {SceneNodes.Count}" +
                               (filter != null ? $" (Filtro: '{filter}')" : string.Empty);
            StatusText = $"Scena ispezionata: {SceneCameras.Count} telecamere, {SceneNodes.Count} nodi.";
            AddLog($"[SensorBridge] Scena ispezionata: {SceneCameras.Count} telecamere, {SceneNodes.Count} nodi.");
        }
        else
        {
            SceneSummaryText = $"Errore ispezione scena: {graphResult.Error.Message}";
            StatusText = SceneSummaryText;
            AddLog($"[SensorBridge] Errore /scene/nodes: {graphResult.Error.Message}");
        }

        IsBusy = false;
    }

    private void StartSensorMonitoring()
    {
        StopSensorMonitoring();
        _visionMonitorCts = new CancellationTokenSource();
        var ct = _visionMonitorCts.Token;

        _ = Task.Run(async () =>
        {
            string? baseUrl = _controller.ActiveSensorBaseUrl;
            string? token = _controller.ActiveSensorToken;
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
            {
                return;
            }

            bool connected = false;
            for (int i = 0; i < 30 && !ct.IsCancellationRequested && IsRunning; i++)
            {
                await Task.Delay(1000, ct);

                var helloResult = await _sensorClient.GetHelloAsync(baseUrl, token, ct);
                if (helloResult.IsSuccess)
                {
                    await RefreshSensorTelemetryAsync(baseUrl, token, helloResult.Value, isInitialHandshake: true, ct);
                    connected = true;
                    break;
                }
            }

            if (!connected && IsRunning)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    VisionStatusText = "Modalità Tier 1 BaseWindow attiva (Nessun sensore in-game rilevato)";
                    VisionCapabilitiesText = "Screenshot finestra Win32 + Input Mouse/Tastiera/Gamepad fallback attivi.";
                });
                return;
            }

            while (!ct.IsCancellationRequested && IsRunning)
            {
                await Task.Delay(2500, ct);
                if (ct.IsCancellationRequested || !IsRunning)
                {
                    break;
                }

                var helloResult = await _sensorClient.GetHelloAsync(baseUrl, token, ct);
                if (helloResult.IsSuccess)
                {
                    await RefreshSensorTelemetryAsync(baseUrl, token, helloResult.Value, isInitialHandshake: false, ct);
                }
            }
        }, ct);
    }

    private async Task RefreshSensorTelemetryAsync(
        string baseUrl,
        string token,
        VisionHelloDto hello,
        bool isInitialHandshake,
        CancellationToken ct)
    {
        var capResult = await _sensorClient.GetCapabilitiesAsync(baseUrl, token, ct);
        var sceneResult = await _sensorClient.GetSceneInfoAsync(baseUrl, token, ct);

        string capsText = "Tier 2 SensorBridge attivo";
        bool isolated = _isVirtualMouseIsolated;
        List<string> capsList = new();

        if (capResult.IsSuccess)
        {
            var c = capResult.Value;
            capsList = c.Capabilities.ToList();
            capsText = capsList.Count > 0 ? string.Join(" • ", capsList) : "screenshot";

            if (!string.IsNullOrEmpty(c.PointerMethod))
            {
                if (c.PointerMethod.Contains("[ISOLATED]", StringComparison.OrdinalIgnoreCase))
                {
                    isolated = true;
                }
                else if (c.PointerMethod.Contains("[PHYSICAL_ACTIVE]", StringComparison.OrdinalIgnoreCase))
                {
                    isolated = false;
                }
            }
        }

        if (hello.ScreenWidth > 0 && hello.ScreenHeight > 0)
        {
            _targetInternalWidth = hello.ScreenWidth;
            _targetInternalHeight = hello.ScreenHeight;
        }

        string sceneBadge = _activeSceneBadge;
        if (sceneResult.IsSuccess && !string.IsNullOrWhiteSpace(sceneResult.Value.ActiveSceneName))
        {
            sceneBadge = $"Scena: {sceneResult.Value.ActiveSceneName} ({sceneResult.Value.RootCount} root)";
        }

        string engineDesc = string.IsNullOrWhiteSpace(hello.EngineName)
            ? "Sensor"
            : $"{hello.EngineName} {hello.EngineVariant} {hello.EngineVersion}".Trim();

        void UpdateUi()
        {
            _activeCapabilities = capsList;
            VisionStatusText = $"Tier 2 Connesso • {engineDesc} ({hello.ScreenWidth}×{hello.ScreenHeight})";
            VisionCapabilitiesText = capsText;
            IsSensorConnected = true;
            IsVirtualMouseIsolated = isolated;
            ActiveSceneBadge = sceneBadge;
            UpdateViewportBadge(hello.ScreenWidth, hello.ScreenHeight);

            if (isInitialHandshake)
            {
                ViewportPresetChanged?.Invoke();
                AddLog($"[SensorBridge] Handshake v{hello.ProtocolVersion} riuscito! Engine: {engineDesc}");
                AddLog($"[SensorBridge] Capabilities: {capsText}");
            }
        }

        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.Invoke(UpdateUi);
        }
        else
        {
            UpdateUi();
        }
    }

    private void StopSensorMonitoring()
    {
        _visionMonitorCts?.Cancel();
        _visionMonitorCts?.Dispose();
        _visionMonitorCts = null;
    }

    private static BitmapImage LoadBitmapFromBytes(byte[] bytes)
    {
        var bitmap = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        ms.Position = 0;
        bitmap.BeginInit();
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = ms;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static RenderTargetBitmap DrawUiOverlay(BitmapSource baseImage, IReadOnlyList<VisionUiElementDto> elements)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawImage(baseImage, new Rect(0, 0, baseImage.PixelWidth, baseImage.PixelHeight));

            var pen = new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 2);
            var fillBrush = new SolidColorBrush(Color.FromArgb(35, 56, 189, 248));
            var font = new Typeface("Segoe UI");

            foreach (var elem in elements)
            {
                if (!elem.Visible)
                {
                    continue;
                }

                var rect = new Rect(elem.PixelRect.X, elem.PixelRect.Y, elem.PixelRect.Width, elem.PixelRect.Height);
                dc.DrawRectangle(fillBrush, pen, rect);

                string label = $"{elem.Type}: {(string.IsNullOrEmpty(elem.Text) ? elem.Id : elem.Text)}";
                if (label.Length > 28)
                {
                    label = string.Concat(label.AsSpan(0, 28), "...");
                }

                var formattedText = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    font,
                    12,
                    Brushes.Yellow,
                    1.0);

                dc.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)),
                    null,
                    new Rect(rect.X, Math.Max(0, rect.Y - 18), formattedText.Width + 6, 18));
                dc.DrawText(formattedText, new Point(rect.X + 3, Math.Max(0, rect.Y - 18)));
            }
        }

        var rtb = new RenderTargetBitmap(baseImage.PixelWidth, baseImage.PixelHeight, baseImage.DpiX, baseImage.DpiY, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private void OnControllerStatusChanged(string status)
    {
        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.Invoke(() => StatusText = status);
        }
        else
        {
            StatusText = status;
        }
    }

    private void OnControllerLogReceived(string log)
    {
        AddLog(log);
    }

    private void OnControllerProcessExited(int exitCode)
    {
        void HandleExit()
        {
            IsRunning = false;
            EmbeddingMode = "Nessuno";
            VisionStatusText = "Inattivo (Processo terminato)";
            ActiveSceneBadge = "Scena: -";
            IsSensorConnected = false;
            IsVirtualMouseIsolated = false;
            _activeCapabilities.Clear();
        }

        if (Application.Current?.Dispatcher != null)
        {
            Application.Current.Dispatcher.Invoke(HandleExit);
        }
        else
        {
            HandleExit();
        }
    }

    public void AddLog(string entry)
    {
        void Append()
        {
            var structured = StudioLogEntry.Create(entry, DateTime.Now);

            if (LogEntries.Count > 1000)
            {
                LogEntries.RemoveAt(0);
            }

            LogEntries.Add(structured.FullText);

            if (_allStructuredLogs.Count > 1000)
            {
                _allStructuredLogs.RemoveAt(0);
            }

            _allStructuredLogs.Add(structured);

            if (structured.MatchesFilter(_logSearchText, _onlyWarningsAndErrors))
            {
                if (FilteredLogEntries.Count > 1000)
                {
                    FilteredLogEntries.RemoveAt(0);
                }

                FilteredLogEntries.Add(structured);
            }
        }

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(Append);
        }
        else
        {
            Append();
        }
    }

    public void ClearLogs()
    {
        LogEntries.Clear();
        _allStructuredLogs.Clear();
        FilteredLogEntries.Clear();
    }

    public void RebuildFilteredLogs()
    {
        FilteredLogEntries.Clear();
        foreach (var item in _allStructuredLogs)
        {
            if (item.MatchesFilter(_logSearchText, _onlyWarningsAndErrors))
            {
                FilteredLogEntries.Add(item);
            }
        }
    }

    private void CopyLogsToClipboard()
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var item in FilteredLogEntries)
            {
                sb.AppendLine(item.FullText);
            }

            Clipboard.SetText(sb.ToString());
            StatusText = $"Copiate {FilteredLogEntries.Count} righe di log negli appunti.";
        }
        catch (Exception ex)
        {
            StatusText = $"Impossibile copiare i log: {ex.Message}";
        }
    }

    private static void UpdateCommandStates()
    {
        RelayCommand.RaiseCanExecuteChanged();
    }

    public void Dispose()
    {
        StopSensorMonitoring();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
