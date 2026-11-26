using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Candied.Vision.Protocol;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class VisionProtocolTests
{
    [Fact]
    public void CoordinateConverter_ConvertsBottomLeftAndNormalizedRectsAccurately()
    {
        var rect = CoordinateConverter.BottomLeftToImagePixelRect(100f, 200f, 300f, 100f, 1080);
        Assert.Equal(100f, rect.X);
        Assert.Equal(780f, rect.Y);
        Assert.Equal(300f, rect.Width);
        Assert.Equal(100f, rect.Height);

        var normalized = CoordinateConverter.ToNormalizedRect(new VisionRectDto(192f, 108f, 960f, 540f), 1920, 1080);
        Assert.Equal(0.1f, normalized.X, 3);
        Assert.Equal(0.1f, normalized.Y, 3);
        Assert.Equal(0.5f, normalized.Width, 3);
        Assert.Equal(0.5f, normalized.Height, 3);

        var bottomLeftPt = CoordinateConverter.ImageToBottomLeftScreenPoint(150f, 250f, 1080);
        Assert.Equal(150f, bottomLeftPt.X);
        Assert.Equal(830f, bottomLeftPt.Y);
    }

    [Fact]
    public void JsonLite_RoundTrips_AgnosticHello_Capabilities_SceneNodes_And_ThreeChannelInput()
    {
        var hello = new VisionHelloDto
        {
            ProtocolVersion = VisionConstants.ProtocolVersion,
            SensorVersion = "2.0.0",
            EngineName = "Unity",
            EngineVariant = "Mono",
            EngineVersion = "2022.3.10f1",
            ScreenWidth = 1920,
            ScreenHeight = 1080
        };
        var parsedHello = JsonLite.DeserializeHello(JsonLite.Serialize(hello));
        Assert.Equal("Unity", parsedHello.EngineName);
        Assert.Equal("Mono", parsedHello.EngineVariant);
        Assert.Equal("2022.3.10f1", parsedHello.EngineVersion);
        Assert.Equal(1920, parsedHello.ScreenWidth);

        var caps = new VisionCapabilitiesDto
        {
            Capabilities = new List<string> { VisionConstants.CapScreenshot, VisionConstants.CapKeyboard, VisionConstants.CapGamepad },
            ScreenshotMethod = "screen_capture",
            PointerMethod = "virtual_mouse [ISOLATED]",
            Metadata = new Dictionary<string, string> { ["uiFramework"] = "ugui" }
        };
        var parsedCaps = JsonLite.DeserializeCapabilities(JsonLite.Serialize(caps));
        Assert.Contains(VisionConstants.CapGamepad, parsedCaps.Capabilities);
        Assert.Equal("ugui", parsedCaps.Metadata["uiFramework"]);

        var kbReq = new VisionInputRequest
        {
            Channel = VisionConstants.ChannelKeyboard,
            Action = "press",
            Key = "Escape",
            Modifiers = new List<string> { "Shift" },
            HoldFrames = 8
        };
        var parsedKbReq = JsonLite.DeserializeInputRequest(JsonLite.Serialize(kbReq));
        Assert.Equal(VisionConstants.ChannelKeyboard, parsedKbReq.Channel);
        Assert.Equal("Escape", parsedKbReq.Key);
        Assert.Contains("Shift", parsedKbReq.Modifiers);
        Assert.Equal(8, parsedKbReq.HoldFrames);

        var padReq = new VisionInputRequest
        {
            Channel = VisionConstants.ChannelGamepad,
            Action = "stick",
            GamepadButton = "South",
            Stick = "left",
            StickX = -0.75f,
            StickY = 0.5f,
            HoldFrames = 12
        };
        var parsedPadReq = JsonLite.DeserializeInputRequest(JsonLite.Serialize(padReq));
        Assert.Equal(VisionConstants.ChannelGamepad, parsedPadReq.Channel);
        Assert.Equal("South", parsedPadReq.GamepadButton);
        Assert.Equal("left", parsedPadReq.Stick);
        Assert.Equal(-0.75f, parsedPadReq.StickX!.Value, 2);
        Assert.Equal(0.5f, parsedPadReq.StickY!.Value, 2);

        var sceneGraph = new VisionSceneGraphResponse
        {
            Success = true,
            TotalCameras = 1,
            TotalNodes = 1,
            TotalMatched = 1,
            Cameras = new List<VisionSceneCameraDto>
            {
                new() { Name = "Main Camera", Orthographic = true, OrthographicSize = 5f }
            },
            Nodes = new List<VisionSceneNodeDto>
            {
                new()
                {
                    Id = "node-1",
                    Name = "HeroSprite",
                    FullPath = "World/HeroSprite",
                    NodeType = "SpriteRenderer",
                    IsVisible = true,
                    Components = new List<string> { "Transform", "SpriteRenderer" },
                    Attributes = new Dictionary<string, string>
                    {
                        ["sortingLayer"] = "Characters",
                        ["spriteName"] = "hero_idle_01"
                    }
                }
            }
        };
        var parsedScene = JsonLite.DeserializeSceneGraph(JsonLite.Serialize(sceneGraph));
        Assert.Single(parsedScene.Nodes);
        Assert.Equal("Characters", parsedScene.Nodes[0].Attributes["sortingLayer"]);
        Assert.Equal("hero_idle_01", parsedScene.Nodes[0].Attributes["spriteName"]);
    }

    [Fact]
    public async Task MainThreadDispatcher_ProcessesWorkAndPropagatesExceptions()
    {
        var dispatcher = new MainThreadDispatcher();
        var task = Task.Run(() => dispatcher.Enqueue(() => 42, timeoutMs: 3000));

        for (int i = 0; i < 50 && dispatcher.PendingCount == 0; i++)
        {
            await Task.Delay(10);
        }

        dispatcher.ProcessQueue();
        Assert.Equal(42, await task);
        Assert.Throws<TimeoutException>(() => dispatcher.Enqueue(() => 1, timeoutMs: 30));
    }

    [Fact]
    public void VirtualInputStateTracker_TracksMouseKeyboardAndGamepadEdges()
    {
        var tracker = new VirtualInputStateTracker();
        Assert.False(tracker.IsIsolated);

        tracker.UpdateMouseState(640f, 360f, buttonsMask: 1, clickCount: 1, currentFrame: 10);
        Assert.True(tracker.IsIsolated);
        Assert.True(tracker.IsButtonHeld(0));
        Assert.True(tracker.IsButtonDownOnFrame(0, 11));
        Assert.False(tracker.IsButtonDownOnFrame(0, 12));

        tracker.UpdateKeyState("Space", isDown: true, currentFrame: 20);
        Assert.True(tracker.IsKeyHeld("space"));
        Assert.True(tracker.IsKeyDownOnFrame("SPACE", 21));
        tracker.UpdateKeyState("Space", isDown: false, currentFrame: 25);
        Assert.False(tracker.IsKeyHeld("Space"));
        Assert.True(tracker.IsKeyUpOnFrame("Space", 26));

        tracker.UpdateGamepadButtonState("South", isDown: true);
        tracker.UpdateGamepadStick("left", x: 1.0f, y: -0.5f);
        Assert.True(tracker.IsGamepadButtonHeld("south"));
        Assert.Equal(1.0f, tracker.LeftStickX);
        Assert.Equal(-0.5f, tracker.LeftStickY);
        tracker.UpdateGamepadButtonState("South", isDown: false);
        Assert.False(tracker.IsGamepadButtonHeld("South"));

        tracker.UpdateGamepadButtonState("East", isDown: true);
        tracker.Reset();
        Assert.False(tracker.IsIsolated);
        Assert.False(tracker.IsGamepadButtonHeld("East"));
        Assert.Equal(0f, tracker.LeftStickX);
    }
}
