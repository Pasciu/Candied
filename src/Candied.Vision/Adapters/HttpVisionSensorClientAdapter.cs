using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Candied.Primitives;
using Candied.Vision.Models;
using Candied.Vision.Ports;
using Candied.Vision.Protocol;

namespace Candied.Vision.Adapters;

internal sealed class HttpVisionSensorClientAdapter : IVisionSensorClient
{
    private readonly HttpClient _httpClient;

    public HttpVisionSensorClientAdapter(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<Result<VisionHelloDto, VisionError>> GetHelloAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default)
    {
        return await SendJsonRequestAsync(
            HttpMethod.Get,
            CombineUrl(baseUrl, VisionConstants.RouteHello),
            token,
            null,
            JsonLite.DeserializeHello,
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionCapabilitiesDto, VisionError>> GetCapabilitiesAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default)
    {
        return await SendJsonRequestAsync(
            HttpMethod.Get,
            CombineUrl(baseUrl, VisionConstants.RouteCapabilities),
            token,
            null,
            JsonLite.DeserializeCapabilities,
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionFrame, VisionError>> CaptureScreenshotAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, CombineUrl(baseUrl, VisionConstants.RouteScreenshot));
            req.Headers.Add(VisionConstants.HeaderToken, token);

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                string errorBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                return ParseErrorResponse<VisionFrame>(resp.StatusCode, errorBody);
            }

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            bool isUniform = false;
            if (resp.Headers.TryGetValues(VisionConstants.HeaderUniformImage, out var values))
            {
                foreach (var v in values)
                {
                    if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase))
                    {
                        isUniform = true;
                        break;
                    }
                }
            }

            int width = 0;
            int height = 0;
            if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            }

            return Result.Ok<VisionFrame, VisionError>(new VisionFrame(bytes, isUniform, width, height, VisionTier.SensorBridge));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return Result.Fail<VisionFrame, VisionError>(
                new VisionError("TIMEOUT", "Screenshot request timed out: " + ex.Message, "Verify that the game or emulator is actively rendering frames."));
        }
        catch (Exception ex)
        {
            return Result.Fail<VisionFrame, VisionError>(
                new VisionError("HTTP_ERROR", ex.Message, "Verify HTTP connectivity with the Vision sensor."));
        }
    }

    public async Task<Result<IReadOnlyList<VisionUiElementDto>, VisionError>> GetUiElementsAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default)
    {
        var result = await SendJsonRequestAsync(
            HttpMethod.Get,
            CombineUrl(baseUrl, VisionConstants.RouteUiElements),
            token,
            null,
            JsonLite.DeserializeUiElements,
            ct).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result.Fail<IReadOnlyList<VisionUiElementDto>, VisionError>(result.Error);
        }

        return Result.Ok<IReadOnlyList<VisionUiElementDto>, VisionError>(result.Value.Elements);
    }

    public async Task<Result<VisionInputResponse, VisionError>> SendInputAsync(
        string baseUrl,
        string token,
        VisionInputRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string body = JsonLite.Serialize(request);
        string url = CombineUrl(baseUrl, VisionConstants.RouteInput);
        if (!string.IsNullOrEmpty(request.Method))
        {
            url += "?method=" + Uri.EscapeDataString(request.Method);
        }

        return await SendJsonRequestAsync(
            HttpMethod.Post,
            url,
            token,
            body,
            JsonLite.DeserializeInputResponse,
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionWaitIdleResponse, VisionError>> WaitForIdleAsync(
        string baseUrl,
        string token,
        int maxFrames = 10,
        int timeoutMs = 5000,
        CancellationToken ct = default)
    {
        string url = CombineUrl(baseUrl, $"{VisionConstants.RouteWaitIdle}?frames={maxFrames}&timeoutMs={timeoutMs}");
        return await SendJsonRequestAsync(
            HttpMethod.Get,
            url,
            token,
            null,
            JsonLite.DeserializeWaitIdleResponse,
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionResizeResponse, VisionError>> ResizeAsync(
        string baseUrl,
        string token,
        int width,
        int height,
        CancellationToken ct = default)
    {
        var resizeReq = new VisionResizeRequest { Width = width, Height = height, Fullscreen = false };
        string json = JsonLite.Serialize(resizeReq);
        return await SendJsonRequestAsync(
            HttpMethod.Post,
            CombineUrl(baseUrl, VisionConstants.RouteResize),
            token,
            json,
            JsonLite.DeserializeResizeResponse,
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionSceneInfoDto, VisionError>> GetSceneInfoAsync(
        string baseUrl,
        string token,
        CancellationToken ct = default)
    {
        return await SendJsonRequestAsync(
            HttpMethod.Get,
            CombineUrl(baseUrl, VisionConstants.RouteScene),
            token,
            null,
            JsonLite.DeserializeSceneInfo,
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionSceneGraphResponse, VisionError>> InspectSceneAsync(
        string baseUrl,
        string token,
        SceneGraphQuery? query = null,
        CancellationToken ct = default)
    {
        var effective = query ?? new SceneGraphQuery();
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(effective.NameFilter))
        {
            queryParts.Add("nameFilter=" + Uri.EscapeDataString(effective.NameFilter.Trim()));
        }

        if (effective.IncludeInactive)
        {
            queryParts.Add("includeInactive=true");
        }

        if (effective.VisibleOnly)
        {
            queryParts.Add("visibleOnly=true");
        }

        if (!string.IsNullOrWhiteSpace(effective.AttributeFilter))
        {
            queryParts.Add("attributeFilter=" + Uri.EscapeDataString(effective.AttributeFilter.Trim()));
        }

        if (effective.MaxResults > 0)
        {
            queryParts.Add("maxResults=" + effective.MaxResults);
        }

        string url = CombineUrl(baseUrl, VisionConstants.RouteSceneNodes);
        if (queryParts.Count > 0)
        {
            url += "?" + string.Join("&", queryParts);
        }

        return await SendJsonRequestAsync(
            HttpMethod.Get,
            url,
            token,
            null,
            JsonLite.DeserializeSceneGraph,
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionRuntimeAssetsResponse, VisionError>> InspectRuntimeAssetsAsync(
        string baseUrl,
        string token,
        RuntimeAssetsQuery? query = null,
        CancellationToken ct = default)
    {
        var effective = query ?? new RuntimeAssetsQuery();
        var queryParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(effective.NameFilter))
        {
            queryParts.Add("nameFilter=" + Uri.EscapeDataString(effective.NameFilter.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(effective.AssetType))
        {
            queryParts.Add("assetType=" + Uri.EscapeDataString(effective.AssetType.Trim()));
        }

        if (effective.MaxResults > 0)
        {
            queryParts.Add("maxResults=" + effective.MaxResults);
        }

        string url = CombineUrl(baseUrl, VisionConstants.RouteLoadedAssets);
        if (queryParts.Count > 0)
        {
            url += "?" + string.Join("&", queryParts);
        }

        return await SendJsonRequestAsync(
            HttpMethod.Get,
            url,
            token,
            null,
            JsonLite.DeserializeRuntimeAssetsResponse,
            ct).ConfigureAwait(false);
    }

    private async Task<Result<T, VisionError>> SendJsonRequestAsync<T>(
        HttpMethod method,
        string url,
        string token,
        string? jsonBody,
        Func<string, T> deserializer,
        CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(method, url);
            req.Headers.Add(VisionConstants.HeaderToken, token);

            if (jsonBody != null)
            {
                req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            }

            using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
            string responseText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!resp.IsSuccessStatusCode)
            {
                return ParseErrorResponse<T>(resp.StatusCode, responseText);
            }

            T val = deserializer(responseText);
            return Result.Ok<T, VisionError>(val);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return Result.Fail<T, VisionError>(
                new VisionError("TIMEOUT", "HTTP request timed out: " + ex.Message, "Verify target responsiveness."));
        }
        catch (Exception ex)
        {
            return Result.Fail<T, VisionError>(
                new VisionError("HTTP_ERROR", ex.Message, "Verify that the Vision sensor HTTP server is reachable."));
        }
    }

    private static Result<T, VisionError> ParseErrorResponse<T>(HttpStatusCode statusCode, string responseText)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            return Result.Fail<T, VisionError>(new VisionError(
                "UNAUTHORIZED",
                "Unauthorized request (HTTP 401).",
                "Verify that the session token matches the X-Candied-Token header."));
        }

        if (statusCode == HttpStatusCode.Forbidden)
        {
            return Result.Fail<T, VisionError>(new VisionError(
                "FORBIDDEN",
                "Forbidden request (HTTP 403).",
                "Requests must target 127.0.0.1 without unauthorized Origin headers."));
        }

        try
        {
            var err = JsonLite.DeserializeErrorResponse(responseText);
            if (!string.IsNullOrEmpty(err.Code) && err.Code != "UNKNOWN_ERROR")
            {
                return Result.Fail<T, VisionError>(new VisionError(err.Code, err.Message, err.Suggestion));
            }
        }
        catch
        {
        }

        return Result.Fail<T, VisionError>(new VisionError(
            $"HTTP_{(int)statusCode}",
            $"Request failed with HTTP {(int)statusCode}: {responseText}",
            "Check the runtime logs of the active target."));
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }
}
