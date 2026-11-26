using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Agent;
using Candied.Agent.Mcp;
using Candied.Infrastructure;
using Candied.Platforms.Unity.Mono;
using Candied.Primitives;
using Candied.Vision;
using Candied.Vision.Analysis;
using Candied.Vision.Models;
using Candied.Vision.Ports;
using Candied.Vision.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class McpAndAgentWorkbenchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;
    private readonly McpServer _mcpServer;

    public McpAndAgentWorkbenchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_McpTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddCandiedCoreAndInfrastructure(_tempDir);
        services.AddVisionInfrastructure();
        services.AddUnityMonoPlatform();
        services.AddTransient<AgentWorkbenchService>();
        services.AddTransient<McpServer>();

        _serviceProvider = services.BuildServiceProvider();
        _mcpServer = _serviceProvider.GetRequiredService<McpServer>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }

    [Fact]
    public async Task McpServer_Initialize_And_ToolsList_ExposeAgnosticAndDynamicPlatformTools()
    {
        string? initResp = await _mcpServer.HandleMessageAsync("""{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""");
        Assert.NotNull(initResp);
        using (var initDoc = JsonDocument.Parse(initResp))
        {
            var result = initDoc.RootElement.GetProperty("result");
            Assert.Equal("2.0.0", result.GetProperty("serverInfo").GetProperty("version").GetString());
            string instructions = result.GetProperty("instructions").GetString()!;
            Assert.Contains("extract-code", instructions);
            Assert.Contains("unity-add-reference", instructions);
        }

        string? listResp = await _mcpServer.HandleMessageAsync("""{"jsonrpc":"2.0","id":2,"method":"tools/list"}""");
        Assert.NotNull(listResp);
        using (var listDoc = JsonDocument.Parse(listResp))
        {
            var tools = listDoc.RootElement.GetProperty("result").GetProperty("tools");
            var names = new HashSet<string>();
            foreach (var t in tools.EnumerateArray())
            {
                names.Add(t.GetProperty("name").GetString()!);
            }

            // Agnostic v2.0 tools
            Assert.Contains("inspect", names);
            Assert.Contains("extract-code", names);
            Assert.Contains("send-input", names);
            Assert.Contains("get-controls", names);
            Assert.Contains("set-controls", names);
            Assert.Contains("trigger-action", names);
            Assert.Contains("inspect-scene", names);

            // Dynamic platform tool from IPlatformToolProvider
            Assert.Contains("unity-add-reference", names);

            // Legacy Unity-coupled names must NOT exist
            Assert.DoesNotContain("decompile", names);
            Assert.DoesNotContain("register-game", names);
            Assert.DoesNotContain("install-bridge", names);
        }
    }

    [Fact]
    public async Task AgentWorkbench_DirectLaunch_SupportsTier1BaseWindowWithoutAllowlistOrSensor_AndAnnotatedClick()
    {
        string fakeRom = Path.Combine(_tempDir, "CrashBandicoot3.iso");
        string fakeEmu = Path.Combine(_tempDir, "pcsx2-qt.exe");
        await File.WriteAllTextAsync(fakeRom, "iso");
        await File.WriteAllTextAsync(fakeEmu, "exe");

        var fakeStudio = new FakeStudioHostClient();
        var controlStore = _serviceProvider.GetRequiredService<IGameControlProfileStore>();
        var workbench = new AgentWorkbenchService(
            storageLayout: new Candied.Core.Models.CandiedStorageLayout(_tempDir),
            studioClient: fakeStudio,
            controlProfileStore: controlStore);

        // 1. Direct launch of Emulator + ROM without any registration allowlist or in-game sensor!
        string launchJson = await workbench.LaunchInStudioAsync(fakeRom, fakeEmu, stabilityTimeoutSeconds: 4);
        using (var doc = JsonDocument.Parse(launchJson))
        {
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("BaseWindow", doc.RootElement.GetProperty("visionTier").GetString());
            Assert.Equal("CrashBandicoot3", doc.RootElement.GetProperty("targetName").GetString());
            Assert.Equal(1, fakeStudio.LaunchCallCount);
        }

        // 2. Second launch call for the same target is idempotent (0 additional Studio /launch calls)
        string secondLaunchJson = await workbench.LaunchInStudioAsync(fakeRom, fakeEmu, stabilityTimeoutSeconds: 4);
        using (var doc2 = JsonDocument.Parse(secondLaunchJson))
        {
            Assert.True(doc2.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(1, fakeStudio.LaunchCallCount);
        }

        // 3. Register semantic control action and trigger it via Tier 1 BaseWindow
        await workbench.SetControlsAsync(
        [
            new Candied.Vision.Controls.ControlActionEntry(
                "SpinAttack",
                "Spin Attack",
                "agent",
                [new Candied.Vision.Controls.InputBinding(Candied.Vision.Controls.InputChannel.Keyboard, Key: "X")])
        ], targetPath: fakeRom, runnerPath: fakeEmu);

        string triggerJson = await workbench.TriggerActionAsync("SpinAttack", fakeRom, fakeEmu);
        using (var trigDoc = JsonDocument.Parse(triggerJson))
        {
            Assert.True(trigDoc.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("keyboard", trigDoc.RootElement.GetProperty("channel").GetString());
        }

        // 4. Pointer click produces annotated PNG with red click crosshair
        string clickJson = await workbench.SendInputAsync(new VisionInputRequest
        {
            Channel = VisionConstants.ChannelPointer,
            Action = "click",
            X = 16,
            Y = 16
        });
        using (var clickDoc = JsonDocument.Parse(clickJson))
        {
            Assert.True(clickDoc.RootElement.GetProperty("success").GetBoolean());
            string savedMarked = clickDoc.RootElement.GetProperty("screenshotPath").GetString()!;
            Assert.True(File.Exists(savedMarked));
        }
    }

    private sealed class FakeStudioHostClient : IStudioHostClient
    {
        private readonly byte[] _nonUniformPng;
        public int LaunchCallCount { get; private set; }
        public WindowLaunchSpec? ActiveSpec { get; private set; }

        public FakeStudioHostClient()
        {
            byte[] rgba = new byte[32 * 32 * 4];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                rgba[i] = (byte)(i % 251);
                rgba[i + 1] = 100;
                rgba[i + 2] = 180;
                rgba[i + 3] = 255;
            }

            _nonUniformPng = PurePngDecoder.EncodeRgbaToPng(rgba, 32, 32);
        }

        public Task<StudioSessionInfo?> GetCurrentSessionAsync(CancellationToken ct = default) =>
            Task.FromResult<StudioSessionInfo?>(new StudioSessionInfo(Environment.ProcessId, 19000, "tok", ActiveSpec?.TargetName, 555, null, null));

        public Task<Result<StudioSessionInfo, string>> EnsureStudioRunningAsync(CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<StudioSessionInfo, string>(new StudioSessionInfo(Environment.ProcessId, 19000, "tok", ActiveSpec?.TargetName, 555, null, null)));

        public Task<Result<StudioStatusInfo, string>> GetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<StudioStatusInfo, string>(BuildStatus()));

        public Task<Result<StudioStatusInfo, string>> LaunchTargetAsync(WindowLaunchSpec spec, CancellationToken ct = default)
        {
            LaunchCallCount++;
            ActiveSpec = spec;
            return Task.FromResult(Result.Ok<StudioStatusInfo, string>(BuildStatus()));
        }

        public Task<Result<bool, string>> CloseTargetAsync(CancellationToken ct = default)
        {
            ActiveSpec = null;
            return Task.FromResult(Result.Ok<bool, string>(true));
        }

        public Task<Result<VisionFrame, VisionError>> CaptureScreenshotAsync(CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<VisionFrame, VisionError>(new VisionFrame(_nonUniformPng, false, 32, 32, VisionTier.BaseWindow)));

        public Task<Result<VisionInputResponse, VisionError>> SendInputAsync(VisionInputRequest request, CancellationToken ct = default) =>
            Task.FromResult(Result.Ok<VisionInputResponse, VisionError>(new VisionInputResponse
            {
                Success = true,
                Channel = request.Channel,
                Action = request.Action,
                HitPointX = request.X ?? 0,
                HitPointY = request.Y ?? 0,
                Message = "[BaseWindow] Dispatched"
            }));

        private StudioStatusInfo BuildStatus() => new(
            StudioPid: Environment.ProcessId,
            IsRunning: ActiveSpec != null,
            ActiveTargetName: ActiveSpec?.TargetName,
            ActiveTargetPath: ActiveSpec?.TargetPath,
            ActiveRunnerPath: ActiveSpec?.RunnerPath,
            TargetPid: ActiveSpec != null ? 555 : null,
            SensorPort: null,
            EmbeddingMode: "SetParent (Win32)",
            ActiveVisionTier: ActiveSpec != null ? VisionTier.BaseWindow : VisionTier.None,
            IsSensorConnected: false,
            Window: new StudioWindowInfo(1001, true, "Normal", "Visible", 10, 10, 1290, 730, 1280, 720, true, true));
    }
}
