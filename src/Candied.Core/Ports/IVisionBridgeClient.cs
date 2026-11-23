using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Primitives;
using Candied.VisionBridge.Protocol;

namespace Candied.Core.Ports;

public interface IVisionBridgeClient
{
    Task<Result<VisionHelloDto, VisionBridgeError>> GetHelloAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<VisionCapabilitiesDto, VisionBridgeError>> GetCapabilitiesAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<VisionScreenshot, VisionBridgeError>> CaptureScreenshotAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<VisionUiElementDto>, VisionBridgeError>> GetUiElementsAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<VisionClickResponse, VisionBridgeError>> SendClickAsync(
        string baseUrl,
        string token,
        VisionClickRequest request,
        CancellationToken ct = default);

    Task<Result<VisionClickResponse, VisionBridgeError>> SendInputClickAsync(
        string baseUrl,
        string token,
        float x,
        float y,
        int button = 0,
        string? method = null,
        string? action = null,
        int holdFrames = 5,
        CancellationToken ct = default);

    Task<Result<VisionWaitIdleResponse, VisionBridgeError>> WaitForIdleAsync(
        string baseUrl,
        string token,
        int maxFrames = 10,
        int timeoutMs = 5000,
        CancellationToken ct = default);

    Task<Result<VisionResizeResponse, VisionBridgeError>> ResizeAsync(
        string baseUrl,
        string token,
        int width,
        int height,
        CancellationToken ct = default);

    Task<Result<VisionSceneInfoDto, VisionBridgeError>> GetSceneInfoAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<VisionSceneObjectsResponse, VisionBridgeError>> InspectSceneObjectsAsync(
        string baseUrl,
        string token,
        SceneObjectsQuery? query = null,
        CancellationToken ct = default);

    Task<Result<VisionRuntimeAssetsResponse, VisionBridgeError>> InspectRuntimeAssetsAsync(
        string baseUrl,
        string token,
        RuntimeAssetsQuery? query = null,
        CancellationToken ct = default);
}
