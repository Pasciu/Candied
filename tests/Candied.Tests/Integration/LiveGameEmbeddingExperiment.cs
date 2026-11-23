using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Candied.Decompiler;
using Candied.Studio.Native;
using Candied.Studio.Services;
using Candied.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Candied.Tests.Integration;

public sealed class LiveGameEmbeddingExperiment
{
    private readonly ITestOutputHelper _output;

    public LiveGameEmbeddingExperiment(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact(Skip = "Explicit live game test against installed Steam game")]
    public async Task Experiment_PotionCraft_EmbeddingAndLifecycle()
    {
        string gamePath = Environment.GetEnvironmentVariable("CANDIED_TEST_GAME_EXE") ?? string.Empty;
        if (!File.Exists(gamePath))
        {
            _output.WriteLine("Potion Craft not installed. Skipping live test.");
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

        controller.StatusChanged += s => _output.WriteLine($"[STATUS] {s}");
        controller.LogReceived += l => _output.WriteLine($"[LOG] {l}");

        // 1. Validation
        var selectResult = await controller.ValidateAndSelectGameAsync(gamePath);
        Assert.True(selectResult.IsSuccess);
        _output.WriteLine($"Validated Game: {controller.CurrentGameName}");

        // 2. Create Win32 host container window
        IntPtr hostHwnd = Win32Native.CreateWindowEx(
            0,
            "STATIC",
            "CandiedTestHost",
            Win32Native.WS_CAPTION | Win32Native.WS_VISIBLE | Win32Native.WS_CLIPCHILDREN,
            100, 100, 1024, 768,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        Assert.NotEqual(IntPtr.Zero, hostHwnd);
        _output.WriteLine($"Created Host Window HWND: {hostHwnd}");

        try
        {
            // 3. Launch game with -parentHWND
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            bool launched = await controller.LaunchAsync(hostHwnd, 1024, 768, cts.Token);
            _output.WriteLine($"Launch result: {launched}");

            if (launched)
            {
                _output.WriteLine($"Embedding Mode: {controller.EmbeddingMode}");
                _output.WriteLine($"Embedded Game HWND: {controller.EmbeddedGameHwnd}");

                // 4. Test Resizing
                controller.Resize(1280, 800);
                _output.WriteLine("Resize to 1280x800 executed.");

                // Wait 4 seconds to observe running state
                await Task.Delay(4000);

                // 5. Test Clean Stop / Process Termination via Job Object
                _output.WriteLine("Stopping game...");
                await controller.StopAsync();
                _output.WriteLine($"IsRunning after stop: {controller.IsRunning}");
            }
        }
        finally
        {
            await controller.StopAsync();
            Win32Native.DestroyWindow(hostHwnd);
        }
    }
}
