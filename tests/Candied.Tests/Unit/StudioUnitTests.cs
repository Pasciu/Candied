#if WINDOWS
using System;
using System.IO;
using System.Threading.Tasks;
using Candied.Studio.Native;
using Candied.Studio.Services;
using Candied.Studio.ViewModels;
using Candied.Vision.Hosting;
using Candied.Vision.Protocol;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class StudioUnitTests
{
    [Fact]
    public void Win32WindowEmbedder_CalculateChildWindowStyle_StripsTopLevelAndAppliesChildStyles()
    {
        var embedder = new Win32WindowEmbedder();

        int originalStyle = Win32Native.WS_POPUP | Win32Native.WS_CAPTION | Win32Native.WS_THICKFRAME
            | Win32Native.WS_MINIMIZEBOX | Win32Native.WS_MAXIMIZEBOX;

        int newStyle = embedder.CalculateChildWindowStyle(originalStyle);

        Assert.Equal(0, newStyle & Win32Native.WS_POPUP);
        Assert.Equal(0, newStyle & Win32Native.WS_CAPTION);
        Assert.Equal(0, newStyle & Win32Native.WS_THICKFRAME);
        Assert.Equal(0, newStyle & Win32Native.WS_MINIMIZEBOX);
        Assert.Equal(0, newStyle & Win32Native.WS_MAXIMIZEBOX);

        Assert.NotEqual(0, newStyle & Win32Native.WS_CHILD);
        Assert.NotEqual(0, newStyle & Win32Native.WS_VISIBLE);
        Assert.NotEqual(0, newStyle & Win32Native.WS_CLIPCHILDREN);
    }

    [Fact]
    public void Win32WindowEmbedder_And_WindowsJobObject_HandleInvalidInputsSafely()
    {
        var embedder = new Win32WindowEmbedder();
        Assert.False(embedder.EmbedWindow(IntPtr.Zero, new IntPtr(123), 800, 600));
        Assert.False(embedder.EmbedWindow(new IntPtr(123), IntPtr.Zero, 800, 600));
        Assert.False(embedder.ResizeEmbeddedWindow(IntPtr.Zero, 800, 600));

        using var job = new WindowsJobObject();
        Assert.True(job.Initialize());
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
    public void Win32WindowLocator_TracksCurrentProcessTreeAndHandlesZeroInputs()
    {
        var locator = new Win32WindowLocator();
        Assert.Equal(IntPtr.Zero, locator.FindChildWindow(IntPtr.Zero));
        Assert.Equal(IntPtr.Zero, locator.FindWindowForProcessTree(0));
        Assert.Contains(Environment.ProcessId, locator.GetProcessTreeIds(Environment.ProcessId));
    }

    [Fact]
    public async Task TargetHostController_AcceptsAnyExecutableOrEmulatorWithoutEngineRestriction()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Candied_HostCtrl_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using var job = new WindowsJobObject();
            var locator = new Win32WindowLocator();
            var embedder = new Win32WindowEmbedder();
            using var controller = new TargetHostController(job, locator, embedder);

            // Non-existent path fails cleanly
            var missing = controller.SelectTarget(Path.Combine(tempDir, "Missing.exe"));
            Assert.True(missing.IsFailure);

            // Any PC executable (including future IL2CPP, Godot, C++) or Emulator + ROM succeeds!
            string anyExe = Path.Combine(tempDir, "Il2CppOrCustomGame.exe");
            File.WriteAllText(anyExe, "MZ");

            var selected = controller.SelectTarget(anyExe);
            Assert.True(selected.IsSuccess);
            Assert.Equal("Il2CppOrCustomGame", controller.CurrentTargetName);

            // Launch with zero HWND fails gracefully without spawning
            bool launchedZero = await controller.LaunchAsync(IntPtr.Zero, 1280, 720);
            Assert.False(launchedZero);

            // DispatchBaseWindowInput without embedded window returns structured failure
            var inputResp = controller.DispatchBaseWindowInput(new VisionInputRequest { Channel = VisionConstants.ChannelKeyboard, Key = "Enter" });
            Assert.False(inputResp.Success);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Theory]
    [InlineData("auto_16_9", 1000, 700, 1.0, 1000, 562)]
    [InlineData("960x540", 1400, 900, 1.0, 960, 540)]
    [InlineData("1280x720", 1600, 1000, 1.0, 1280, 720)]
    [InlineData("1920x1080", 2000, 1200, 1.0, 1920, 1080)]
    public void ViewportCalculator_MaintainsStrict16By9AspectRatio(
        string presetKey,
        double availDipW,
        double availDipH,
        double dpiScale,
        int expectedPxW,
        int expectedPxH)
    {
        var (dipW, dipH, pxW, pxH) = ViewportCalculator.CalculateStageViewport(availDipW, availDipH, dpiScale, dpiScale, presetKey);
        Assert.Equal(expectedPxW, pxW);
        Assert.Equal(expectedPxH, pxH);
        Assert.Equal(expectedPxW / dpiScale, dipW, 2);
        Assert.Equal(expectedPxH / dpiScale, dipH, 2);
    }

    [Fact]
    public void StudioLogEntry_ClassifiesSeverityAndFiltersAccurately()
    {
        var now = new DateTime(2026, 11, 26, 18, 30, 0);
        var errEntry = StudioLogEntry.Create("[Error  : Runtime] NullReferenceException", now);
        var sensorEntry = StudioLogEntry.Create("[SensorBridge] Handshake v2.0.0 riuscito", now);

        Assert.Equal("Error", errEntry.Level);
        Assert.True(errEntry.IsWarningOrError);
        Assert.Equal("Bridge", sensorEntry.Level);
        Assert.False(sensorEntry.IsWarningOrError);
        Assert.True(errEntry.MatchesFilter("NullReference", onlyWarningsAndErrors: true));
    }
}
#endif
