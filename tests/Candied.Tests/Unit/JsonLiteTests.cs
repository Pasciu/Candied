namespace Candied.Tests.Unit;

using System.Collections.Generic;
using Candied.VisionBridge.Protocol;
using Xunit;

public sealed class JsonLiteTests
{
    [Fact]
    public void SerializeAndDeserialize_HelloDto_RoundTripsAccurately()
    {
        var original = new VisionHelloDto
        {
            ProtocolVersion = "1.0.0",
            PluginVersion = "1.0.0",
            UnityVersion = "2021.3.16f1",
            ScreenWidth = 1920,
            ScreenHeight = 1080
        };

        string json = JsonLite.Serialize(original);
        Assert.Contains("\"protocolVersion\":\"1.0.0\"", json);
        Assert.Contains("\"screenWidth\":1920", json);

        var deserialized = JsonLite.DeserializeHello(json);
        Assert.NotNull(deserialized);
        Assert.Equal(original.ProtocolVersion, deserialized.ProtocolVersion);
        Assert.Equal(original.PluginVersion, deserialized.PluginVersion);
        Assert.Equal(original.UnityVersion, deserialized.UnityVersion);
        Assert.Equal(original.ScreenWidth, deserialized.ScreenWidth);
        Assert.Equal(original.ScreenHeight, deserialized.ScreenHeight);
    }

    [Fact]
    public void SerializeAndDeserialize_CapabilitiesDto_RoundTripsAccurately()
    {
        var original = new VisionCapabilitiesDto
        {
            Ugui = true,
            EventSystem = true,
            UiToolkit = false,
            LegacyInput = true,
            InputSystem = false,
            Screenshot = true,
            ScreenshotMethod = "camera_fallback",
            ScreenshotLimitations = "No overlay UI",
            InputClickMethod = "input_system",
            InputClickStatus = "input_system: provato una volta su un gioco (Potion Craft), affidabilità non misurata",
            UntestedMethods = "legacy e postmessage sono implementati ma mai provati dal vivo"
        };

        string json = JsonLite.Serialize(original);
        var deserialized = JsonLite.DeserializeCapabilities(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Ugui);
        Assert.True(deserialized.EventSystem);
        Assert.False(deserialized.UiToolkit);
        Assert.True(deserialized.LegacyInput);
        Assert.False(deserialized.InputSystem);
        Assert.True(deserialized.Screenshot);
        Assert.Equal("camera_fallback", deserialized.ScreenshotMethod);
        Assert.Equal("No overlay UI", deserialized.ScreenshotLimitations);
        Assert.Equal("input_system", deserialized.InputClickMethod);
        Assert.Equal("input_system: provato una volta su un gioco (Potion Craft), affidabilità non misurata", deserialized.InputClickStatus);
        Assert.Equal("legacy e postmessage sono implementati ma mai provati dal vivo", deserialized.UntestedMethods);
    }

    [Fact]
    public void SerializeAndDeserialize_SceneInfoDto_RoundTripsAccurately()
    {
        var original = new VisionSceneInfoDto
        {
            ActiveSceneName = "AlchemistLaboratory",
            ActiveSceneIndex = 2,
            RootCount = 14,
            IsLoaded = true,
            LoadedSceneCount = 1,
            ScreenWidth = 1024,
            ScreenHeight = 720
        };

        string json = JsonLite.Serialize(original);
        var deserialized = JsonLite.DeserializeSceneInfo(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ActiveSceneName, deserialized.ActiveSceneName);
        Assert.Equal(original.ActiveSceneIndex, deserialized.ActiveSceneIndex);
        Assert.Equal(original.RootCount, deserialized.RootCount);
        Assert.True(deserialized.IsLoaded);
        Assert.Equal(original.LoadedSceneCount, deserialized.LoadedSceneCount);
        Assert.Equal(original.ScreenWidth, deserialized.ScreenWidth);
        Assert.Equal(original.ScreenHeight, deserialized.ScreenHeight);
    }

    [Fact]
    public void SerializeAndDeserialize_UiElementsResponse_RoundTripsAccurately()
    {
        var original = new VisionUiElementsResponse
        {
            Count = 1,
            Elements = new List<VisionUiElementDto>
            {
                new VisionUiElementDto
                {
                    Id = "Canvas/Panel/StartBtn",
                    Type = "Button",
                    Text = "Play Game",
                    PixelRect = new VisionRectDto(100, 200, 150, 50),
                    NormalizedRect = new VisionRectDto(0.078f, 0.277f, 0.117f, 0.069f),
                    Interactable = true,
                    Visible = true
                }
            }
        };

        string json = JsonLite.Serialize(original);
        var deserialized = JsonLite.DeserializeUiElements(json);

        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.Count);
        Assert.Single(deserialized.Elements);

        var elem = deserialized.Elements[0];
        Assert.Equal("Canvas/Panel/StartBtn", elem.Id);
        Assert.Equal("Button", elem.Type);
        Assert.Equal("Play Game", elem.Text);
        Assert.Equal(100, elem.PixelRect.X);
        Assert.Equal(200, elem.PixelRect.Y);
        Assert.Equal(150, elem.PixelRect.Width);
        Assert.Equal(50, elem.PixelRect.Height);
        Assert.True(elem.Interactable);
        Assert.True(elem.Visible);
    }

    [Fact]
    public void SerializeAndDeserialize_ClickRequestAndResponse_Works()
    {
        var req = new VisionClickRequest
        {
            Id = "Canvas/Button",
            X = 0.5f,
            Y = 0.5f,
            Button = 0
        };
        string jsonReq = JsonLite.Serialize(req);
        var parsedReq = JsonLite.DeserializeClickRequest(jsonReq);
        Assert.NotNull(parsedReq);
        Assert.Equal("Canvas/Button", parsedReq.Id);
        Assert.Equal(0.5f, parsedReq.X);

        var resp = new VisionClickResponse
        {
            Success = true,
            HitElementId = "Canvas/Button",
            Message = "Clicked successfully"
        };
        string jsonResp = JsonLite.Serialize(resp);
        var parsedResp = JsonLite.DeserializeClickResponse(jsonResp);
        Assert.NotNull(parsedResp);
        Assert.True(parsedResp.Success);
        Assert.Equal("Canvas/Button", parsedResp.HitElementId);
        Assert.Equal("Clicked successfully", parsedResp.Message);
    }

    [Fact]
    public void SerializeAndDeserialize_ResizeRequestAndResponse_Works()
    {
        var req = new VisionResizeRequest
        {
            Width = 1280,
            Height = 720,
            Fullscreen = false
        };
        string jsonReq = JsonLite.Serialize(req);
        Assert.Contains("\"width\":1280", jsonReq);
        Assert.Contains("\"height\":720", jsonReq);

        var parsedReq = JsonLite.DeserializeResizeRequest(jsonReq);
        Assert.NotNull(parsedReq);
        Assert.Equal(1280, parsedReq.Width);
        Assert.Equal(720, parsedReq.Height);
        Assert.False(parsedReq.Fullscreen);

        var resp = new VisionResizeResponse
        {
            Success = true,
            ScreenWidth = 1280,
            ScreenHeight = 720,
            Message = "Resolution updated"
        };
        string jsonResp = JsonLite.Serialize(resp);
        var parsedResp = JsonLite.DeserializeResizeResponse(jsonResp);
        Assert.NotNull(parsedResp);
        Assert.True(parsedResp.Success);
        Assert.Equal(1280, parsedResp.ScreenWidth);
        Assert.Equal(720, parsedResp.ScreenHeight);
        Assert.Equal("Resolution updated", parsedResp.Message);
    }

    [Fact]
    public void SerializeAndDeserialize_ErrorResponse_ParsesCorrectly()
    {
        var err = new VisionErrorResponse
        {
            Code = "UNAUTHORIZED",
            Message = "Invalid token provided",
            Suggestion = "Pass X-Candied-Token matching CANDIED_TOKEN"
        };
        string json = JsonLite.Serialize(err);
        var parsed = JsonLite.DeserializeErrorResponse(json);

        Assert.NotNull(parsed);
        Assert.Equal("UNAUTHORIZED", parsed.Code);
        Assert.Equal("Invalid token provided", parsed.Message);
        Assert.Equal("Pass X-Candied-Token matching CANDIED_TOKEN", parsed.Suggestion);
    }

    [Fact]
    public void JsonValue_ParsesPrimitivesAndSpecialCharacters()
    {
        string raw = "{\"escaped\": \"Line1\\nLine2 \\\"Quotes\\\" \\\\ Backslash\", \"nullVal\": null, \"flag\": true, \"num\": 42.5}";
        var parsed = JsonLite.Parse(raw);
        var obj = parsed.AsObject();

        Assert.NotNull(obj);
        Assert.Equal("Line1\nLine2 \"Quotes\" \\ Backslash", obj["escaped"].AsString());
        Assert.Null(obj["nullVal"].Value);
        Assert.True(obj["flag"].AsBoolean());
        Assert.Equal(42.5, obj["num"].AsDouble());
    }
}
