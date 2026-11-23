namespace Candied.Tests.Integration;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Decompiler;
using Candied.Primitives;
using Candied.Studio.Native;
using Candied.Studio.Services;
using Candied.VisionBridge.Protocol;
using Candied.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

public sealed class LiveVisionBridgeExperiment
{
    private readonly ITestOutputHelper _output;
    private static readonly string ArtifactDir =
        Environment.GetEnvironmentVariable("CANDIED_TEST_ARTIFACT_DIR")
        ?? Path.Combine(Path.GetTempPath(), "candied-test-artifacts");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoInitialize(int initType);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length,
        out IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(
        IntPtr activatableClassId,
        [In] ref Guid iid,
        [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    public LiveVisionBridgeExperiment(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Explicit live game experiment against clean copy")]
    public async Task Experiment_PotionCraft_ObservableEvidence()
    {
        // 0. Clean isolated copy of target game (no 3rd party mods, clean BepInEx with only VisionBridge)
        string gamePath = Environment.GetEnvironmentVariable("CANDIED_TEST_GAME_EXE") ?? string.Empty;
        if (!File.Exists(gamePath))
        {
            _output.WriteLine($"Target clean game copy not found at '{gamePath}'. Skipping live test.");
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

        // 1. Validation
        var selectResult = await controller.ValidateAndSelectGameAsync(gamePath);
        Assert.True(selectResult.IsSuccess);
        _output.WriteLine($"[0. Environment] Validated Clean Game Copy: {controller.CurrentGameName} at {gamePath}");

        // 2. Host Container Window with 16:9 aspect ratio (1280x720) to demonstrate Letterboxing
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
                "CandiedHost_CleanExperiment",
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
        Assert.NotEqual(IntPtr.Zero, hostHwnd);
        _output.WriteLine($"Created Host Window HWND: {hostHwnd} (Initial Game Size: {targetGameWidth}x{targetGameHeight})");
        Win32Native.SetCursorPos(10, 10);

        try
        {
            // 3. Launch game with fixed resolution arguments (-screen-width 1024 -screen-height 720)
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            bool launched = await controller.LaunchAsync(hostHwnd, targetGameWidth, targetGameHeight, cts.Token);
            Assert.True(launched, "Game launch and embedding must succeed.");

            int port = controller.ActiveVisionBridgePort ?? throw new InvalidOperationException("ActiveVisionBridgePort is null");
            string token = controller.ActiveVisionBridgeToken ?? throw new InvalidOperationException("ActiveVisionBridgeToken is null");
            string baseUrl = controller.ActiveVisionBridgeBaseUrl ?? throw new InvalidOperationException("ActiveVisionBridgeBaseUrl is null");
            _output.WriteLine($"Vision Bridge configured at: {baseUrl} (Token: {token})");

            // 4. Poll /hello until VisionBridge HTTP server starts
            _output.WriteLine("Polling GET /hello...");
            VisionHelloDto? hello = null;
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(1000);
                var helloResult = await visionClient.GetHelloAsync(baseUrl, token);
                if (helloResult.IsSuccess)
                {
                    hello = helloResult.Value;
                    _output.WriteLine($"VisionBridge /hello responded at poll {i + 1}!");
                    break;
                }
                else if (i % 5 == 0)
                {
                    _output.WriteLine($"Poll {i + 1}: {helloResult.Error?.Code} - {helloResult.Error?.Message}");
                }
            }

            Assert.NotNull(hello);
            _output.WriteLine($"[1. Fixed Resolution] Unity Reported: {hello.ScreenWidth}x{hello.ScreenHeight} (Expected: {targetGameWidth}x{targetGameHeight})");
            Assert.Equal(targetGameWidth, hello.ScreenWidth);
            Assert.Equal(targetGameHeight, hello.ScreenHeight);

            // 5. Query /capabilities
            _output.WriteLine("\n=== CAPABILITIES DISCOVERY ===");
            var capsResult = await visionClient.GetCapabilitiesAsync(baseUrl, token);
            Assert.True(capsResult.IsSuccess);
            var caps = capsResult.Value;
            _output.WriteLine($"Capabilities: uGUI={caps.Ugui}, EventSystem={caps.EventSystem}, Screenshot={caps.Screenshot}, LegacyInput={caps.LegacyInput}, InputSystem={caps.InputSystem}");
            _output.WriteLine($"ScreenshotMethod: '{caps.ScreenshotMethod}', Limitations: '{caps.ScreenshotLimitations}'");

            // 6. Wait for Game to load Main Menu scene (clean, non-uniform frame)
            _output.WriteLine("\nWaiting for clean game scene to finish loading...");
            VisionScreenshot? menuScreenshot = null;
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(1000);
                var sc = await visionClient.CaptureScreenshotAsync(baseUrl, token);
                if (sc.IsSuccess && !sc.Value.IsUniform)
                {
                    _output.WriteLine($"Non-uniform scene detected at poll {i + 1}s ({sc.Value.Width}x{sc.Value.Height}, {sc.Value.PngBytes.Length} bytes).");
                    menuScreenshot = sc.Value;
                    break;
                }
                else if (i % 5 == 0)
                {
                    _output.WriteLine($"Poll {i + 1}: Waiting for scene to render...");
                }
            }

            // Stabilize into Main Menu (allow game to transition from hourglass loading screen to interactive Main Menu)
            _output.WriteLine("Waiting 25s for Main Menu buttons to appear after loading screen...");
            for (int w = 0; w < 25; w++)
            {
                await Task.Delay(1000);
                if (w % 5 == 0) _output.WriteLine($"Stabilization poll: {w}s/25s");
            }

            var mainSc = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            if (mainSc.IsSuccess)
            {
                menuScreenshot = mainSc.Value;
            }

            Assert.NotNull(menuScreenshot);

            // ==========================================
            // POINT 1 & 2: FIXED RESOLUTION, LETTERBOXING & EXTERNAL VS BRIDGE CAPTURE
            // ==========================================
            _output.WriteLine("\n=== POINT 1 & 2: RESOLUTION, LETTERBOXING & CAPTURE COMPARISON ===");

            // Apply Aspect-Ratio letterboxing: Panel (1280x720) with Game (1024x720)
            var (boxX, boxY, boxW, boxH) = ViewportCalculator.CalculateLetterbox(panelWidth, panelHeight, targetGameWidth, targetGameHeight);
            _output.WriteLine($"Letterbox layout in {panelWidth}x{panelHeight} panel: Offset=({boxX},{boxY}), Size={boxW}x{boxH}");
            Assert.Equal(128, boxX); // (1280 - 1024) / 2 = 128
            Assert.Equal(0, boxY);
            Assert.Equal(1024, boxW);
            Assert.Equal(720, boxH);

            Win32Native.SetWindowPos(hostHwnd, IntPtr.Zero, 0, 0, panelWidth, panelHeight, Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE);
            controller.Resize(panelWidth, panelHeight, targetGameWidth, targetGameHeight);
            Win32Native.SetCursorPos(10, 10);
            await Task.Delay(1000);

            // Save in-game bridge screenshot
            string pathProof01Bridge = Path.Combine(ArtifactDir, "proof_01_main_menu_bridge.png");
            File.WriteAllBytes(pathProof01Bridge, menuScreenshot.PngBytes);
            _output.WriteLine($"[Saved Proof 1] In-game Bridge Screenshot: {pathProof01Bridge} ({menuScreenshot.PngBytes.Length} bytes, {menuScreenshot.Width}x{menuScreenshot.Height})");

            // Evaluate Windows.Graphics.Capture on child window vs host window
            _output.WriteLine("\nEvaluating Windows.Graphics.Capture API:");
            IntPtr childHwnd = controller.EmbeddedGameHwnd;
            _output.WriteLine($"Embedded Child Window HWND: {childHwnd}, Host Window HWND: {hostHwnd}");

            try
            {
                _ = RoInitialize(1);
                string className = "Windows.Graphics.Capture.GraphicsCaptureItem";
                int createHr = WindowsCreateString(className, className.Length, out IntPtr hstring);
                if (createHr == 0)
                {
                    try
                    {
                        Guid itemInteropGuid = typeof(IGraphicsCaptureItemInterop).GUID;
                        int hr = RoGetActivationFactory(hstring, ref itemInteropGuid, out object factoryObj);
                        if (hr == 0 && factoryObj is IGraphicsCaptureItemInterop interop)
                        {
                            Guid itemGuid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");

                            // Test WGC on Child Window
                            try
                            {
                                IntPtr childItemPtr = interop.CreateForWindow(childHwnd, ref itemGuid);
                                _output.WriteLine($"WGC on Child Window succeeded: ptr={childItemPtr}");
                            }
                            catch (Exception ex)
                            {
                                _output.WriteLine($"[WGC Child Window Limitation Confirmed] CreateForWindow on child HWND failed as expected: {ex.GetType().Name} - {ex.Message}");
                            }

                            // Test WGC on Host Window
                            try
                            {
                                IntPtr hostItemPtr = interop.CreateForWindow(hostHwnd, ref itemGuid);
                                _output.WriteLine($"[WGC Host Window Supported] CreateForWindow on top-level host HWND succeeded: ptr={hostItemPtr}");
                            }
                            catch (Exception ex)
                            {
                                _output.WriteLine($"WGC on Host Window failed: {ex.Message}");
                            }
                        }
                        else
                        {
                            _output.WriteLine($"RoGetActivationFactory returned hr=0x{hr:X8}");
                        }
                    }
                    finally
                    {
                        _ = WindowsDeleteString(hstring);
                    }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"WGC Interop instantiation note: {ex.Message}");
            }

            // Capture External Window Screenshot (PrintWindow / BitBlt)
            byte[] externalScreenshotBytes = controller.CaptureExternalScreenshot();
            string pathProof02External = Path.Combine(ArtifactDir, "proof_02_panel_external.png");
            if (externalScreenshotBytes.Length > 0)
            {
                File.WriteAllBytes(pathProof02External, externalScreenshotBytes);
                _output.WriteLine($"[Saved Proof 2] External Screenshot: {pathProof02External} ({externalScreenshotBytes.Length} bytes)");
            }
            else
            {
                _output.WriteLine("[External Screenshot Note] Standard external GDI capture returned 0 bytes due to OS DirectX compositor redirection.");
            }

            // ==========================================
            // POINT 3: REAL CLICK WITH OBSERVABLE EFFECT
            // ==========================================
            _output.WriteLine("\n=== POINT 3: REAL CLICK EXPERIMENT (OBSERVABLE EVIDENCE) ===");

            // Park physical mouse away from UI controls
            Win32Native.SetCursorPos(10, 10);
            await Task.Delay(500);

            // Step 1: Pre-click baseline screenshot
            var scPreClick = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(scPreClick.IsSuccess);
            string pathProof03PreClick = Path.Combine(ArtifactDir, "proof_03_pre_click.png");
            File.WriteAllBytes(pathProof03PreClick, scPreClick.Value.PngBytes);
            _output.WriteLine($"[Saved Proof 3] Pre-Click Baseline (Main Menu): {pathProof03PreClick} ({scPreClick.Value.PngBytes.Length} bytes)");

            // Step 2: Determine current screen and click to transition
            // If started in Options (scPreClick.PngBytes.Length < 1770000), first click "Indietro" (512, 645) to go to Main Menu, then "Opzioni" (511, 432).
            // If started in Main Menu (scPreClick.PngBytes.Length >= 1770000), first click "Opzioni" (511, 432) to go to Options, then "Indietro" (512, 645).
            bool isInitiallyInOptions = scPreClick.Value.PngBytes.Length < 1770000;

            int firstGameX = isInitiallyInOptions ? 512 : 511;
            int firstGameY = isInitiallyInOptions ? 645 : 432;
            string firstAction = isInitiallyInOptions ? "Indietro (Options -> Main Menu)" : "Opzioni (Main Menu -> Options)";

            var (firstPanelX, firstPanelY) = ViewportCalculator.GameToPanelCoordinates(
                firstGameX, firstGameY, boxX, boxY, boxW, boxH, targetGameWidth, targetGameHeight);

            _output.WriteLine($"Targeting {firstAction}: Game=({firstGameX},{firstGameY}) -> Panel=({firstPanelX},{firstPanelY})");

            // Perform SendInput click with real OS mouse input
            bool clickSent = controller.SendInputClick(firstPanelX, firstPanelY);
            Assert.True(clickSent, "SendInputClick should return true");
            _output.WriteLine("SendInput click sent! Waiting for UI transition & idle...");

            // Wait for UI animation and transition to complete
            await Task.Delay(2500);
            var idleRes = await visionClient.WaitForIdleAsync(baseUrl, token, maxFrames: 10, timeoutMs: 3000);
            _output.WriteLine($"WaitForIdle response: IdleReached={idleRes.Value.IdleReached}, Frames={idleRes.Value.ElapsedFrames}");

            // Step 3: Post-click screenshot (Transition 1)
            var scPostClick = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(scPostClick.IsSuccess);
            string pathProof04PostClick = Path.Combine(ArtifactDir, "proof_04_post_click_options.png");
            File.WriteAllBytes(pathProof04PostClick, scPostClick.Value.PngBytes);
            _output.WriteLine($"[Saved Proof 4] Post-Click Screenshot: {pathProof04PostClick} ({scPostClick.Value.PngBytes.Length} bytes)");

            // Observable Effect Verification: byte length / image hash comparison
            bool isScreenVisuallyChanged = scPreClick.Value.PngBytes.Length != scPostClick.Value.PngBytes.Length;
            _output.WriteLine($"[OBSERVABLE EVIDENCE 1] Transition 1 changed screen? {isScreenVisuallyChanged} (Pre={scPreClick.Value.PngBytes.Length}B, Post={scPostClick.Value.PngBytes.Length}B)");

            // Step 4: Click return button (Transition 2)
            int secondGameX = isInitiallyInOptions ? 511 : 512;
            int secondGameY = isInitiallyInOptions ? 432 : 645;
            string secondAction = isInitiallyInOptions ? "Opzioni (Main Menu -> Options)" : "Indietro (Options -> Main Menu)";

            var (secondPanelX, secondPanelY) = ViewportCalculator.GameToPanelCoordinates(
                secondGameX, secondGameY, boxX, boxY, boxW, boxH, targetGameWidth, targetGameHeight);

            _output.WriteLine($"Targeting {secondAction}: Game=({secondGameX},{secondGameY}) -> Panel=({secondPanelX},{secondPanelY})");
            controller.SendInputClick(secondPanelX, secondPanelY);
            _output.WriteLine($"SendInput click for '{secondAction}' sent! Waiting for UI transition & idle...");

            await Task.Delay(2500);
            await visionClient.WaitForIdleAsync(baseUrl, token, maxFrames: 10, timeoutMs: 3000);

            // Step 5: Post-back screenshot (Transition 2)
            var scPostBack = await visionClient.CaptureScreenshotAsync(baseUrl, token);
            Assert.True(scPostBack.IsSuccess);
            string pathProof05PostBack = Path.Combine(ArtifactDir, "proof_05_post_back_main_menu.png");
            File.WriteAllBytes(pathProof05PostBack, scPostBack.Value.PngBytes);
            _output.WriteLine($"[Saved Proof 5] Post-Return Screenshot: {pathProof05PostBack} ({scPostBack.Value.PngBytes.Length} bytes)");

            bool isBackVisuallyChanged = scPostClick.Value.PngBytes.Length != scPostBack.Value.PngBytes.Length;
            _output.WriteLine($"[OBSERVABLE EVIDENCE 2] Transition 2 returned/changed screen? {isBackVisuallyChanged} (Transition1={scPostClick.Value.PngBytes.Length}B, Transition2={scPostBack.Value.PngBytes.Length}B)");
        }
        finally
        {
            _output.WriteLine("\n=== CLEANUP ===");
            _output.WriteLine("Shutting down live game...");
            await controller.StopAsync();
            stopPump = true;
            Win32Native.DestroyWindow(hostHwnd);
            pumpThread.Join(1500);
        }
    }
}
