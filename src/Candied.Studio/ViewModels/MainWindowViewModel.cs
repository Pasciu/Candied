using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Studio.Services;
using Candied.VisionBridge.Protocol;
using Microsoft.Win32;

namespace Candied.Studio.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IUnityGameHostController _controller;
    private readonly IVisionBridgeClient _visionClient;
    private readonly IRegisteredGameRegistry? _gameRegistry;

    private string _selectedGamePath = string.Empty;
    private string _gameName = "Nessun gioco selezionato";
    private string _statusText = "Seleziona un gioco registrato o sfoglia un eseguibile Unity Mono.";
    private string _embeddingMode = "Nessuno";
    private bool _isRunning;
    private bool _isBusy;
    private bool _isSidebarVisible = true;

    // Registered Games & Viewport Presets
    private RegisteredGameInfo? _selectedRegisteredGame;
    private ViewportPresetOption _selectedViewportPreset = ViewportPresetOption.DefaultPresets[0];
    private string _viewportResolutionBadge = "1280×720 • 16:9 (1080p Aspect)";
    private string _activeSceneBadge = "Scena: -";

    // Vision Bridge & Input Isolation State
    private string _visionStatusText = "Inattivo (Avvia il gioco)";
    private string _visionCapabilitiesText = "In attesa di connessione...";
    private bool _isVisionConnected;
    private bool _isVirtualMouseIsolated;
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

    // Scene Inspector State
    private string _sceneFilterText = string.Empty;
    private string _sceneSummaryText = "Premi 'Ispeziona Scena' per elencare Telecamere e Renderer attivi.";

    // Log Filtering State
    private readonly List<StudioLogEntry> _allStructuredLogs = new();
    private string _logSearchText = string.Empty;
    private bool _onlyWarningsAndErrors;

    private CancellationTokenSource? _visionMonitorCts;
    private CancellationTokenSource? _resizeDebounceCts;

    private int _gameInternalWidth = UnityGameHostController.DefaultGameWidth;
    private int _gameInternalHeight = UnityGameHostController.DefaultGameHeight;

    public Func<IntPtr>? GetContainerHwndFunc { get; set; }
    public Func<(int width, int height)>? GetContainerPixelSizeFunc { get; set; }
    public event Action? ViewportPresetChanged;

    public ObservableCollection<RegisteredGameInfo> RegisteredGames { get; } = new();
    public IReadOnlyList<ViewportPresetOption> ViewportPresets { get; } = ViewportPresetOption.DefaultPresets;

    public ObservableCollection<UiElementItemViewModel> UiElementItems { get; } = new();
    public ObservableCollection<SceneCameraItemViewModel> SceneCameras { get; } = new();
    public ObservableCollection<SceneRendererItemViewModel> SceneRenderers { get; } = new();

    public ObservableCollection<string> LogEntries { get; } = new();
    public ObservableCollection<StudioLogEntry> FilteredLogEntries { get; } = new();

    public RegisteredGameInfo? SelectedRegisteredGame
    {
        get => _selectedRegisteredGame;
        set
        {
            if (ReferenceEquals(_selectedRegisteredGame, value)) return;
            _selectedRegisteredGame = value;
            OnPropertyChanged();
            if (value != null && !string.Equals(SelectedGamePath, value.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            {
                _ = SelectGamePathAsync(value.ExecutablePath);
            }
        }
    }

    public ViewportPresetOption SelectedViewportPreset
    {
        get => _selectedViewportPreset;
        set
        {
            if (value == null || ReferenceEquals(_selectedViewportPreset, value)) return;
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
            if (_isSidebarVisible == value) return;
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

    public string SelectedGamePath
    {
        get => _selectedGamePath;
        set { _selectedGamePath = value; OnPropertyChanged(); UpdateCommandStates(); }
    }

    public string GameName
    {
        get => _gameName;
        set { _gameName = value; OnPropertyChanged(); }
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
        set { _isRunning = value; OnPropertyChanged(); UpdateCommandStates(); }
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

    public bool IsVisionConnected
    {
        get => _isVisionConnected;
        set { _isVisionConnected = value; OnPropertyChanged(); UpdateCommandStates(); }
    }

    public bool IsVirtualMouseIsolated
    {
        get => _isVirtualMouseIsolated;
        set
        {
            _isVirtualMouseIsolated = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(InputIsolationBadgeText));
            OnPropertyChanged(nameof(InputIsolationButtonText));
            OnPropertyChanged(nameof(InputIsolationBadgeColor));
        }
    }

    public string InputIsolationBadgeText => IsVirtualMouseIsolated
        ? "🤖 Input: Virtual Mouse AI (Isolato)"
        : "🖱️ Input: Mouse Fisico (Manuale)";

    public string InputIsolationButtonText => IsVirtualMouseIsolated
        ? "🖱️ Usa Mouse Fisico"
        : "🤖 Isola Mouse AI";

    public string InputIsolationBadgeColor => IsVirtualMouseIsolated ? "#38BDF8" : "#4ADE80";

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

    public bool CanStart => !IsRunning && !IsBusy && !string.IsNullOrWhiteSpace(SelectedGamePath);
    public bool CanStop => IsRunning && !IsBusy;
    public bool CanRestart => IsRunning && !IsBusy;
    public bool CanCaptureScreenshot => IsRunning && !IsBusy && _controller.ActiveVisionBridgePort.HasValue;
    public bool CanInspectUi => IsRunning && !IsBusy && _controller.ActiveVisionBridgePort.HasValue;
    public bool CanInspectScene => IsRunning && !IsBusy && _controller.ActiveVisionBridgePort.HasValue;
    public bool CanToggleInputIsolation => IsRunning && !IsBusy && _controller.ActiveVisionBridgePort.HasValue;

    public ICommand BrowseGameCommand { get; }
    public ICommand RefreshRegisteredGamesCommand { get; }
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

    public MainWindowViewModel(
        IUnityGameHostController controller,
        IVisionBridgeClient visionClient,
        IRegisteredGameRegistry? gameRegistry = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _visionClient = visionClient ?? throw new ArgumentNullException(nameof(visionClient));
        _gameRegistry = gameRegistry;

        _controller.StatusChanged += OnControllerStatusChanged;
        _controller.LogReceived += OnControllerLogReceived;
        _controller.ProcessExited += OnControllerProcessExited;

        BrowseGameCommand = new RelayCommand(_ => BrowseGame());
        RefreshRegisteredGamesCommand = new RelayCommand(async _ => await LoadRegisteredGamesAsync());
        StartCommand = new RelayCommand(async _ => await StartGameAsync(), _ => CanStart);
        StopCommand = new RelayCommand(async _ => await StopGameAsync(), _ => CanStop);
        RestartCommand = new RelayCommand(async _ => await RestartGameAsync(), _ => CanRestart);
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
                await ClickGamePointAsync(item.CenterX, item.CenterY, item.DisplayLabel);
            }
        }, _ => CanInspectUi);
        InspectSceneCommand = new RelayCommand(async _ => await InspectSceneAsync(), _ => CanInspectScene);

        if (_gameRegistry != null)
        {
            _ = LoadRegisteredGamesAsync();
        }
    }

    public async Task LoadRegisteredGamesAsync()
    {
        if (_gameRegistry == null) return;

        try
        {
            var games = await _gameRegistry.ListRegisteredGamesAsync();
            void ApplyGames()
            {
                RegisteredGames.Clear();
                foreach (var g in games)
                {
                    RegisteredGames.Add(g);
                }

                if (SelectedRegisteredGame == null && RegisteredGames.Count > 0 && string.IsNullOrWhiteSpace(SelectedGamePath))
                {
                    SelectedRegisteredGame = RegisteredGames[0];
                }
            }

            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.Invoke(ApplyGames);
            }
            else
            {
                ApplyGames();
            }
        }
        catch (Exception ex)
        {
            AddLog($"[Host] Impossibile caricare i giochi registrati: {ex.Message}");
        }
    }

    public void BrowseGame()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Eseguibili Windows (*.exe)|*.exe|Tutti i file (*.*)|*.*",
            Title = "Seleziona l'eseguibile del gioco Unity Mono"
        };

        if (dialog.ShowDialog() == true)
        {
            _ = SelectGamePathAsync(dialog.FileName);
        }
    }

    public async Task SelectGamePathAsync(string path)
    {
        SelectedGamePath = path;
        IsBusy = true;
        StatusText = "Rilevamento e validazione del gioco...";

        var result = await _controller.ValidateAndSelectGameAsync(path);
        if (result.IsSuccess)
        {
            GameName = result.Value.GameName;
            StatusText = $"Pronto: {result.Value.GameName} ({result.Value.Architecture}, Mono)";
            AddLog($"[Host] Gioco convalidato: {result.Value.GameName} ({result.Value.ManagedPath})");
        }
        else
        {
            GameName = "Non valido";
            StatusText = result.Error;
            AddLog($"[Host] Errore di validazione: {result.Error}");
        }

        IsBusy = false;
    }

    public async Task StartGameAsync()
    {
        if (GetContainerHwndFunc == null || GetContainerPixelSizeFunc == null) return;

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
            ViewportPresetChanged?.Invoke();
            StartVisionBridgeMonitoring();
        }
    }

    public async Task StopGameAsync()
    {
        IsBusy = true;
        StopVisionBridgeMonitoring();
        await _controller.StopAsync();
        IsRunning = false;
        EmbeddingMode = "Nessuno";
        VisionStatusText = "Inattivo (Gioco fermato)";
        VisionCapabilitiesText = "-";
        ActiveSceneBadge = "Scena: -";
        IsVisionConnected = false;
        IsVirtualMouseIsolated = false;
        IsBusy = false;
    }

    public async Task RestartGameAsync()
    {
        if (GetContainerHwndFunc == null || GetContainerPixelSizeFunc == null) return;

        IsBusy = true;
        StopVisionBridgeMonitoring();
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
            StartVisionBridgeMonitoring();
        }
    }

    public void OnHostResized(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return;

        UpdateViewportBadge(pixelWidth, pixelHeight);

        if (!IsRunning) return;

        // Maintain strict 16:9 (1920x1080 aspect ratio) inside the host container
        _controller.Resize(
            pixelWidth,
            pixelHeight,
            ViewportCalculator.ReferenceDesignWidth,
            ViewportCalculator.ReferenceDesignHeight);
    }

    private void UpdateViewportBadge(int pxW, int pxH)
    {
        int w = pxW > 0 ? pxW : _gameInternalWidth;
        int h = pxH > 0 ? pxH : _gameInternalHeight;
        ViewportResolutionBadge = $"{w}×{h}px • 16:9 (Ref 1920×1080)";
    }

    public async Task SetInputIsolationAsync(bool isolate)
    {
        string? baseUrl = _controller.ActiveVisionBridgeBaseUrl;
        string? token = _controller.ActiveVisionBridgeToken;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token)) return;

        IsBusy = true;
        string action = isolate ? "isolate" : "release_isolation";
        var res = await _visionClient.SendInputClickAsync(baseUrl, token, 0, 0, button: 0, method: null, action: action, holdFrames: 1);
        if (res.IsSuccess)
        {
            IsVirtualMouseIsolated = isolate;
            StatusText = isolate
                ? "Mouse Virtuale AI isolato (il tuo mouse fisico non interferisce col gioco)."
                : "Mouse Fisico riattivato (puoi cliccare manualmente dentro il gioco).";
            AddLog($"[VisionBridge] {res.Value.Message}");
        }
        else
        {
            StatusText = $"Impossibile cambiare modalità input: {res.Error.Message}";
            AddLog($"[VisionBridge] Errore cambio modalità input: {res.Error.Message}");
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
            HoverCoordinatesText = "Cursore fuori dall'area 16:9 del frame.";
            return;
        }

        HoverCoordinatesText = $"Frame ({_lastScreenshotPixelWidth}×{_lastScreenshotPixelHeight}): X={frameX}, Y={frameY}  •  1080p Ref: X={refX}, Y={refY}";
    }

    public async Task ClickOnScreenshotPreviewAsync(double controlX, double controlY, double controlWidth, double controlHeight)
    {
        if (!HasScreenshot) return;

        var (frameX, frameY, refX, refY, inBounds) = ViewportCalculator.ImageControlToFrameCoordinates(
            controlX,
            controlY,
            controlWidth,
            controlHeight,
            _lastScreenshotPixelWidth,
            _lastScreenshotPixelHeight);

        if (!inBounds) return;

        try
        {
            Clipboard.SetText($"{frameX}, {frameY}");
        }
        catch { }

        if (!SendClickOnImageTap)
        {
            StatusText = $"Coordinate copiate negli appunti: Frame ({frameX}, {frameY}) | 1080p ({refX}, {refY})";
            return;
        }

        await ClickGamePointAsync(frameX, frameY, $"Screenshot ({frameX}, {frameY}) [1080p: {refX}, {refY}]");
    }

    public async Task ClickGamePointAsync(float frameX, float frameY, string targetDescription)
    {
        string? baseUrl = _controller.ActiveVisionBridgeBaseUrl;
        string? token = _controller.ActiveVisionBridgeToken;
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token)) return;

        IsBusy = true;
        StatusText = $"Invio click virtuale su {targetDescription}...";
        AddLog($"[VisionBridge] POST /input/click su ({frameX:F0}, {frameY:F0}) -> {targetDescription}");

        var clickRes = await _visionClient.SendInputClickAsync(baseUrl, token, frameX, frameY, button: 0, method: null, action: "click", holdFrames: 5);
        if (clickRes.IsSuccess)
        {
            IsVirtualMouseIsolated = true;
            StatusText = $"Click eseguito su {targetDescription}. Aggiornamento frame...";
            AddLog($"[VisionBridge] {clickRes.Value.Message}");
            await Task.Delay(250);
            IsBusy = false;
            await CaptureScreenshotAsync();
        }
        else
        {
            StatusText = $"Errore click su {targetDescription}: {clickRes.Error.Message}";
            AddLog($"[VisionBridge] Click fallito: {clickRes.Error.Message}");
            IsBusy = false;
        }
    }

    public async Task CaptureScreenshotAsync()
    {
        string? baseUrl = _controller.ActiveVisionBridgeBaseUrl;
        string? token = _controller.ActiveVisionBridgeToken;

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
        {
            StatusText = "VisionBridge non configurato o inattivo.";
            return;
        }

        IsBusy = true;
        StatusText = "Cattura dello screenshot in corso da VisionBridge...";
        AddLog("[Host] Richiesta GET /screenshot a VisionBridge...");

        var result = await _visionClient.CaptureScreenshotAsync(baseUrl, token);
        if (result.IsSuccess)
        {
            var screenshot = result.Value;
            _lastScreenshotBytes = screenshot.PngBytes;
            IsUniformImage = screenshot.IsUniform;

            try
            {
                var bmp = LoadBitmapFromBytes(screenshot.PngBytes);
                _lastScreenshotPixelWidth = bmp.PixelWidth > 0 ? bmp.PixelWidth : Math.Max(1, screenshot.Width);
                _lastScreenshotPixelHeight = bmp.PixelHeight > 0 ? bmp.PixelHeight : Math.Max(1, screenshot.Height);
                ScreenshotImage = bmp;
                HasScreenshot = true;
                UpdateViewportBadge(_lastScreenshotPixelWidth, _lastScreenshotPixelHeight);

                StatusText = $"Screenshot catturato ({_lastScreenshotPixelWidth}×{_lastScreenshotPixelHeight}px, 16:9)" +
                             (screenshot.IsUniform ? " [ATTENZIONE: Immagine uniforme/nera!]" : "");
                AddLog($"[Host] Screenshot ricevuto: {_lastScreenshotPixelWidth}×{_lastScreenshotPixelHeight}px ({screenshot.PngBytes.Length} byte)");
            }
            catch (Exception ex)
            {
                StatusText = $"Errore decodifica immagine: {ex.Message}";
                AddLog($"[Host] Errore decodifica PNG: {ex}");
            }
        }
        else
        {
            StatusText = $"Errore cattura screenshot: {result.Error.Message}";
            AddLog($"[Host] Screenshot fallito: {result.Error.Code} - {result.Error.Message}");
        }

        IsBusy = false;
    }

    public async Task InspectUiElementsAsync()
    {
        string? baseUrl = _controller.ActiveVisionBridgeBaseUrl;
        string? token = _controller.ActiveVisionBridgeToken;

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
        {
            StatusText = "VisionBridge non attivo.";
            return;
        }

        IsBusy = true;
        StatusText = "Ispezione elementi UI e cattura frame in corso...";
        AddLog("[Host] Richiesta GET /ui/elements a VisionBridge...");

        var shotResult = await _visionClient.CaptureScreenshotAsync(baseUrl, token);
        if (shotResult.IsSuccess)
        {
            _lastScreenshotBytes = shotResult.Value.PngBytes;
            IsUniformImage = shotResult.Value.IsUniform;
            var bmp = LoadBitmapFromBytes(shotResult.Value.PngBytes);
            _lastScreenshotPixelWidth = bmp.PixelWidth > 0 ? bmp.PixelWidth : Math.Max(1, shotResult.Value.Width);
            _lastScreenshotPixelHeight = bmp.PixelHeight > 0 ? bmp.PixelHeight : Math.Max(1, shotResult.Value.Height);
            ScreenshotImage = bmp;
            HasScreenshot = true;
        }

        var elementsResult = await _visionClient.GetUiElementsAsync(baseUrl, token);
        if (elementsResult.IsSuccess)
        {
            var elements = elementsResult.Value;
            _lastUiElements = elements;
            RebuildUiElementsList();

            UiInspectionSummary = $"Rilevati {elements.Count} elementi interattivi uGUI ({UiElementItems.Count} visibili nel filtro).";
            AddLog($"[Host] Rilevati {elements.Count} elementi interattivi via UguiProvider.");

            if (_lastScreenshotBytes != null && ScreenshotImage is BitmapSource baseBitmap)
            {
                try
                {
                    ScreenshotImage = DrawUiOverlay(baseBitmap, elements);
                    HasScreenshot = true;
                }
                catch (Exception ex)
                {
                    AddLog($"[Host] Errore disegno overlay: {ex.Message}");
                }
            }

            StatusText = $"Ispezione UI completata: {elements.Count} elementi tracciati.";
        }
        else
        {
            StatusText = $"Errore ispezione UI: {elementsResult.Error.Message}";
            AddLog($"[Host] Ispezione UI fallita: {elementsResult.Error.Code} - {elementsResult.Error.Message}");
        }

        IsBusy = false;
    }

    private void RebuildUiElementsList()
    {
        UiElementItems.Clear();
        foreach (var dto in _lastUiElements)
        {
            if (!dto.Visible) continue;
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
        string? baseUrl = _controller.ActiveVisionBridgeBaseUrl;
        string? token = _controller.ActiveVisionBridgeToken;

        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token))
        {
            StatusText = "VisionBridge non attivo.";
            return;
        }

        IsBusy = true;
        StatusText = "Ispezione gerarchia scena, telecamere e renderer in corso...";
        AddLog($"[Host] Richiesta GET /scene/objects (filtro: '{_sceneFilterText}')...");

        var sceneInfoResult = await _visionClient.GetSceneInfoAsync(baseUrl, token);
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
        var objectsResult = await _visionClient.InspectSceneObjectsAsync(baseUrl, token, new SceneObjectsQuery(NameFilter: filter));
        if (objectsResult.IsSuccess)
        {
            var data = objectsResult.Value;
            SceneCameras.Clear();
            if (data.Cameras != null)
            {
                foreach (var cam in data.Cameras)
                {
                    SceneCameras.Add(SceneCameraItemViewModel.FromDto(cam));
                }
            }

            SceneRenderers.Clear();
            if (data.Renderers != null)
            {
                foreach (var ren in data.Renderers)
                {
                    SceneRenderers.Add(SceneRendererItemViewModel.FromDto(ren));
                }
            }

            SceneSummaryText = $"Telecamere: {SceneCameras.Count}  •  Renderer attivi: {SceneRenderers.Count}" +
                               (filter != null ? $" (Filtro: '{filter}')" : "");
            StatusText = $"Scena ispezionata: {SceneCameras.Count} telecamere, {SceneRenderers.Count} renderer trovati.";
            AddLog($"[Host] Scena ispezionata: {SceneCameras.Count} telecamere, {SceneRenderers.Count} renderer.");
        }
        else
        {
            SceneSummaryText = $"Errore ispezione scena: {objectsResult.Error.Message}";
            StatusText = SceneSummaryText;
            AddLog($"[Host] Errore /scene/objects: {objectsResult.Error.Message}");
        }

        IsBusy = false;
    }

    private void StartVisionBridgeMonitoring()
    {
        StopVisionBridgeMonitoring();
        _visionMonitorCts = new CancellationTokenSource();
        var ct = _visionMonitorCts.Token;

        _ = Task.Run(async () =>
        {
            string? baseUrl = _controller.ActiveVisionBridgeBaseUrl;
            string? token = _controller.ActiveVisionBridgeToken;
            if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(token)) return;

            VisionStatusText = $"In ascolto su {baseUrl} (In attesa handshake...)";

            // 1. Initial Handshake Loop
            bool connected = false;
            for (int i = 0; i < 30 && !ct.IsCancellationRequested && IsRunning; i++)
            {
                await Task.Delay(1000, ct);

                var helloResult = await _visionClient.GetHelloAsync(baseUrl, token, ct);
                if (helloResult.IsSuccess)
                {
                    var hello = helloResult.Value;
                    await RefreshBridgeTelemetryAsync(baseUrl, token, hello, isInitialHandshake: true, ct);
                    connected = true;
                    break;
                }
            }

            if (!connected && IsRunning)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    VisionStatusText = "Non rilevato (Plugin BepInEx non presente o in caricamento)";
                });
                return;
            }

            // 2. Periodic Live Telemetry Loop (tracks scene changes, resolution, and AI virtual mouse isolation state)
            while (!ct.IsCancellationRequested && IsRunning)
            {
                await Task.Delay(2500, ct);
                if (ct.IsCancellationRequested || !IsRunning) break;

                var helloResult = await _visionClient.GetHelloAsync(baseUrl, token, ct);
                if (helloResult.IsSuccess)
                {
                    await RefreshBridgeTelemetryAsync(baseUrl, token, helloResult.Value, isInitialHandshake: false, ct);
                }
            }
        }, ct);
    }

    private async Task RefreshBridgeTelemetryAsync(
        string baseUrl,
        string token,
        VisionHelloDto hello,
        bool isInitialHandshake,
        CancellationToken ct)
    {
        var capResult = await _visionClient.GetCapabilitiesAsync(baseUrl, token, ct);
        var sceneResult = await _visionClient.GetSceneInfoAsync(baseUrl, token, ct);

        string capsText = "Capabilities attive";
        bool isolated = _isVirtualMouseIsolated;

        if (capResult.IsSuccess)
        {
            var c = capResult.Value;
            var capList = new List<string>();
            if (c.Ugui) capList.Add("uGUI");
            if (c.EventSystem) capList.Add("EventSystem");
            if (c.UiToolkit) capList.Add("UI Toolkit");
            if (c.LegacyInput) capList.Add("Legacy Input");
            if (c.InputSystem) capList.Add("InputSystem");
            if (c.Screenshot) capList.Add($"Screenshot ({c.ScreenshotMethod})");
            capsText = string.Join(" • ", capList.ToArray());

            if (!string.IsNullOrEmpty(c.InputClickStatus))
            {
                if (c.InputClickStatus.Contains("[ISOLATED]", StringComparison.OrdinalIgnoreCase))
                {
                    isolated = true;
                }
                else if (c.InputClickStatus.Contains("[PHYSICAL_ACTIVE]", StringComparison.OrdinalIgnoreCase))
                {
                    isolated = false;
                }
            }
        }

        if (hello.ScreenWidth > 0 && hello.ScreenHeight > 0)
        {
            _gameInternalWidth = hello.ScreenWidth;
            _gameInternalHeight = hello.ScreenHeight;
        }

        string sceneBadge = _activeSceneBadge;
        if (sceneResult.IsSuccess && !string.IsNullOrWhiteSpace(sceneResult.Value.ActiveSceneName))
        {
            sceneBadge = $"Scena: {sceneResult.Value.ActiveSceneName} ({sceneResult.Value.RootCount} root)";
        }

        void UpdateUi()
        {
            VisionStatusText = $"Connesso • Unity {hello.UnityVersion} ({hello.ScreenWidth}×{hello.ScreenHeight})";
            VisionCapabilitiesText = capsText;
            IsVisionConnected = true;
            IsVirtualMouseIsolated = isolated;
            ActiveSceneBadge = sceneBadge;
            UpdateViewportBadge(hello.ScreenWidth, hello.ScreenHeight);

            if (isInitialHandshake)
            {
                ViewportPresetChanged?.Invoke();
                AddLog($"[VisionBridge] Handshake riuscito! Protocollo {hello.ProtocolVersion}, Unity {hello.UnityVersion}");
                AddLog($"[VisionBridge] Capabilities attive: {capsText}");
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

    private void StopVisionBridgeMonitoring()
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

            var pen = new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 2); // #38BDF8 cyan
            var fillBrush = new SolidColorBrush(Color.FromArgb(35, 56, 189, 248));
            var font = new Typeface("Segoe UI");

            foreach (var elem in elements)
            {
                if (!elem.Visible) continue;
                var rect = new Rect(elem.PixelRect.X, elem.PixelRect.Y, elem.PixelRect.Width, elem.PixelRect.Height);
                dc.DrawRectangle(fillBrush, pen, rect);

                string label = $"{elem.Type}: {(string.IsNullOrEmpty(elem.Text) ? elem.Id : elem.Text)}";
                if (label.Length > 28) label = string.Concat(label.AsSpan(0, 28), "...");

                var formattedText = new FormattedText(
                    label,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    font,
                    12,
                    Brushes.Yellow,
                    1.0);

                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(190, 0, 0, 0)), null, new Rect(rect.X, Math.Max(0, rect.Y - 18), formattedText.Width + 6, 18));
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
            IsVisionConnected = false;
            IsVirtualMouseIsolated = false;
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
        _resizeDebounceCts?.Cancel();
        _resizeDebounceCts?.Dispose();
        _resizeDebounceCts = null;
        StopVisionBridgeMonitoring();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
