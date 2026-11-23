using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Core.Ports;
using Candied.Primitives;
using Candied.VisionBridge.Protocol;

namespace Candied.Workspace.Adapters;

internal sealed class HttpVisionBridgeClientAdapter : IVisionBridgeClient
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly HttpClient _httpClient;

    public HttpVisionBridgeClientAdapter(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<Result<VisionHelloDto, VisionBridgeError>> GetHelloAsync(
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

    public async Task<Result<VisionCapabilitiesDto, VisionBridgeError>> GetCapabilitiesAsync(
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

    public async Task<Result<VisionScreenshot, VisionBridgeError>> CaptureScreenshotAsync(
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
                return ParseErrorResponse<VisionScreenshot>(resp.StatusCode, errorBody);
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
            // Quick PNG header dimension extraction if valid PNG
            if (bytes.Length >= 24 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            }

            return Result.Ok<VisionScreenshot, VisionBridgeError>(new VisionScreenshot(bytes, isUniform, width, height));
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return Result.Fail<VisionScreenshot, VisionBridgeError>(
                new VisionBridgeError("TIMEOUT", "La richiesta dello screenshot è andata in timeout: " + ex.Message, "Verifica che il gioco stia renderizzando la schermata."));
        }
        catch (Exception ex)
        {
            return Result.Fail<VisionScreenshot, VisionBridgeError>(
                new VisionBridgeError("HTTP_ERROR", ex.Message, "Verifica la connessione HTTP con VisionBridge."));
        }
    }

    public async Task<Result<IReadOnlyList<VisionUiElementDto>, VisionBridgeError>> GetUiElementsAsync(
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
            return Result.Fail<IReadOnlyList<VisionUiElementDto>, VisionBridgeError>(result.Error);
        }

        return Result.Ok<IReadOnlyList<VisionUiElementDto>, VisionBridgeError>(result.Value.Elements);
    }

    public async Task<Result<VisionClickResponse, VisionBridgeError>> SendClickAsync(
        string baseUrl,
        string token,
        VisionClickRequest request,
        CancellationToken ct = default)
    {
        string body = JsonLite.Serialize(request);
        return await SendJsonRequestAsync(
            HttpMethod.Post,
            CombineUrl(baseUrl, VisionConstants.RouteClick),
            token,
            body,
            JsonLite.DeserializeClickResponse,
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionClickResponse, VisionBridgeError>> SendInputClickAsync(
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
        var req = new VisionClickRequest
        {
            X = x,
            Y = y,
            Button = button,
            Method = method,
            Action = action,
            HoldFrames = holdFrames
        };
        string body = JsonLite.Serialize(req);
        string url = CombineUrl(baseUrl, VisionConstants.RouteInputClick);
        if (!string.IsNullOrEmpty(method))
        {
            url += "?method=" + Uri.EscapeDataString(method);
        }

        return await SendJsonRequestAsync(
            HttpMethod.Post,
            url,
            token,
            body,
            JsonLite.DeserializeClickResponse,
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionWaitIdleResponse, VisionBridgeError>> WaitForIdleAsync(
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

    public async Task<Result<VisionResizeResponse, VisionBridgeError>> ResizeAsync(
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

    public async Task<Result<VisionSceneInfoDto, VisionBridgeError>> GetSceneInfoAsync(
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

    public async Task<Result<VisionSceneObjectsResponse, VisionBridgeError>> InspectSceneObjectsAsync(
        string baseUrl,
        string token,
        SceneObjectsQuery? query = null,
        CancellationToken ct = default)
    {
        var effective = query ?? new SceneObjectsQuery();
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
        if (!string.IsNullOrWhiteSpace(effective.SortingLayer))
        {
            queryParts.Add("sortingLayer=" + Uri.EscapeDataString(effective.SortingLayer.Trim()));
        }
        if (effective.MaxResults > 0)
        {
            queryParts.Add("maxResults=" + effective.MaxResults);
        }

        string url = CombineUrl(baseUrl, VisionConstants.RouteSceneObjects);
        if (queryParts.Count > 0)
        {
            url += "?" + string.Join("&", queryParts);
        }

        return await SendJsonRequestAsync(
            HttpMethod.Get,
            url,
            token,
            null,
            responseText => System.Text.Json.JsonSerializer.Deserialize<VisionSceneObjectsResponse>(responseText, JsonOptions) ?? new VisionSceneObjectsResponse(),
            ct).ConfigureAwait(false);
    }

    public async Task<Result<VisionRuntimeAssetsResponse, VisionBridgeError>> InspectRuntimeAssetsAsync(
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
            responseText => System.Text.Json.JsonSerializer.Deserialize<VisionRuntimeAssetsResponse>(responseText, JsonOptions) ?? new VisionRuntimeAssetsResponse(),
            ct).ConfigureAwait(false);
    }

    private async Task<Result<T, VisionBridgeError>> SendJsonRequestAsync<T>(
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
            return Result.Ok<T, VisionBridgeError>(val);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            return Result.Fail<T, VisionBridgeError>(
                new VisionBridgeError("TIMEOUT", "Richiesta HTTP andata in timeout: " + ex.Message, "Verifica la reattività del gioco."));
        }
        catch (Exception ex)
        {
            return Result.Fail<T, VisionBridgeError>(
                new VisionBridgeError("HTTP_ERROR", ex.Message, "Verifica che il server VisionBridge sia attivo e raggiungibile."));
        }
    }

    private static Result<T, VisionBridgeError> ParseErrorResponse<T>(System.Net.HttpStatusCode statusCode, string responseText)
    {
        if (statusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return Result.Fail<T, VisionBridgeError>(new VisionBridgeError(
                "UNAUTHORIZED",
                "Richiesta non autorizzata (HTTP 401).",
                "Verifica che il token di sessione sia valido e presente nell'header X-Candied-Token."));
        }

        if (statusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return Result.Fail<T, VisionBridgeError>(new VisionBridgeError(
                "FORBIDDEN",
                "Richiesta rifiutata (HTTP 403).",
                "La richiesta deve essere inviata a 127.0.0.1 senza header Origin non autorizzati."));
        }

        try
        {
            var err = JsonLite.DeserializeErrorResponse(responseText);
            if (!string.IsNullOrEmpty(err.Code) && err.Code != "UNKNOWN_ERROR")
            {
                return Result.Fail<T, VisionBridgeError>(new VisionBridgeError(err.Code, err.Message, err.Suggestion));
            }
        }
        catch { }

        return Result.Fail<T, VisionBridgeError>(new VisionBridgeError(
            $"HTTP_{(int)statusCode}",
            $"Richiesta fallita con codice HTTP {(int)statusCode}: {responseText}",
            "Verifica i log di VisionBridge dentro BepInEx/LogOutput.log"));
    }

    private static string CombineUrl(string baseUrl, string path)
    {
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }
}
