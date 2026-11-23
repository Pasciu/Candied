namespace Candied.Tests.Unit;

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Ports;
using Candied.VisionBridge.Protocol;
using Candied.Workspace.Adapters;
using Xunit;

public sealed class HttpVisionBridgeClientAdapterTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task GetHelloAsync_ValidResponse_ReturnsSuccessAndSendsTokenHeader()
    {
        string expectedToken = "secret-token-123";
        bool tokenHeaderFound = false;

        var handler = new MockHttpMessageHandler(req =>
        {
            if (req.Headers.TryGetValues(VisionConstants.HeaderToken, out var values))
            {
                foreach (var v in values)
                {
                    if (v == expectedToken) tokenHeaderFound = true;
                }
            }

            var hello = new VisionHelloDto
            {
                ProtocolVersion = "1.0.0",
                PluginVersion = "1.0.0",
                UnityVersion = "2021.3.16f1",
                ScreenWidth = 1920,
                ScreenHeight = 1080
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonLite.Serialize(hello), Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.GetHelloAsync("http://127.0.0.1:5000", expectedToken);

        Assert.True(result.IsSuccess);
        Assert.True(tokenHeaderFound);
        Assert.Equal("1.0.0", result.Value.ProtocolVersion);
        Assert.Equal(1920, result.Value.ScreenWidth);
    }

    [Fact]
    public async Task GetCapabilitiesAsync_ValidResponse_ReturnsCapabilities()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var caps = new VisionCapabilitiesDto
            {
                Ugui = true,
                EventSystem = true,
                UiToolkit = false,
                LegacyInput = true,
                InputSystem = false,
                Screenshot = true,
                ScreenshotMethod = "camera_fallback",
                ScreenshotLimitations = "No overlay UI"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonLite.Serialize(caps), Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.GetCapabilitiesAsync("http://127.0.0.1:5000", "token");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Ugui);
        Assert.True(result.Value.EventSystem);
        Assert.Equal("camera_fallback", result.Value.ScreenshotMethod);
        Assert.Equal("No overlay UI", result.Value.ScreenshotLimitations);
    }

    [Fact]
    public async Task CaptureScreenshotAsync_ParsesUniformHeaderAndPngBytes()
    {
        // Minimal PNG signature + IHDR chunk (100x200)
        byte[] fakePng = new byte[30];
        fakePng[0] = 0x89; fakePng[1] = 0x50; fakePng[2] = 0x4E; fakePng[3] = 0x47; // \x89PNG
        fakePng[16] = 0; fakePng[17] = 0; fakePng[18] = 0; fakePng[19] = 100; // width 100
        fakePng[20] = 0; fakePng[21] = 0; fakePng[22] = 0; fakePng[23] = 200; // height 200

        var handler = new MockHttpMessageHandler(req =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(fakePng)
            };
            response.Headers.Add(VisionConstants.HeaderUniformImage, "true");
            return response;
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.CaptureScreenshotAsync("http://127.0.0.1:5000", "token");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsUniform);
        Assert.Equal(100, result.Value.Width);
        Assert.Equal(200, result.Value.Height);
        Assert.Equal(30, result.Value.PngBytes.Length);
    }

    [Fact]
    public async Task GetUiElementsAsync_ReturnsListOfElements()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var resp = new VisionUiElementsResponse
            {
                Count = 1,
                Elements = new List<VisionUiElementDto>
                {
                    new VisionUiElementDto
                    {
                        Id = "Canvas/Btn",
                        Type = "Button",
                        Text = "Click Me",
                        PixelRect = new VisionRectDto(10, 20, 100, 40),
                        NormalizedRect = new VisionRectDto(0.01f, 0.02f, 0.05f, 0.04f),
                        Interactable = true,
                        Visible = true
                    }
                }
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonLite.Serialize(resp), Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.GetUiElementsAsync("http://127.0.0.1:5000", "token");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.Equal("Canvas/Btn", result.Value[0].Id);
    }

    [Fact]
    public async Task SendClickAsync_ById_SendsPayloadAndReceivesHitResponse()
    {
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

            var resp = new VisionClickResponse
            {
                Success = true,
                HitElementId = "Canvas/Btn",
                Message = "Raycast hit button"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonLite.Serialize(resp), Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.SendClickAsync("http://127.0.0.1:5000", "token", new VisionClickRequest { Id = "Canvas/Btn" });

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"id\":\"Canvas/Btn\"", capturedBody);
        Assert.True(result.Value.Success);
        Assert.Equal("Canvas/Btn", result.Value.HitElementId);
    }

    [Fact]
    public async Task WaitForIdleAsync_ValidResponse_ReturnsIdleState()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            var resp = new VisionWaitIdleResponse
            {
                IdleReached = true,
                ElapsedFrames = 10,
                ElapsedMs = 160,
                Reason = "UI stable for 10 frames"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonLite.Serialize(resp), Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.WaitForIdleAsync("http://127.0.0.1:5000", "token");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IdleReached);
        Assert.Equal(10, result.Value.ElapsedFrames);
    }

    [Fact]
    public async Task ResizeAsync_SendsPayloadAndReceivesResponse()
    {
        string? capturedBody = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            capturedBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();

            var resp = new VisionResizeResponse
            {
                Success = true,
                ScreenWidth = 1920,
                ScreenHeight = 1080,
                Message = "Resolution updated"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonLite.Serialize(resp), Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.ResizeAsync("http://127.0.0.1:5000", "token", 1920, 1080);

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"width\":1920", capturedBody);
        Assert.Contains("\"height\":1080", capturedBody);
        Assert.True(result.Value.Success);
        Assert.Equal(1920, result.Value.ScreenWidth);
        Assert.Equal(1080, result.Value.ScreenHeight);
    }

    [Fact]
    public async Task SendInputClickAsync_SendsPayloadAndReceivesResponse()
    {
        string? capturedBody = null;
        string? capturedUrl = null;

        var handler = new MockHttpMessageHandler(req =>
        {
            capturedUrl = req.RequestUri?.ToString();
            capturedBody = req.Content?.ReadAsStringAsync().Result;

            var resp = new VisionClickResponse
            {
                Success = true,
                HitPointX = 511f,
                HitPointY = 432f,
                Message = "Input click dispatched"
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonLite.Serialize(resp), Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.SendInputClickAsync("http://127.0.0.1:5000", "token", 511f, 432f, button: 0, method: "input_system");

        Assert.True(result.IsSuccess);
        Assert.NotNull(capturedUrl);
        Assert.Contains(VisionConstants.RouteInputClick, capturedUrl);
        Assert.Contains("method=input_system", capturedUrl);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"x\":511", capturedBody);
        Assert.Contains("\"y\":432", capturedBody);
        Assert.True(result.Value.Success);
        Assert.Equal(511f, result.Value.HitPointX);
        Assert.Equal(432f, result.Value.HitPointY);
    }

    [Fact]
    public async Task WhenUnauthorized_ReturnsStructuredErrorWithoutLeakingToken()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(string.Empty)
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.GetHelloAsync("http://127.0.0.1:5000", "bad-token");

        Assert.True(result.IsFailure);
        Assert.Equal("UNAUTHORIZED", result.Error.Code);
        Assert.DoesNotContain("bad-token", result.Error.Message);
        Assert.DoesNotContain("bad-token", result.Error.Suggestion);
    }

    [Fact]
    public async Task GetSceneInfoAsync_ValidResponse_DeserializesAccurately()
    {
        var handler = new MockHttpMessageHandler(req =>
        {
            Assert.Contains(VisionConstants.RouteScene, req.RequestUri!.ToString());
            var scene = new VisionSceneInfoDto
            {
                ActiveSceneName = "TestScene",
                ActiveSceneIndex = 1,
                RootCount = 5,
                IsLoaded = true,
                LoadedSceneCount = 1,
                ScreenWidth = 1280,
                ScreenHeight = 720
            };

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonLite.Serialize(scene), Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler);
        var client = new HttpVisionBridgeClientAdapter(httpClient);

        var result = await client.GetSceneInfoAsync("http://127.0.0.1:5000", "test-token");

        Assert.True(result.IsSuccess);
        Assert.Equal("TestScene", result.Value.ActiveSceneName);
        Assert.Equal(1, result.Value.ActiveSceneIndex);
        Assert.Equal(5, result.Value.RootCount);
        Assert.Equal(1280, result.Value.ScreenWidth);
    }
}
