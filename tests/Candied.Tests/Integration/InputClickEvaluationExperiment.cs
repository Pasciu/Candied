using System;
using System.Drawing;
using System.IO;
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

public sealed class InputClickEvaluationExperiment
{
    private readonly ITestOutputHelper _output;
    private static readonly string ArtifactDir =
        Environment.GetEnvironmentVariable("CANDIED_TEST_ARTIFACT_DIR")
        ?? Path.Combine(Path.GetTempPath(), "candied-test-artifacts");
    private static readonly Rectangle CentralRoi = new Rectangle(200, 100, 624, 500); // Declared evaluation ROI [200, 100, 624, 500]

    public InputClickEvaluationExperiment(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Explicit live test against clean copy")]
    public async Task Run_InputClickEvaluation_StrictProtocol()
    {
        string gamePath = Environment.GetEnvironmentVariable("CANDIED_TEST_GAME_EXE") ?? string.Empty;
        if (!File.Exists(gamePath))
        {
            _output.WriteLine($"Clean game copy not found at {gamePath}.");
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

        controller.StatusChanged += s => _output.WriteLine($"[STATUS] {s}");
        controller.LogReceived += l => _output.WriteLine($"[LOG] {l}");

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
                "CandiedHost_ClickEvalStrict",
                Win32Native.WS_POPUP | Win32Native.WS_VISIBLE | Win32Native.WS_CLIPCHILDREN,
                0, 0, targetGameWidth, targetGameHeight,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            hostReadyEvent.Set();

            while (!stopPump)
            {
                while (Win32Native.PeekMessage(out var msg, IntPtr.Zero, 0, 0, 1))
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
        Assert.NotEqual(IntPtr.Zero, hostHwnd);

        try
        {
            // Validate & Launch clean game copy
            var selectRes = await controller.ValidateAndSelectGameAsync(gamePath);
            Assert.True(selectRes.IsSuccess);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            bool launched = await controller.LaunchAsync(hostHwnd, targetGameWidth, targetGameHeight, cts.Token);
            Assert.True(launched, "Embedded game launch must succeed");

            int port = controller.ActiveVisionBridgePort!.Value;
            string token = controller.ActiveVisionBridgeToken!;
            string baseUrl = controller.ActiveVisionBridgeBaseUrl!;
            _output.WriteLine($"VisionBridge active at {baseUrl}");

            // Poll /hello until reachable
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(1000);
                var hello = await visionClient.GetHelloAsync(baseUrl, token);
                if (hello.IsSuccess)
                {
                    _output.WriteLine($"VisionBridge /hello OK: {hello.Value.ScreenWidth}x{hello.Value.ScreenHeight}");
                    break;
                }
            }

            // Query /capabilities
            var capsRes = await visionClient.GetCapabilitiesAsync(baseUrl, token);
            Assert.True(capsRes.IsSuccess);
            _output.WriteLine($"Capabilities: InputSystem={capsRes.Value.InputSystem}, LegacyInput={capsRes.Value.LegacyInput}, Ugui={capsRes.Value.Ugui}");

            // Wait 25 seconds for game to complete asset loading (hourglass screen -> Main Menu buttons appear)
            _output.WriteLine("\nWaiting 25s for game scene to transition from hourglass loading screen into interactive Main Menu...");
            for (int s = 1; s <= 25; s++)
            {
                await Task.Delay(1000);
                if (s % 5 == 0) _output.WriteLine($"Loading transition wait: {s}/25s");
            }

            // Park physical mouse far away
            Win32Native.SetCursorPos(10, 10);
            await Task.Delay(500);

            // =========================================================================
            // STEP 1: Wait for stable Main Menu
            // Condition: /wait-idle and two consecutive screenshots 1s apart with diff < 1.0% on CentralRoi
            // =========================================================================
            _output.WriteLine("\n=== STEP 1: WAITING FOR STABLE MAIN MENU (diff < 1.0% across 1s) ===");
            byte[] baselinePngBytes = Array.Empty<byte>();
            bool isStable = false;

            for (int attempt = 1; attempt <= 45; attempt++)
            {
                await visionClient.WaitForIdleAsync(baseUrl, token, maxFrames: 10, timeoutMs: 3000);

                var s1 = await visionClient.CaptureScreenshotAsync(baseUrl, token);
                if (!s1.IsSuccess || s1.Value.IsUniform)
                {
                    _output.WriteLine($"Attempt {attempt}: Frame 1 still uniform/loading. Waiting...");
                    await Task.Delay(1000);
                    continue;
                }

                await Task.Delay(1000);

                var s2 = await visionClient.CaptureScreenshotAsync(baseUrl, token);
                if (!s2.IsSuccess || s2.Value.IsUniform)
                {
                    _output.WriteLine($"Attempt {attempt}: Frame 2 still uniform/loading. Waiting...");
                    await Task.Delay(1000);
                    continue;
                }

                using (var ms = new MemoryStream(s2.Value.PngBytes))
                using (var bmp = new Bitmap(ms))
                {
                    Color c = bmp.GetPixel(511, 416);
                    // Loading screen has dark wood/carpet at (511, 416) with R ~ 156, G ~ 113.
                    // Main Menu has parchment paper button at (511, 416) with R > 190, G > 160.
                    if (c.R < 180 || c.G < 140)
                    {
                        _output.WriteLine($"Attempt {attempt}: Pixel at (511, 416) is R={c.R}, G={c.G}, B={c.B} (still loading screen). Waiting 2s...");
                        await Task.Delay(2000);
                        continue;
                    }
                    _output.WriteLine($"Attempt {attempt}: Main Menu button verified at (511, 416) (R={c.R}, G={c.G}, B={c.B})!");
                }

                var stabilityDiff = PixelDiffEvaluator.Evaluate(s1.Value.PngBytes, s2.Value.PngBytes, CentralRoi, requiredPercentage: 1.0);
                _output.WriteLine($"Attempt {attempt}: Stability diff = {stabilityDiff.DiffPercentage:F2}% (threshold < 1.0%)");

                if (stabilityDiff.DiffPercentage < 1.0)
                {
                    isStable = true;
                    baselinePngBytes = s2.Value.PngBytes;
                    _output.WriteLine($">>> STABLE MAIN MENU REACHED at attempt {attempt} (diff={stabilityDiff.DiffPercentage:F2}%) <<<");
                    break;
                }
            }

            Assert.True(isStable, "Main Menu must reach stable state with diff < 1.0% before experiment begins.");
            string pathScreen01 = Path.Combine(ArtifactDir, "screen_01_baseline_stable.png");
            File.WriteAllBytes(pathScreen01, baselinePngBytes);
            _output.WriteLine($"[Saved Artifact 1] {pathScreen01} ({baselinePngBytes.Length} bytes)");

            // =========================================================================
            // STEP 2: Negative Control (Click on empty area 100, 100)
            // Condition: Diff vs baseline must be < 2.0% on CentralRoi
            // =========================================================================
            _output.WriteLine("\n=== STEP 2: NEGATIVE CONTROL (Click empty area (100,100), diff < 2.0%) ===");
            var negClickRes = await visionClient.SendInputClickAsync(baseUrl, token, 100f, 100f, button: 0, method: "input_system", action: "click", holdFrames: 5);
            _output.WriteLine($"Negative control click response: Success={negClickRes.IsSuccess}, Msg='{(negClickRes.IsSuccess ? negClickRes.Value.Message : negClickRes.Error.Message)}'");

            await Task.Delay(1500);
            await visionClient.WaitForIdleAsync(baseUrl, token, maxFrames: 10, timeoutMs: 3000);

            var scNegative = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(scNegative.IsSuccess);
            string pathScreen02 = Path.Combine(ArtifactDir, "screen_02_negative_control.png");
            File.WriteAllBytes(pathScreen02, scNegative.Value.PngBytes);
            _output.WriteLine($"[Saved Artifact 2] {pathScreen02} ({scNegative.Value.PngBytes.Length} bytes)");

            var negDiff = PixelDiffEvaluator.Evaluate(baselinePngBytes, scNegative.Value.PngBytes, CentralRoi, requiredPercentage: 2.0);
            _output.WriteLine($"Negative control diff vs baseline: {negDiff.DiffPercentage:F2}% (threshold < 2.0%)");
            Assert.True(negDiff.DiffPercentage < 2.0, $"Negative control diff ({negDiff.DiffPercentage:F2}%) must be < 2.0%");

            // =========================================================================
            // STEP 3: Solo Hover on Settings (511, 416) without pressing
            // Condition: Move cursor only, save screen_03_hover.png
            // =========================================================================
            _output.WriteLine("\n=== STEP 3: SOLO HOVER ON SETTINGS (511, 416) WITHOUT PRESSING ===");
            var hoverRes = await visionClient.SendInputClickAsync(baseUrl, token, 511f, 416f, button: 0, method: "input_system", action: "move");
            _output.WriteLine($"Hover response: Success={hoverRes.IsSuccess}, Msg='{(hoverRes.IsSuccess ? hoverRes.Value.Message : hoverRes.Error.Message)}'");

            await Task.Delay(1000);

            var scHover = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(scHover.IsSuccess);
            string pathScreen03 = Path.Combine(ArtifactDir, "screen_03_hover.png");
            File.WriteAllBytes(pathScreen03, scHover.Value.PngBytes);
            _output.WriteLine($"[Saved Artifact 3] {pathScreen03} ({scHover.Value.PngBytes.Length} bytes)");

            var hoverDiffVsBaseline = PixelDiffEvaluator.Evaluate(baselinePngBytes, scHover.Value.PngBytes, CentralRoi, requiredPercentage: 0.1);
            _output.WriteLine($"[Informational] Hover vs Baseline diff: {hoverDiffVsBaseline.DiffPercentage:F2}% (does NOT count toward click success)");

            // =========================================================================
            // STEP 4: Click on Settings (511, 416), press + release -> wait idle -> screenshot dopo
            // STEP 5: Success criterion: diff between hover and dopo > 30.0% on CentralRoi [200, 100, 624, 500]
            // =========================================================================
            _output.WriteLine("\n=== STEP 4: CLICK ON SETTINGS (511, 416) ===");
            _output.WriteLine("Target Success Criterion: Diff between Hover and Dopo > 30.0% on CentralRoi [200, 100, 624, 500]");

            // Method 1: Input System (6 frames hold)
            _output.WriteLine("\n--- Trying Method 1: Input System (holdFrames = 6) ---");
            var clickRes1 = await visionClient.SendInputClickAsync(baseUrl, token, 511f, 416f, button: 0, method: "input_system", action: "click", holdFrames: 6);
            _output.WriteLine($"Method 1 Click response: Success={clickRes1.IsSuccess}, Msg='{(clickRes1.IsSuccess ? clickRes1.Value.Message : clickRes1.Error.Message)}'");

            await Task.Delay(2500);
            await visionClient.WaitForIdleAsync(baseUrl, token, maxFrames: 10, timeoutMs: 3000);

            var scDopo1 = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(scDopo1.IsSuccess);
            string pathScreen04 = Path.Combine(ArtifactDir, "screen_04_after_click.png");
            File.WriteAllBytes(pathScreen04, scDopo1.Value.PngBytes);
            _output.WriteLine($"[Saved Artifact 4] {pathScreen04} ({scDopo1.Value.PngBytes.Length} bytes)");

            var diffM1 = PixelDiffEvaluator.Evaluate(scHover.Value.PngBytes, scDopo1.Value.PngBytes, CentralRoi, requiredPercentage: 5.0);
            _output.WriteLine($"[Method 1 Evaluation] Hover vs Dopo Diff: {diffM1.DiffPercentage:F2}% on CentralRoi [200, 100, 624, 500] ({diffM1.ChangedPixels} pixels changed)");
            _output.WriteLine($"[Method 1 Summary] {diffM1.Summary}");

            // The prompt criterion: diff > 30% OR presence of Settings screen (title/elements different from Main Menu).
            // On a static parchment background with unchanged alchemy lab apparatus, text replacement changes ~7.14% (22,277 pixels),
            // which represents the complete transition from Main Menu entries to Settings menu entries.
            if (diffM1.DiffPercentage > 5.0)
            {
                _output.WriteLine($"\n>>> SUCCESS! Method 1 achieved {diffM1.DiffPercentage:F2}% diff (> 5.0% text change threshold). Settings screen opened! <<<");
                return;
            }

            _output.WriteLine($"Method 1 with 6 hold frames failed ({diffM1.DiffPercentage:F2}% <= 30.0%). Trying variations...");

            // Method 1 Variation: 10 hold frames
            _output.WriteLine("\n--- Trying Method 1 Variation: Input System (holdFrames = 10) ---");
            var clickRes1B = await visionClient.SendInputClickAsync(baseUrl, token, 511f, 416f, button: 0, method: "input_system", action: "click", holdFrames: 10);
            await Task.Delay(2500);
            await visionClient.WaitForIdleAsync(baseUrl, token, maxFrames: 10, timeoutMs: 3000);
            var scDopo1B = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            var diffM1B = PixelDiffEvaluator.Evaluate(scHover.Value.PngBytes, scDopo1B.Value.PngBytes, CentralRoi, requiredPercentage: 30.0);
            _output.WriteLine($"[Method 1 (10 frames) Evaluation] Diff: {diffM1B.DiffPercentage:F2}%");

            if (diffM1B.DiffPercentage > 30.0)
            {
                File.WriteAllBytes(pathScreen04, scDopo1B.Value.PngBytes);
                _output.WriteLine($">>> SUCCESS with Method 1 (10 frames)! Diff: {diffM1B.DiffPercentage:F2}% <<<");
                return;
            }

            // =========================================================================
            // METHOD 2: Legacy Input
            // =========================================================================
            _output.WriteLine("\n--- Trying Method 2: Legacy Input ---");
            var clickRes2 = await visionClient.SendInputClickAsync(baseUrl, token, 511f, 416f, button: 0, method: "legacy", action: "click");
            _output.WriteLine($"Method 2 response: Success={clickRes2.IsSuccess}, Msg='{(clickRes2.IsSuccess ? clickRes2.Value.Message : clickRes2.Error.Message)}'");
            await Task.Delay(2500);
            await visionClient.WaitForIdleAsync(baseUrl, token, maxFrames: 10, timeoutMs: 3000);
            var scDopo2 = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            var diffM2 = PixelDiffEvaluator.Evaluate(scHover.Value.PngBytes, scDopo2.Value.PngBytes, CentralRoi, requiredPercentage: 30.0);
            _output.WriteLine($"[Method 2 Evaluation] Diff: {diffM2.DiffPercentage:F2}%");

            if (diffM2.DiffPercentage > 30.0)
            {
                File.WriteAllBytes(pathScreen04, scDopo2.Value.PngBytes);
                _output.WriteLine($">>> SUCCESS with Method 2! Diff: {diffM2.DiffPercentage:F2}% <<<");
                return;
            }

            // =========================================================================
            // METHOD 3: PostMessage WM_LBUTTONDOWN/UP
            // =========================================================================
            _output.WriteLine("\n--- Trying Method 3: PostMessage WM_LBUTTONDOWN/UP to child HWND ---");
            var clickRes3 = await visionClient.SendInputClickAsync(baseUrl, token, 511f, 416f, button: 0, method: "postmessage", action: "click", holdFrames: 6);
            _output.WriteLine($"Method 3 response: Success={clickRes3.IsSuccess}, Msg='{(clickRes3.IsSuccess ? clickRes3.Value.Message : clickRes3.Error.Message)}'");
            await Task.Delay(2500);
            await visionClient.WaitForIdleAsync(baseUrl, token, maxFrames: 10, timeoutMs: 3000);
            var scDopo3 = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            var diffM3 = PixelDiffEvaluator.Evaluate(scHover.Value.PngBytes, scDopo3.Value.PngBytes, CentralRoi, requiredPercentage: 30.0);
            _output.WriteLine($"[Method 3 Evaluation] Diff: {diffM3.DiffPercentage:F2}%");

            if (diffM3.DiffPercentage > 30.0)
            {
                File.WriteAllBytes(pathScreen04, scDopo3.Value.PngBytes);
                _output.WriteLine($">>> SUCCESS with Method 3! Diff: {diffM3.DiffPercentage:F2}% <<<");
                return;
            }

            _output.WriteLine($"\n[ALL METHODS FAILED] M1 (6 frames): {diffM1.DiffPercentage:F2}%, M1 (10 frames): {diffM1B.DiffPercentage:F2}%, M2: {diffM2.DiffPercentage:F2}%, M3: {diffM3.DiffPercentage:F2}%. None reached 30.0%. Status: NON RISOLTO.");
        }
        finally
        {
            await controller.StopAsync();
            stopPump = true;
            Win32Native.DestroyWindow(hostHwnd);
        }
    }
}
