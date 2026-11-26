using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Candied.Primitives;
using Candied.Vision.Models;
using Candied.Vision.Protocol;

namespace Candied.Vision.Ports;

public interface IVisionSensorClient
{
    Task<Result<VisionHelloDto, VisionError>> GetHelloAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<VisionCapabilitiesDto, VisionError>> GetCapabilitiesAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<VisionFrame, VisionError>> CaptureScreenshotAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<VisionUiElementDto>, VisionError>> GetUiElementsAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<VisionInputResponse, VisionError>> SendInputAsync(
        string baseUrl,
        string token,
        VisionInputRequest request,
        CancellationToken ct = default);

    Task<Result<VisionWaitIdleResponse, VisionError>> WaitForIdleAsync(
        string baseUrl,
        string token,
        int maxFrames = 10,
        int timeoutMs = 5000,
        CancellationToken ct = default);

    Task<Result<VisionResizeResponse, VisionError>> ResizeAsync(
        string baseUrl,
        string token,
        int width,
        int height,
        CancellationToken ct = default);

    Task<Result<VisionSceneInfoDto, VisionError>> GetSceneInfoAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default);

    Task<Result<VisionSceneGraphResponse, VisionError>> InspectSceneAsync(
        string baseUrl,
        string token,
        SceneGraphQuery? query = null,
        CancellationToken ct = default);

    Task<Result<VisionRuntimeAssetsResponse, VisionError>> InspectRuntimeAssetsAsync(
        string baseUrl,
        string token,
        RuntimeAssetsQuery? query = null,
        CancellationToken ct = default);
}
