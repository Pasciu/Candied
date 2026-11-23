using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.Studio.Native;
using Candied.Studio.Services;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class StudioUnitTests
{
    [Fact]
    public void Win32WindowEmbedder_CalculateChildWindowStyle_StripsTopLevelAndAppliesChildStyles()
    {
        var embedder = new Win32WindowEmbedder();

        // Sample top-level style with popup, caption, thick frame and min/max boxes
        int originalStyle = Win32Native.WS_POPUP | Win32Native.WS_CAPTION | Win32Native.WS_THICKFRAME
            | Win32Native.WS_MINIMIZEBOX | Win32Native.WS_MAXIMIZEBOX;

        int newStyle = embedder.CalculateChildWindowStyle(originalStyle);

        // Stripped styles must be false
        Assert.Equal(0, newStyle & Win32Native.WS_POPUP);
        Assert.Equal(0, newStyle & Win32Native.WS_CAPTION);
        Assert.Equal(0, newStyle & Win32Native.WS_THICKFRAME);
        Assert.Equal(0, newStyle & Win32Native.WS_MINIMIZEBOX);
        Assert.Equal(0, newStyle & Win32Native.WS_MAXIMIZEBOX);

        // Added styles must be true
        Assert.NotEqual(0, newStyle & Win32Native.WS_CHILD);
        Assert.NotEqual(0, newStyle & Win32Native.WS_VISIBLE);
        Assert.NotEqual(0, newStyle & Win32Native.WS_CLIPCHILDREN);
    }

    [Fact]
    public void Win32WindowEmbedder_InvalidHandles_ReturnFalseSafely()
    {
        var embedder = new Win32WindowEmbedder();

        Assert.False(embedder.EmbedWindow(IntPtr.Zero, new IntPtr(123), 800, 600));
        Assert.False(embedder.EmbedWindow(new IntPtr(123), IntPtr.Zero, 800, 600));
        Assert.False(embedder.ResizeEmbeddedWindow(IntPtr.Zero, 800, 600));
    }

    [Fact]
    public void WindowsJobObject_InitializeAndDispose_BehavesCorrectly()
    {
        using var job = new WindowsJobObject();

        Assert.False(job.IsInitialized);
        bool initialized = job.Initialize();
        Assert.True(initialized);
        Assert.True(job.IsInitialized);

        // Calling Initialize again is idempotent
        Assert.True(job.Initialize());

        // Assign null process returns false
        Assert.False(job.AssignProcess(null!));
        Assert.False(job.AssignProcess(IntPtr.Zero));
    }

    [Fact]
    public void Win32Native_CaptureWindowPng_ReturnsValidPngBytes()
    {
        IntPtr hwnd = Win32Native.CreateWindowEx(
            0,
            "STATIC",
            "CaptureTestWindow",
            Win32Native.WS_CAPTION | Win32Native.WS_VISIBLE,
            100, 100, 400, 300,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        Assert.NotEqual(IntPtr.Zero, hwnd);
        try
        {
            byte[] png = Win32Native.CaptureWindowPng(hwnd);
            Assert.NotEmpty(png);
        }
        finally
        {
            Win32Native.DestroyWindow(hwnd);
        }
    }

    [Fact]
    public void UnityWindowLocator_InvalidInputs_ReturnsZero()
    {
        var locator = new UnityWindowLocator();

        Assert.Equal(IntPtr.Zero, locator.FindChildWindow(IntPtr.Zero));
        Assert.Equal(IntPtr.Zero, locator.FindUnityWindowForProcessTree(0));
        Assert.Equal(IntPtr.Zero, locator.FindUnityWindowForProcessTree(-1));
    }

    [Fact]
    public void UnityWindowLocator_GetProcessTreeIds_ContainsCurrentProcess()
    {
        var locator = new UnityWindowLocator();
        int currentPid = Environment.ProcessId;

        var tree = locator.GetProcessTreeIds(currentPid);

        Assert.NotNull(tree);
        Assert.Contains(currentPid, tree);
    }

    [Fact]
    public void UnityGameHostController_BuildLaunchArguments_FormatsCorrectlyWithMinBounds()
    {
        var hwnd = new IntPtr(0x12345678);
        string args = UnityGameHostController.BuildLaunchArguments(hwnd, 100, 100);

        Assert.Contains($"-parentHWND {hwnd.ToInt64()}", args);
        Assert.Contains("-popupwindow", args);
        Assert.Contains("-screen-fullscreen 0", args);
        // Minimum width and height clamping (min 320x240)
        Assert.Contains("-screen-width 320", args);
        Assert.Contains("-screen-height 240", args);

        string argsLarger = UnityGameHostController.BuildLaunchArguments(hwnd, 1920, 1080);
        Assert.Contains("-screen-width 1920", argsLarger);
        Assert.Contains("-screen-height 1080", argsLarger);
    }

    [Fact]
    public async Task UnityGameHostController_ValidateAndSelectGame_FailsIfNonExistent()
    {
        var detector = new FakeGameDetector(null);
        var job = new WindowsJobObject();
        var locator = new UnityWindowLocator();
        var embedder = new Win32WindowEmbedder();

        var controller = new UnityGameHostController(detector, job, locator, embedder);

        var result = await controller.ValidateAndSelectGameAsync("C:\\NonExistent\\Game.exe");

        Assert.True(result.IsFailure);
        Assert.Contains("non esiste", result.Error);
    }

    [Fact]
    public async Task UnityGameHostController_ValidateAndSelectGame_RejectsIl2CppWithExactMessage()
    {
        // Create a temporary dummy file to satisfy File.Exists
        string tempFile = Path.GetTempFileName();
        try
        {
            var il2CppEnvironment = new GameEnvironment(
                Id: new GameId("DummyIl2Cpp"),
                GameName: "DummyIl2Cpp",
                GameRootPath: Path.GetDirectoryName(tempFile)!,
                ManagedPath: Path.Combine(Path.GetDirectoryName(tempFile)!, "Dummy_Data", "Managed"),
                EngineType: UnityEngineType.Il2Cpp,
                Architecture: "x64",
                TargetFramework: null,
                BepInExInfo: new BepInExInstallation(false),
                DiscoveredAssemblies: Array.Empty<DiscoveredAssembly>(),
                EnvironmentWarnings: Array.Empty<string>());

            var detector = new FakeGameDetector(Result.Ok<GameEnvironment, DetectionError>(il2CppEnvironment));
            var job = new WindowsJobObject();
            var locator = new UnityWindowLocator();
            var embedder = new Win32WindowEmbedder();

            var controller = new UnityGameHostController(detector, job, locator, embedder);

            var result = await controller.ValidateAndSelectGameAsync(tempFile);

            Assert.True(result.IsFailure);
            Assert.Equal("Il gioco selezionato utilizza il runtime Unity IL2CPP. Candied supporta esclusivamente giochi Unity Mono.", result.Error);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task UnityGameHostController_LaunchAsync_FailsIfNoGameOrInvalidContainer()
    {
        var detector = new FakeGameDetector(null);
        var job = new WindowsJobObject();
        var locator = new UnityWindowLocator();
        var embedder = new Win32WindowEmbedder();

        var controller = new UnityGameHostController(detector, job, locator, embedder);

        // No game selected
        bool launchedNoGame = await controller.LaunchAsync(new IntPtr(100), 800, 600);
        Assert.False(launchedNoGame);

        // Select valid Mono game
        string tempFile = Path.GetTempFileName();
        try
        {
            var monoEnvironment = new GameEnvironment(
                Id: new GameId("DummyMono"),
                GameName: "DummyMono",
                GameRootPath: Path.GetDirectoryName(tempFile)!,
                ManagedPath: Path.Combine(Path.GetDirectoryName(tempFile)!, "Dummy_Data", "Managed"),
                EngineType: UnityEngineType.Mono,
                Architecture: "x64",
                TargetFramework: "4.7.1",
                BepInExInfo: new BepInExInstallation(false),
                DiscoveredAssemblies: Array.Empty<DiscoveredAssembly>(),
                EnvironmentWarnings: Array.Empty<string>());

            detector = new FakeGameDetector(Result.Ok<GameEnvironment, DetectionError>(monoEnvironment));
            controller = new UnityGameHostController(detector, job, locator, embedder);
            var selectResult = await controller.ValidateAndSelectGameAsync(tempFile);
            Assert.True(selectResult.IsSuccess);

            // Container HWND is Zero
            bool launchedZeroHwnd = await controller.LaunchAsync(IntPtr.Zero, 800, 600);
            Assert.False(launchedZeroHwnd);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void UnityGameHostController_BuildLaunchArguments_AlwaysUsesDefault16By9Resolution()
    {
        var args = UnityGameHostController.BuildLaunchArguments(new IntPtr(12345));
        Assert.Contains("-screen-width 1280", args);
        Assert.Contains("-screen-height 720", args);
        Assert.Contains("-parentHWND 12345", args);
        Assert.Contains("-screen-fullscreen 0", args);
    }

    [Theory]
    [InlineData("auto_16_9", 1000, 700, 1.0, 1000, 562)]
    [InlineData("960x540", 1400, 900, 1.0, 960, 540)]
    [InlineData("1280x720", 1600, 1000, 1.0, 1280, 720)]
    [InlineData("1920x1080", 2000, 1200, 1.0, 1920, 1080)]
    public void ViewportCalculator_CalculateStageViewport_MaintainsStrict16By9AspectRatio(
        string presetKey,
        double availDipW,
        double availDipH,
        double dpiScale,
        int expectedPxW,
        int expectedPxH)
    {
        var (dipW, dipH, pxW, pxH) = ViewportCalculator.CalculateStageViewport(
            availDipW,
            availDipH,
            dpiScale,
            dpiScale,
            presetKey);

        Assert.Equal(expectedPxW, pxW);
        Assert.Equal(expectedPxH, pxH);
        Assert.Equal(expectedPxW / dpiScale, dipW, 2);
        Assert.Equal(expectedPxH / dpiScale, dipH, 2);

        double aspect = (double)pxW / pxH;
        Assert.InRange(aspect, 1.77, 1.785);
    }

    [Fact]
    public void ViewportCalculator_MapToReference1080p_ScalesFromCompact16By9To1920x1080()
    {
        // Center of 960x540 (480, 270) maps to (960, 540) in 1920x1080
        var (refX, refY) = ViewportCalculator.MapToReference1080p(480, 270, 960, 540);
        Assert.Equal(960, refX);
        Assert.Equal(540, refY);

        // Center of 1280x720 (640, 360) maps to (960, 540) in 1920x1080
        var (refX2, refY2) = ViewportCalculator.MapToReference1080p(640, 360, 1280, 720);
        Assert.Equal(960, refX2);
        Assert.Equal(540, refY2);
    }

    [Fact]
    public void ViewportCalculator_ImageControlToFrameCoordinates_MapsUniformImageAndRejectsLetterboxBars()
    {
        // 400x300 container holding a 1280x720 (16:9) bitmap -> letterboxed to 400x225 at Y=37
        var (fx, fy, rx, ry, inBounds) = ViewportCalculator.ImageControlToFrameCoordinates(
            controlX: 200,
            controlY: 150,
            controlWidth: 400,
            controlHeight: 300,
            bitmapPixelWidth: 1280,
            bitmapPixelHeight: 720);

        Assert.True(inBounds);
        Assert.InRange(fx, 638, 642);
        Assert.InRange(fy, 358, 362);
        Assert.InRange(rx, 957, 963);
        Assert.InRange(ry, 537, 543);

        // Click on top letterbox bar (Y=5) is out of bounds
        var (_, _, _, _, outBounds) = ViewportCalculator.ImageControlToFrameCoordinates(
            controlX: 200,
            controlY: 5,
            controlWidth: 400,
            controlHeight: 300,
            bitmapPixelWidth: 1280,
            bitmapPixelHeight: 720);
        Assert.False(outBounds);
    }

    [Fact]
    public void StudioLogEntry_ClassifiesSeverityAndFiltersAccurately()
    {
        var now = new DateTime(2026, 11, 18, 16, 30, 0);
        var errEntry = Candied.Studio.ViewModels.StudioLogEntry.Create("[Error  : Unity Log] NullReferenceException", now);
        var warnEntry = Candied.Studio.ViewModels.StudioLogEntry.Create("[Warning: BepInEx] Patch timeout", now);
        var bridgeEntry = Candied.Studio.ViewModels.StudioLogEntry.Create("[VisionBridge] Handshake riuscito", now);
        var infoEntry = Candied.Studio.ViewModels.StudioLogEntry.Create("[Info   :   BepInEx] Loading NPCDesigner", now);

        Assert.Equal("Error", errEntry.Level);
        Assert.True(errEntry.IsWarningOrError);
        Assert.Equal("Warning", warnEntry.Level);
        Assert.True(warnEntry.IsWarningOrError);
        Assert.Equal("Bridge", bridgeEntry.Level);
        Assert.False(bridgeEntry.IsWarningOrError);
        Assert.Equal("Info", infoEntry.Level);
        Assert.False(infoEntry.IsWarningOrError);

        Assert.True(errEntry.MatchesFilter("NullReference", onlyWarningsAndErrors: true));
        Assert.False(infoEntry.MatchesFilter(null, onlyWarningsAndErrors: true));
        Assert.True(infoEntry.MatchesFilter("NPCDesigner", onlyWarningsAndErrors: false));
        Assert.False(infoEntry.MatchesFilter("OtherMod", onlyWarningsAndErrors: false));
    }

    private sealed class FakeGameDetector : IGameDetector
    {
        private readonly Result<GameEnvironment, DetectionError>? _result;

        public FakeGameDetector(Result<GameEnvironment, DetectionError>? result)
        {
            _result = result;
        }

        public Task<Result<GameEnvironment, DetectionError>> DetectGameAsync(string gameRootPath, CancellationToken ct = default)
        {
            if (_result.HasValue)
            {
                return Task.FromResult(_result.Value);
            }

            return Task.FromResult(Result.Fail<GameEnvironment, DetectionError>(
                new DetectionError("Not found")));
        }
    }
}
