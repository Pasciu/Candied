namespace Candied.Tests.Unit;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Candied.Agent;
using Candied.Agent.Mcp;
using Candied.Core.Errors;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Core.UseCases;
using Candied.Primitives;
using Candied.VisionBridge.Protocol;
using Xunit;

public sealed class AgentVisionToolsTests
{
    private sealed class FakeVisionBridgeClient : IVisionBridgeClient
    {
        public VisionCapabilitiesDto Capabilities { get; set; } = new VisionCapabilitiesDto
        {
            Ugui = true,
            EventSystem = true,
            UiToolkit = false,
            LegacyInput = true,
            InputSystem = true,
            Screenshot = true,
            ScreenshotMethod = "screen_capture",
            ScreenshotLimitations = "None",
            InputClickMethod = "input_system",
            InputClickStatus = "input_system: provato una volta su un gioco (Potion Craft), affidabilità non misurata",
            UntestedMethods = "legacy e postmessage sono implementati ma mai provati dal vivo"
        };

        public VisionHelloDto Hello { get; set; } = new VisionHelloDto
        {
            ProtocolVersion = "1.0.0",
            PluginVersion = "1.0.0",
            UnityVersion = "2021.3.16f1",
            ScreenWidth = 1024,
            ScreenHeight = 720
        };

        public VisionSceneInfoDto SceneInfo { get; set; } = new VisionSceneInfoDto
        {
            ActiveSceneName = "MainScene",
            ActiveSceneIndex = 0,
            RootCount = 10,
            IsLoaded = true,
            LoadedSceneCount = 1,
            ScreenWidth = 1024,
            ScreenHeight = 720
        };

        public List<VisionUiElementDto> Elements { get; set; } = new List<VisionUiElementDto>
        {
            new VisionUiElementDto
            {
                Id = "Canvas/PlayButton",
                Type = "Button",
                Text = "Play",
                Interactable = true,
                Visible = true,
                PixelRect = new VisionRectDto(100, 200, 150, 50),
                NormalizedRect = new VisionRectDto(0.1f, 0.2f, 0.15f, 0.05f)
            }
        };

        public bool ShouldFailConnection { get; set; }
        public bool FailSceneInfo { get; set; }
        public float LastClickedX { get; private set; }
        public float LastClickedY { get; private set; }
        public int LastHoldFrames { get; private set; }

        public Task<Result<VisionHelloDto, VisionBridgeError>> GetHelloAsync(string baseUrl, string token, CancellationToken ct = default)
        {
            if (ShouldFailConnection)
                return Task.FromResult(Result.Fail<VisionHelloDto, VisionBridgeError>(new VisionBridgeError("CONN_REFUSED", "Connection refused", "Check game status")));
            return Task.FromResult(Result.Ok<VisionHelloDto, VisionBridgeError>(Hello));
        }

        public Task<Result<VisionCapabilitiesDto, VisionBridgeError>> GetCapabilitiesAsync(string baseUrl, string token, CancellationToken ct = default)
        {
            if (ShouldFailConnection)
                return Task.FromResult(Result.Fail<VisionCapabilitiesDto, VisionBridgeError>(new VisionBridgeError("CONN_REFUSED", "Connection refused", "Check game status")));
            return Task.FromResult(Result.Ok<VisionCapabilitiesDto, VisionBridgeError>(Capabilities));
        }

        public Task<Result<VisionScreenshot, VisionBridgeError>> CaptureScreenshotAsync(string baseUrl, string token, CancellationToken ct = default)
        {
            if (ShouldFailConnection)
                return Task.FromResult(Result.Fail<VisionScreenshot, VisionBridgeError>(new VisionBridgeError("CONN_REFUSED", "Connection refused", "Check game status")));

            byte[] dummyPng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
            return Task.FromResult(Result.Ok<VisionScreenshot, VisionBridgeError>(new VisionScreenshot(dummyPng, false, 1024, 720)));
        }

        public Task<Result<IReadOnlyList<VisionUiElementDto>, VisionBridgeError>> GetUiElementsAsync(string baseUrl, string token, CancellationToken ct = default)
        {
            if (ShouldFailConnection)
                return Task.FromResult(Result.Fail<IReadOnlyList<VisionUiElementDto>, VisionBridgeError>(new VisionBridgeError("CONN_REFUSED", "Connection refused", "Check game status")));
            return Task.FromResult(Result.Ok<IReadOnlyList<VisionUiElementDto>, VisionBridgeError>(Elements));
        }

        public Task<Result<VisionClickResponse, VisionBridgeError>> SendClickAsync(string baseUrl, string token, VisionClickRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<VisionClickResponse, VisionBridgeError>(new VisionClickResponse { Success = true }));
        }

        public Task<Result<VisionClickResponse, VisionBridgeError>> SendInputClickAsync(
            string baseUrl,
            string token,
            float x,
            float y,
            int button = 0,
            string? method = null,
            string? action = null,
            int holdFrames = 5,
            CancellationToken ct = default)
        {
            if (ShouldFailConnection)
                return Task.FromResult(Result.Fail<VisionClickResponse, VisionBridgeError>(new VisionBridgeError("CONN_REFUSED", "Connection refused", "Check game status")));

            LastClickedX = x;
            LastClickedY = y;
            LastHoldFrames = holdFrames;

            return Task.FromResult(Result.Ok<VisionClickResponse, VisionBridgeError>(new VisionClickResponse
            {
                Success = true,
                HitPointX = x,
                HitPointY = y,
                Message = $"Click dispatched at ({x}, {y})"
            }));
        }

        public Task<Result<VisionWaitIdleResponse, VisionBridgeError>> WaitForIdleAsync(string baseUrl, string token, int maxFrames = 10, int timeoutMs = 5000, CancellationToken ct = default)
        {
            if (ShouldFailConnection)
                return Task.FromResult(Result.Fail<VisionWaitIdleResponse, VisionBridgeError>(new VisionBridgeError("CONN_REFUSED", "Connection refused", "Check game status")));
            return Task.FromResult(Result.Ok<VisionWaitIdleResponse, VisionBridgeError>(new VisionWaitIdleResponse
            {
                IdleReached = true,
                ElapsedFrames = 3
            }));
        }

        public Task<Result<VisionResizeResponse, VisionBridgeError>> ResizeAsync(string baseUrl, string token, int width, int height, CancellationToken ct = default)
        {
            return Task.FromResult(Result.Ok<VisionResizeResponse, VisionBridgeError>(new VisionResizeResponse { Success = true, ScreenWidth = width, ScreenHeight = height }));
        }

        public Task<Result<VisionSceneInfoDto, VisionBridgeError>> GetSceneInfoAsync(string baseUrl, string token, CancellationToken ct = default)
        {
            if (ShouldFailConnection)
                return Task.FromResult(Result.Fail<VisionSceneInfoDto, VisionBridgeError>(new VisionBridgeError("CONN_REFUSED", "Connection refused", "Check game status")));
            if (FailSceneInfo)
                return Task.FromResult(Result.Fail<VisionSceneInfoDto, VisionBridgeError>(new VisionBridgeError("NOT_FOUND", "Endpoint not found", "Route unsupported")));
            return Task.FromResult(Result.Ok<VisionSceneInfoDto, VisionBridgeError>(SceneInfo));
        }

        public SceneObjectsQuery? LastSceneObjectsQuery { get; private set; }
        public RuntimeAssetsQuery? LastRuntimeAssetsQuery { get; private set; }

        public Task<Result<VisionSceneObjectsResponse, VisionBridgeError>> InspectSceneObjectsAsync(
            string baseUrl,
            string token,
            SceneObjectsQuery? query = null,
            CancellationToken ct = default)
        {
            LastSceneObjectsQuery = query;
            if (ShouldFailConnection)
                return Task.FromResult(Result.Fail<VisionSceneObjectsResponse, VisionBridgeError>(new VisionBridgeError("CONN_REFUSED", "Connection refused", "Check game status")));

            return Task.FromResult(Result.Ok<VisionSceneObjectsResponse, VisionBridgeError>(new VisionSceneObjectsResponse
            {
                Success = true,
                TotalCameras = 1,
                TotalRenderers = 1,
                TotalMatched = 1,
                Truncated = false,
                Cameras = new List<VisionSceneCameraDto>
                {
                    new VisionSceneCameraDto { Name = "Main Camera", Position = new[] { 0f, 0f, -10f }, Orthographic = true, OrthographicSize = 5f, CullingMask = -1, Depth = 0f }
                },
                Renderers = new List<VisionSceneRendererDto>
                {
                    new VisionSceneRendererDto
                    {
                        Name = "TestObject",
                        FullPath = "Root/TestObject",
                        RendererType = "SpriteRenderer",
                        SortingLayerName = "Default",
                        SortingOrder = 10,
                        Center = new[] { 0f, 0f, 0f },
                        Extents = new[] { 1f, 1f, 1f },
                        IsVisible = true,
                        Materials = new List<VisionSceneMaterialDto>
                        {
                            new VisionSceneMaterialDto { MaterialName = "Default-Sprite", ShaderName = "Sprites/Default" }
                        }
                    }
                },
                Message = "Scene objects enumerated successfully."
            }));
        }

        public Task<Result<VisionRuntimeAssetsResponse, VisionBridgeError>> InspectRuntimeAssetsAsync(
            string baseUrl,
            string token,
            RuntimeAssetsQuery? query = null,
            CancellationToken ct = default)
        {
            LastRuntimeAssetsQuery = query;
            if (ShouldFailConnection)
                return Task.FromResult(Result.Fail<VisionRuntimeAssetsResponse, VisionBridgeError>(new VisionBridgeError("CONN_REFUSED", "Connection refused", "Check game status")));

            return Task.FromResult(Result.Ok<VisionRuntimeAssetsResponse, VisionBridgeError>(new VisionRuntimeAssetsResponse
            {
                Success = true,
                TotalBundles = 1,
                TotalLoadedAssets = 1,
                Truncated = false,
                Bundles = new List<VisionLoadedBundleDto>
                {
                    new VisionLoadedBundleDto { Name = "core_sprites", IsStreamedScene = false, AssetCount = 12 }
                },
                LoadedAssets = new List<VisionLoadedAssetDto>
                {
                    new VisionLoadedAssetDto { Name = "SampleSprite", AssetType = "Sprite", InstanceId = 101 }
                },
                Message = "Runtime assets inspected."
            }));
        }
    }

    private sealed class DummyGameDetector : IGameDetector
    {
        public Task<Result<GameEnvironment, DetectionError>> DetectGameAsync(string gameDirectoryPath, CancellationToken ct = default)
        {
            var env = new GameEnvironment(
                Id: new GameId("TestGame"),
                GameName: "TestGame",
                GameRootPath: gameDirectoryPath,
                ManagedPath: Path.Combine(gameDirectoryPath, "Managed"),
                EngineType: UnityEngineType.Mono,
                GameAssemblyPaths: Array.Empty<string>(),
                HasBepInExInstalled: true);
            return Task.FromResult(Result.Ok<GameEnvironment, DetectionError>(env));
        }
    }

    private static AgentWorkbenchService CreateWorkbench(FakeVisionBridgeClient fakeClient)
    {
        var dummyDetector = new DummyGameDetector();
        return new AgentWorkbenchService(
            scaffoldModUseCase: null!,
            buildModUseCase: null!,
            readLogsUseCase: null!,
            getTypeDetailUseCase: null!,
            gameDetector: dummyDetector,
            visionBridgeClient: fakeClient);
    }

    [Fact]
    public async Task GetGameCapabilitiesAsync_WhenConnected_ReturnsCapabilitiesAndUntestedNotes()
    {
        var fakeClient = new FakeVisionBridgeClient();
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.GetGameCapabilitiesAsync(port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        var caps = root.GetProperty("capabilities");
        Assert.True(caps.GetProperty("ugui").GetBoolean());
        Assert.True(caps.GetProperty("inputSystem").GetBoolean());
        Assert.Contains("provato una volta su un gioco (Potion Craft)", caps.GetProperty("inputClickStatus").GetString()!);
        Assert.Contains("legacy e postmessage sono implementati ma mai provati dal vivo", caps.GetProperty("untestedMethods").GetString()!);
    }

    [Fact]
    public async Task GetGameCapabilitiesAsync_WhenDisconnected_ReturnsClearGiocoNonConnessoError()
    {
        var fakeClient = new FakeVisionBridgeClient { ShouldFailConnection = true };
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.GetGameCapabilitiesAsync(port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("gioco non connesso", root.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("suggestion").GetString()));
    }

    [Fact]
    public async Task GetGameCapabilitiesAsync_WhenNoPortConfigured_ReturnsClearGiocoNonConnessoError()
    {
        var fakeClient = new FakeVisionBridgeClient();
        var workbench = CreateWorkbench(fakeClient);

        string? oldPort = Environment.GetEnvironmentVariable("CANDIED_PORT");
        try
        {
            Environment.SetEnvironmentVariable("CANDIED_PORT", null);
            string json = await workbench.GetGameCapabilitiesAsync(port: null, token: null);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("success").GetBoolean());
            Assert.Equal("gioco non connesso", root.GetProperty("error").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CANDIED_PORT", oldPort);
        }
    }

    [Fact]
    public async Task CaptureGameScreenshotAsync_WhenSuccessful_WritesFileAndReturnsMetadata()
    {
        var fakeClient = new FakeVisionBridgeClient();
        var workbench = CreateWorkbench(fakeClient);
        string tempOut = Path.Combine(Path.GetTempPath(), $"candied_test_{Guid.NewGuid():N}.png");

        try
        {
            string json = await workbench.CaptureGameScreenshotAsync(port: 9999, token: "test-token", savePath: tempOut);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(tempOut, root.GetProperty("filePath").GetString());
            Assert.Equal(1024, root.GetProperty("width").GetInt32());
            Assert.Equal(720, root.GetProperty("height").GetInt32());
            Assert.False(root.GetProperty("isUniform").GetBoolean());
            Assert.True(File.Exists(tempOut));
        }
        finally
        {
            if (File.Exists(tempOut)) File.Delete(tempOut);
        }
    }

    [Fact]
    public async Task CaptureGameScreenshotAsync_WhenScreenshotCapabilityFalse_ReturnsActionableError()
    {
        var fakeClient = new FakeVisionBridgeClient();
        fakeClient.Capabilities.Screenshot = false;
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.CaptureGameScreenshotAsync(port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("screenshot non disponibile", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ClickGameAtAsync_WhenSuccessful_DispatchesCoordinatesAndReturnsWarning()
    {
        var fakeClient = new FakeVisionBridgeClient();
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.ClickGameAtAsync(511f, 416f, button: 0, holdFrames: 8, method: null, port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(511f, fakeClient.LastClickedX);
        Assert.Equal(416f, fakeClient.LastClickedY);
        Assert.Equal(8, fakeClient.LastHoldFrames);
        Assert.Contains("Il click viene inviato tramite input injection", root.GetProperty("warning").GetString()!);
    }

    [Fact]
    public async Task ClickGameAtAsync_WhenNoInputCapability_ReturnsActionableError()
    {
        var fakeClient = new FakeVisionBridgeClient();
        fakeClient.Capabilities.InputSystem = false;
        fakeClient.Capabilities.LegacyInput = false;
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.ClickGameAtAsync(511f, 416f, port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("input non disponibile", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WaitGameIdleAsync_WhenSuccessful_ReturnsIdleReached()
    {
        var fakeClient = new FakeVisionBridgeClient();
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.WaitGameIdleAsync(maxFrames: 10, timeoutMs: 3000, port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.True(root.GetProperty("idleReached").GetBoolean());
        Assert.Equal(3, root.GetProperty("elapsedFrames").GetInt32());
    }

    [Fact]
    public async Task GetGameSceneInfoAsync_WhenSceneRouteAvailable_ReturnsSceneInfo()
    {
        var fakeClient = new FakeVisionBridgeClient();
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.GetGameSceneInfoAsync(port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("MainScene", root.GetProperty("activeSceneName").GetString());
        Assert.Equal(0, root.GetProperty("activeSceneIndex").GetInt32());
        Assert.Equal(10, root.GetProperty("rootCount").GetInt32());
    }

    [Fact]
    public async Task GetGameSceneInfoAsync_WhenSceneFails_FallsBackToHello()
    {
        var fakeClient = new FakeVisionBridgeClient { FailSceneInfo = true };
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.GetGameSceneInfoAsync(port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Unknown", root.GetProperty("activeSceneName").GetString());
        Assert.Equal(1024, root.GetProperty("screenWidth").GetInt32());
        Assert.Equal(720, root.GetProperty("screenHeight").GetInt32());
    }

    [Fact]
    public async Task ListGameUiElementsAsync_WhenUiEnumerable_ReturnsElementsList()
    {
        var fakeClient = new FakeVisionBridgeClient();
        fakeClient.Capabilities.Ugui = true;
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.ListGameUiElementsAsync(port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(1, root.GetProperty("count").GetInt32());
        var elems = root.GetProperty("elements");
        Assert.Equal("Canvas/PlayButton", elems[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task ListGameUiElementsAsync_WhenUiNotEnumerable_ReturnsActionableErrorWithAlternative()
    {
        var fakeClient = new FakeVisionBridgeClient();
        fakeClient.Capabilities.Ugui = false;
        fakeClient.Capabilities.UiToolkit = false;
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.ListGameUiElementsAsync(port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("list_ui non disponibile", root.GetProperty("error").GetString());
        Assert.Equal("list_ui non disponibile: usa screenshot + click", root.GetProperty("suggestion").GetString());
    }

    [Fact]
    public async Task ListGameUiElementsAsync_WhenUiEnumerableButEmpty_ReturnsZeroCountAndAlternativeSuggestion()
    {
        var fakeClient = new FakeVisionBridgeClient();
        fakeClient.Capabilities.Ugui = true;
        fakeClient.Elements.Clear();
        var workbench = CreateWorkbench(fakeClient);

        string json = await workbench.ListGameUiElementsAsync(port: 9999, token: "test-token");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(0, root.GetProperty("count").GetInt32());
        Assert.Contains("usa screenshot + click", root.GetProperty("suggestion").GetString());
    }

    [Fact]
    public async Task McpServer_HandlesGameToolsCalls()
    {
        var fakeClient = new FakeVisionBridgeClient();
        var workbench = CreateWorkbench(fakeClient);
        var mcpServer = new McpServer(workbench);

        // Test capabilities via MCP
        string capRequest = """
        {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "tools/call",
            "params": {
                "name": "capabilities",
                "arguments": { "port": 9999, "token": "test-token" }
            }
        }
        """;
        var capResponseJson = await mcpServer.HandleMessageAsync(capRequest);
        var text = ExtractMcpText(capResponseJson);
        Assert.Contains("\"ugui\":true", text);

        // Test click missing parameters error
        string badClickRequest = """
        {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "tools/call",
            "params": {
                "name": "click",
                "arguments": { }
            }
        }
        """;
        var badClickResp = await mcpServer.HandleMessageAsync(badClickRequest);
        var badClickText = ExtractMcpText(badClickResp);
        Assert.Contains("obbligatori mancanti", badClickText);

        // Test list-ui with reduced capabilities
        fakeClient.Capabilities.Ugui = false;
        fakeClient.Capabilities.UiToolkit = false;
        string listUiRequest = """
        {
            "jsonrpc": "2.0",
            "id": 3,
            "method": "tools/call",
            "params": {
                "name": "list-ui",
                "arguments": { "port": 9999, "token": "test-token" }
            }
        }
        """;
        var listUiResp = await mcpServer.HandleMessageAsync(listUiRequest);
        var listUiText = ExtractMcpText(listUiResp);
        using var listUiDoc = JsonDocument.Parse(listUiText);
        var suggestion = listUiDoc.RootElement.GetProperty("suggestion").GetString()!;
        Assert.Equal("list_ui non disponibile: usa screenshot + click", suggestion);
    }

    [Fact]
    public async Task InspectSceneObjectsAndRuntimeAssets_ForwardAllFilterParametersViaMcp()
    {
        var fakeClient = new FakeVisionBridgeClient();
        var workbench = CreateWorkbench(fakeClient);
        var mcpServer = new McpServer(workbench);

        string sceneObjRequest = """
        {
            "jsonrpc": "2.0",
            "id": 10,
            "method": "tools/call",
            "params": {
                "name": "inspect-scene-objects",
                "arguments": {
                    "name_filter": "Actor",
                    "include_inactive": true,
                    "visible_only": true,
                    "sorting_layer": "Foreground",
                    "max_results": 25,
                    "port": 9999,
                    "token": "test-token"
                }
            }
        }
        """;
        var sceneResp = await mcpServer.HandleMessageAsync(sceneObjRequest);
        var sceneText = ExtractMcpText(sceneResp);
        Assert.Contains("\"totalMatched\":1", sceneText);
        Assert.NotNull(fakeClient.LastSceneObjectsQuery);
        Assert.Equal("Actor", fakeClient.LastSceneObjectsQuery!.NameFilter);
        Assert.True(fakeClient.LastSceneObjectsQuery.IncludeInactive);
        Assert.True(fakeClient.LastSceneObjectsQuery.VisibleOnly);
        Assert.Equal("Foreground", fakeClient.LastSceneObjectsQuery.SortingLayer);
        Assert.Equal(25, fakeClient.LastSceneObjectsQuery.MaxResults);

        string runtimeAssetsRequest = """
        {
            "jsonrpc": "2.0",
            "id": 11,
            "method": "tools/call",
            "params": {
                "name": "inspect-runtime-assets",
                "arguments": {
                    "name_filter": "Sample",
                    "asset_type": "Texture2D",
                    "max_results": 15,
                    "port": 9999,
                    "token": "test-token"
                }
            }
        }
        """;
        var assetsResp = await mcpServer.HandleMessageAsync(runtimeAssetsRequest);
        var assetsText = ExtractMcpText(assetsResp);
        Assert.Contains("\"totalBundles\":1", assetsText);
        Assert.Contains("core_sprites", assetsText);
        Assert.NotNull(fakeClient.LastRuntimeAssetsQuery);
        Assert.Equal("Sample", fakeClient.LastRuntimeAssetsQuery!.NameFilter);
        Assert.Equal("Texture2D", fakeClient.LastRuntimeAssetsQuery.AssetType);
        Assert.Equal(15, fakeClient.LastRuntimeAssetsQuery.MaxResults);
    }

    [Fact]
    public void JsonLite_RoundTripsSceneInfoAndRuntimeAssetsDtos()
    {
        var scene = new VisionSceneInfoDto
        {
            ActiveSceneName = "MainStage",
            ActiveSceneIndex = 2,
            RootCount = 18,
            IsLoaded = true,
            LoadedSceneCount = 2,
            ScreenWidth = 1280,
            ScreenHeight = 720,
            ActiveRendererCount = 74,
            TimeSinceLevelLoad = 12.5f
        };
        var sceneParsed = JsonLite.DeserializeSceneInfo(JsonLite.Serialize(scene));
        Assert.Equal("MainStage", sceneParsed.ActiveSceneName);
        Assert.Equal(74, sceneParsed.ActiveRendererCount);
        Assert.Equal(12.5f, sceneParsed.TimeSinceLevelLoad, 2);

        var runtime = new VisionRuntimeAssetsResponse
        {
            Success = true,
            TotalBundles = 1,
            TotalLoadedAssets = 2,
            Truncated = true,
            Message = "ok",
            Bundles = new List<VisionLoadedBundleDto>
            {
                new VisionLoadedBundleDto
                {
                    Name = "ui_bundle",
                    IsStreamedScene = false,
                    AssetCount = 40,
                    MatchingAssetPaths = new List<string> { "assets/ui/button.png" }
                }
            },
            LoadedAssets = new List<VisionLoadedAssetDto>
            {
                new VisionLoadedAssetDto { Name = "Button_Ok", AssetType = "Sprite", InstanceId = 42 }
            }
        };
        var runtimeParsed = JsonLite.DeserializeRuntimeAssetsResponse(JsonLite.Serialize(runtime));
        Assert.True(runtimeParsed.Success);
        Assert.True(runtimeParsed.Truncated);
        Assert.Single(runtimeParsed.Bundles);
        Assert.Equal("ui_bundle", runtimeParsed.Bundles[0].Name);
        Assert.Single(runtimeParsed.Bundles[0].MatchingAssetPaths);
        Assert.Single(runtimeParsed.LoadedAssets);
        Assert.Equal("Button_Ok", runtimeParsed.LoadedAssets[0].Name);
    }

    private static string ExtractMcpText(string? responseJson)
    {
        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString()!;
    }
}
