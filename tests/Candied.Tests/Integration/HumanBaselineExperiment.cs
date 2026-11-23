using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Ports;
using Candied.Decompiler;
using Candied.Studio.Native;
using Candied.Studio.Services;
using Candied.Tests.Helpers;
using Candied.VisionBridge.Protocol;
using Candied.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Candied.Tests.Integration;

public class HumanBaselineExperiment
{
    private readonly ITestOutputHelper _output;
    private static readonly string ArtifactDir =
        Environment.GetEnvironmentVariable("CANDIED_TEST_ARTIFACT_DIR")
        ?? Path.Combine(Path.GetTempPath(), "candied-test-artifacts");
    private static readonly Rectangle TargetRoi = new Rectangle(300, 300, 424, 300);

    public HumanBaselineExperiment(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Explicit interactive baseline test against clean copy")]
    public async Task Run_HumanBaseline_Embedded()
    {
        string gamePath = Environment.GetEnvironmentVariable("CANDIED_TEST_GAME_EXE") ?? string.Empty;
        if (!File.Exists(gamePath))
        {
            _output.WriteLine($"Clean copy not found at {gamePath}.");
            return;
        }

        var services = new ServiceCollection();
        services.AddDecompilerInfrastructure();
        services.AddWorkspaceInfrastructure();
        services.AddSingleton<IWindowsJobObject, WindowsJobObject>();
        services.AddSingleton<IUnityWindowLocator, UnityWindowLocator>();
        services.AddSingleton<IWindowEmbedder, Win32WindowEmbedder>();
        services.AddSingleton<IUnityGameHostController, UnityGameHostController>();

        using var sp = services.BuildServiceProvider();
        var controller = sp.GetRequiredService<IUnityGameHostController>();
        var visionClient = sp.GetRequiredService<IVisionBridgeClient>();

        int panelWidth = 1280;
        int panelHeight = 720;
        int targetGameWidth = 1024;
        int targetGameHeight = 720;

        var hostReadyEvent = new ManualResetEvent(false);
        IntPtr hostHwnd = IntPtr.Zero;
        bool stopPump = false;

        var pumpThread = new Thread(() =>
        {
            hostHwnd = Win32Native.CreateWindowEx(
                0,
                "STATIC",
                "CandiedHost_HumanBaseline",
                Win32Native.WS_POPUP | Win32Native.WS_VISIBLE | Win32Native.WS_CLIPCHILDREN,
                0, 0, targetGameWidth, targetGameHeight,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            hostReadyEvent.Set();

            while (!stopPump)
            {
                while (Win32Native.PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1 /* PM_REMOVE */))
                {
                    _ = Win32Native.TranslateMessage(ref msg);
                    Win32Native.DispatchMessage(ref msg);
                }
                Thread.Sleep(10);
            }
        });
        pumpThread.SetApartmentState(ApartmentState.STA);
        pumpThread.IsBackground = true;
        pumpThread.Start();

        hostReadyEvent.WaitOne(5000);

        try
        {
            _ = await controller.ValidateAndSelectGameAsync(gamePath);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            bool launched = await controller.LaunchAsync(hostHwnd, targetGameWidth, targetGameHeight, cts.Token);
            Assert.True(launched);

            int port = controller.ActiveVisionBridgePort!.Value;
            string token = controller.ActiveVisionBridgeToken!;
            string baseUrl = controller.ActiveVisionBridgeBaseUrl!;

            // Poll /hello
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000);
                var hello = await visionClient.GetHelloAsync(baseUrl, token);
                if (hello.IsSuccess) break;
            }

            // Stabilize scene
            await Task.Delay(25000);

            // Resize and letterbox host
            Win32Native.SetWindowPos(hostHwnd, IntPtr.Zero, 0, 0, panelWidth, panelHeight, Win32Native.SWP_NOZORDER | Win32Native.SWP_SHOWWINDOW);
            controller.Resize(panelWidth, panelHeight, targetGameWidth, targetGameHeight);
            await Task.Delay(1000);

            // 1. Pre-human-click screenshot
            var preSc = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(preSc.IsSuccess);
            string prePath = Path.Combine(ArtifactDir, "baseline_01_pre_human_embedded.png");
            File.WriteAllBytes(prePath, preSc.Value.PngBytes);
            _output.WriteLine($"[Saved Pre Screenshot] {prePath} ({preSc.Value.PngBytes.Length} bytes)");

            _output.WriteLine("\n====================================================================");
            _output.WriteLine(">>> FINESTRA EMBEDDATA VISIBILE SU SCHERMO! <<<");
            _output.WriteLine("CLICCA A MANO CON IL MOUSE FISICO SUL GIOCO EMBEDDATO (20 secondi)...");
            _output.WriteLine("====================================================================");

            for (int s = 20; s > 0; s--)
            {
                if (s % 5 == 0) _output.WriteLine($"Tempo rimanente per il click umano: {s}s...");
                await Task.Delay(1000);
            }

            // 2. Post-human-click screenshot
            var postSc = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(postSc.IsSuccess);
            string postPath = Path.Combine(ArtifactDir, "baseline_01_post_human_embedded.png");
            File.WriteAllBytes(postPath, postSc.Value.PngBytes);
            _output.WriteLine($"[Saved Post Screenshot] {postPath} ({postSc.Value.PngBytes.Length} bytes)");

            // 3. Objective Evaluation
            var diffRes = PixelDiffEvaluator.Evaluate(preSc.Value.PngBytes, postSc.Value.PngBytes, TargetRoi, requiredPercentage: 15.0);
            _output.WriteLine($"\n[HUMAN BASELINE EMBEDDED RESULT] {diffRes.Summary}");
        }
        finally
        {
            await controller.StopAsync();
            stopPump = true;
            Win32Native.DestroyWindow(hostHwnd);
        }
    }

    [Fact(Skip = "Explicit interactive baseline test against clean copy")]
    public async Task Run_HumanBaseline_Standalone()
    {
        string gamePath = Environment.GetEnvironmentVariable("CANDIED_TEST_GAME_EXE") ?? string.Empty;
        if (!File.Exists(gamePath))
        {
            _output.WriteLine($"Clean copy not found at {gamePath}.");
            return;
        }

        int port = UnityGameHostController.GetAvailableTcpPort();
        string token = Guid.NewGuid().ToString("N");
        string baseUrl = $"http://127.0.0.1:{port}";

        var psi = new ProcessStartInfo
        {
            FileName = gamePath,
            Arguments = "-popupwindow -screen-fullscreen 0 -screen-width 1024 -screen-height 720",
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(gamePath)
        };
        psi.EnvironmentVariables["CANDIED_PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        psi.EnvironmentVariables["CANDIED_TOKEN"] = token;

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var services = new ServiceCollection();
        services.AddWorkspaceInfrastructure();
        using var sp = services.BuildServiceProvider();
        var visionClient = sp.GetRequiredService<IVisionBridgeClient>();

        try
        {
            // Poll /hello
            for (int i = 0; i < 30; i++)
            {
                await Task.Delay(1000);
                var hello = await visionClient.GetHelloAsync(baseUrl, token);
                if (hello.IsSuccess) break;
            }

            // Stabilize scene
            await Task.Delay(25000);

            // 1. Pre-human-click screenshot
            var preSc = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(preSc.IsSuccess);
            string prePath = Path.Combine(ArtifactDir, "baseline_02_pre_human_standalone.png");
            File.WriteAllBytes(prePath, preSc.Value.PngBytes);
            _output.WriteLine($"[Saved Pre Screenshot] {prePath} ({preSc.Value.PngBytes.Length} bytes)");

            _output.WriteLine("\n====================================================================");
            _output.WriteLine(">>> FINESTRA STANDALONE VISIBILE SU SCHERMO! <<<");
            _output.WriteLine("CLICCA A MANO CON IL MOUSE FISICO SUL GIOCO STANDALONE (20 secondi)...");
            _output.WriteLine("====================================================================");

            for (int s = 20; s > 0; s--)
            {
                if (s % 5 == 0) _output.WriteLine($"Tempo rimanente per il click umano: {s}s...");
                await Task.Delay(1000);
            }

            // 2. Post-human-click screenshot
            var postSc = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(postSc.IsSuccess);
            string postPath = Path.Combine(ArtifactDir, "baseline_02_post_human_standalone.png");
            File.WriteAllBytes(postPath, postSc.Value.PngBytes);
            _output.WriteLine($"[Saved Post Screenshot] {postPath} ({postSc.Value.PngBytes.Length} bytes)");

            // 3. Objective Evaluation
            var diffRes = PixelDiffEvaluator.Evaluate(preSc.Value.PngBytes, postSc.Value.PngBytes, TargetRoi, requiredPercentage: 15.0);
            _output.WriteLine($"\n[HUMAN BASELINE STANDALONE RESULT] {diffRes.Summary}");
        }
        finally
        {
            try { process.Kill(true); } catch { }
        }
    }
}
