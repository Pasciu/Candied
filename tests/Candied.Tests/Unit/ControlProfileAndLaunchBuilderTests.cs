using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Candied.Vision.Adapters;
using Candied.Vision.Analysis;
using Candied.Vision.Controls;
using Candied.Vision.Hosting;
using Candied.Vision.Models;
using Candied.Vision.Protocol;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class ControlProfileAndLaunchBuilderTests : IDisposable
{
    private readonly string _tempDir;

    public ControlProfileAndLaunchBuilderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_VisionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
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
    public void ControlActionResolver_SelectsPreferredChannelAndHandlesBuiltInFallbacks()
    {
        var profile = new GameControlProfile(
            TargetId: "crash3",
            TargetName: "Crash Bandicoot 3",
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Actions:
            [
                new ControlActionEntry(
                    ActionName: "SpinAttack",
                    DisplayName: "Spin Attack",
                    Source: "agent",
                    Bindings:
                    [
                        new InputBinding(InputChannel.Gamepad, GamepadButton: "West", HoldFrames: 5),
                        new InputBinding(InputChannel.Keyboard, Key: "X", HoldFrames: 5)
                    ])
            ]);

        // When only keyboard capability is active, selects Keyboard fallback binding
        var kbRes = ControlActionResolver.ResolveAction("SpinAttack", profile, [VisionConstants.CapKeyboard]);
        Assert.True(kbRes.IsSuccess);
        Assert.Equal(VisionConstants.ChannelKeyboard, kbRes.Value.Channel);
        Assert.Equal("X", kbRes.Value.Key);

        // When gamepad capability is active, selects primary Gamepad binding
        var padRes = ControlActionResolver.ResolveAction("SpinAttack", profile, [VisionConstants.CapGamepad]);
        Assert.True(padRes.IsSuccess);
        Assert.Equal(VisionConstants.ChannelGamepad, padRes.Value.Channel);
        Assert.Equal("West", padRes.Value.GamepadButton);

        // Built-in fallback for "Confirm"
        var confirmRes = ControlActionResolver.ResolveAction("Confirm", profile, [VisionConstants.CapKeyboard]);
        Assert.True(confirmRes.IsSuccess);
        Assert.Equal("Enter", confirmRes.Value.Key);

        // Unmapped action returns structured UNMAPPED_ACTION error
        var unmapped = ControlActionResolver.ResolveAction("OpenGrimoire", profile);
        Assert.True(unmapped.IsFailure);
        Assert.Equal("UNMAPPED_ACTION", unmapped.Error.Code);
    }

    [Fact]
    public async Task JsonGameControlProfileStore_EnforcesSourcePriorityOnMerge()
    {
        string profilePath = Path.Combine(_tempDir, "controls.json");
        var store = new JsonGameControlProfileStoreAdapter();

        // 1. User saves custom Confirm = Space
        await store.MergeAndSaveActionsAsync(
            profilePath,
            "sample",
            "Sample",
            [new ControlActionEntry("Confirm", "Confirm", "user", [new InputBinding(InputChannel.Keyboard, Key: "Space")])]);

        // 2. Auto-extractor discovers Confirm = Enter -> should NOT overwrite higher-priority "user" entry!
        var merged = await store.MergeAndSaveActionsAsync(
            profilePath,
            "sample",
            "Sample",
            [
                new ControlActionEntry("Confirm", "Confirm", "auto", [new InputBinding(InputChannel.Keyboard, Key: "Enter")]),
                new ControlActionEntry("Jump", "Jump", "auto", [new InputBinding(InputChannel.Gamepad, GamepadButton: "South")])
            ],
            overwriteHigherPrioritySources: false);

        Assert.Equal(2, merged.Actions.Count);
        var confirm = Assert.Single(merged.Actions, a => a.ActionName == "Confirm");
        Assert.Equal("user", confirm.Source);
        Assert.Equal("Space", confirm.Bindings[0].Key);
    }

    [Fact]
    public void VirtualKeyMapper_ResolvesStandardKeysAndGamepadFallbacks()
    {
        Assert.True(VirtualKeyMapper.TryResolveKeyVk("Enter", out int enterVk));
        Assert.Equal(0x0D, enterVk);

        Assert.True(VirtualKeyMapper.TryResolveKeyVk("w", out int wVk));
        Assert.Equal('W', wVk);

        Assert.True(VirtualKeyMapper.TryResolveGamepadFallbackVk("South", out int southVk));
        Assert.Equal(0x0D, southVk);

        Assert.True(VirtualKeyMapper.TryResolveGamepadFallbackVk("DPadLeft", out int leftVk));
        Assert.Equal(0x25, leftVk);
    }

    [Fact]
    public void WindowLaunchProfileBuilder_SupportsPcExecutable_UnityFolder_And_EmulatorWithRom()
    {
        // 1. Emulator + ROM
        string fakeEmu = Path.Combine(_tempDir, "pcsx2-qt.exe");
        string fakeRom = Path.Combine(_tempDir, "CrashBandicoot3.iso");
        File.WriteAllText(fakeEmu, "mz");
        File.WriteAllText(fakeRom, "iso");

        var emuSpec = WindowLaunchProfileBuilder.Build(fakeRom, fakeEmu, "-batch");
        Assert.True(emuSpec.IsSuccess);
        Assert.Equal("CrashBandicoot3", emuSpec.Value.TargetName);
        Assert.Equal(fakeEmu, emuSpec.Value.RunnerPath);
        Assert.False(emuSpec.Value.SupportsParentHwndFlag);
        Assert.Contains("CrashBandicoot3.iso", WindowLaunchProfileBuilder.BuildEffectiveArguments(emuSpec.Value, new IntPtr(999), 1280, 720));

        // 2. Unity directory
        string unityDir = Path.Combine(_tempDir, "MyUnityGame");
        Directory.CreateDirectory(Path.Combine(unityDir, "MyUnityGame_Data"));
        string unityExe = Path.Combine(unityDir, "MyUnityGame.exe");
        File.WriteAllText(unityExe, "mz");

        var unitySpec = WindowLaunchProfileBuilder.Build(unityDir);
        Assert.True(unitySpec.IsSuccess);
        Assert.Equal("MyUnityGame", unitySpec.Value.TargetName);
        Assert.True(unitySpec.Value.SupportsParentHwndFlag);
        Assert.Equal("UnityWndClass", unitySpec.Value.WindowClassHint);
        Assert.Contains("-parentHWND 12345", WindowLaunchProfileBuilder.BuildEffectiveArguments(unitySpec.Value, new IntPtr(12345), 1280, 720));
    }

    [Fact]
    public void PurePngDecoder_EncodesDrawsClickMarkerAndEvaluatesUniformity()
    {
        byte[] solidBlue = new byte[32 * 32 * 4];
        for (int i = 0; i < solidBlue.Length; i += 4)
        {
            solidBlue[i] = 20;
            solidBlue[i + 1] = 40;
            solidBlue[i + 2] = 200;
            solidBlue[i + 3] = 255;
        }

        byte[] png = PurePngDecoder.EncodeRgbaToPng(solidBlue, 32, 32);
        Assert.True(PurePngDecoder.IsUniformColor(png));
        Assert.Equal(0.0, PurePngDecoder.CalculateDiffPercentage(png, png));

        byte[] marked = PurePngDecoder.DrawClickMarker(png, 16, 16);
        Assert.False(PurePngDecoder.IsUniformColor(marked));
        Assert.True(PurePngDecoder.CalculateDiffPercentage(png, marked) > 0.5);
    }

    [Fact]
    public async Task SessionAndRecentStores_PersistAndPurgeStaleEntries()
    {
        string sessionFile = Path.Combine(_tempDir, "studio_session.json");
        var sessionMgr = new DiskStudioSessionManagerAdapter(sessionFile);

        sessionMgr.WriteSession(new StudioSessionInfo(9999999, 1234, "stale", null, null, null, null));
        Assert.Null(sessionMgr.GetActiveSession());
        Assert.False(File.Exists(sessionFile));

        sessionMgr.WriteSession(new StudioSessionInfo(Environment.ProcessId, 4321, "live", "Crash3", 100, 5000, "tok"));
        var active = sessionMgr.GetActiveSession();
        Assert.NotNull(active);
        Assert.Equal("Crash3", active.ActiveTargetName);

        string recentFile = Path.Combine(_tempDir, "recent.json");
        var recentStore = new JsonRecentTargetsStoreAdapter(recentFile);
        await recentStore.RecordRecentAsync(new RecentTargetEntry("Crash3", fakeRomPath(), null, null, DateTimeOffset.UtcNow));
        var list = await recentStore.ListRecentAsync();
        Assert.Single(list);
        Assert.Equal("Crash3", list[0].Name);

        string fakeRomPath() => Path.Combine(_tempDir, "rom.iso");
    }

    [Fact]
    public async Task HttpVisionSensorClientAdapter_SendsTokenHeaderAndParsesResponses()
    {
        string? observedToken = null;
        var handler = new FakeHttpHandler(req =>
        {
            if (req.Headers.TryGetValues(VisionConstants.HeaderToken, out var vals))
            {
                foreach (var v in vals)
                {
                    observedToken = v;
                }
            }

            var respDto = new VisionInputResponse
            {
                Success = true,
                Channel = VisionConstants.ChannelGamepad,
                Action = "press",
                Message = "Dispatched South"
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonLite.Serialize(respDto), Encoding.UTF8, "application/json")
            };
        });

        using var http = new HttpClient(handler);
        var client = new HttpVisionSensorClientAdapter(http);

        var res = await client.SendInputAsync(
            "http://127.0.0.1:5555",
            "tok-abc",
            new VisionInputRequest { Channel = VisionConstants.ChannelGamepad, Action = "press", GamepadButton = "South" });

        Assert.True(res.IsSuccess);
        Assert.Equal("tok-abc", observedToken);
        Assert.Equal(VisionConstants.ChannelGamepad, res.Value.Channel);
    }

    private sealed class FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
