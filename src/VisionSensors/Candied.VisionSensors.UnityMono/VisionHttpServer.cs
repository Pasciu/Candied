using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Candied.Vision.Protocol;
using UnityEngine;

namespace Candied.VisionSensors.UnityMono;

public sealed class VisionHttpServer : IDisposable
{
    private readonly int _port;
    private readonly string _expectedToken;
    private readonly MainThreadDispatcher _dispatcher;
    private readonly MonoBehaviour _coroutineHost;
    private readonly IUiProvider _uiProvider;
    private readonly IdleTracker _idleTracker;

    private HttpListener? _listener;
    private Thread? _listenerThread;
    private volatile bool _isRunning;
    private bool _isDisposed;

    public bool IsRunning => _isRunning;

    public VisionHttpServer(
        int port,
        string expectedToken,
        MainThreadDispatcher dispatcher,
        MonoBehaviour coroutineHost,
        IUiProvider uiProvider,
        IdleTracker idleTracker)
    {
        _port = port;
        _expectedToken = expectedToken ?? string.Empty;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _coroutineHost = coroutineHost ?? throw new ArgumentNullException(nameof(coroutineHost));
        _uiProvider = uiProvider ?? throw new ArgumentNullException(nameof(uiProvider));
        _idleTracker = idleTracker ?? throw new ArgumentNullException(nameof(idleTracker));
    }

    public void Start()
    {
        if (_isRunning) return;

        _listener = new HttpListener();
        _listener.Prefixes.Add("http://127.0.0.1:" + _port + "/");
        _listener.Start();
        _isRunning = true;

        _listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "Candied.VisionSensor.HttpServer"
        };
        _listenerThread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
        _listener = null;
    }

    private void ListenLoop()
    {
        while (_isRunning && _listener != null && _listener.IsListening)
        {
            try
            {
                var context = _listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => ProcessRequest(context));
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!_isRunning) break;
                Debug.LogWarning("[Candied.VisionSensor] ListenLoop error: " + ex.Message);
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        try
        {
            string? hostHeader = req.Headers["Host"];
            if (string.IsNullOrEmpty(hostHeader) || !hostHeader!.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                resp.StatusCode = 403;
                resp.ContentLength64 = 0;
                resp.Close();
                return;
            }

            string? originHeader = req.Headers["Origin"];
            if (!string.IsNullOrEmpty(originHeader) && !originHeader!.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                resp.StatusCode = 403;
                resp.ContentLength64 = 0;
                resp.Close();
                return;
            }

            string? token = req.Headers[VisionConstants.HeaderToken];
            if (string.IsNullOrEmpty(token) || !string.Equals(token, _expectedToken, StringComparison.Ordinal))
            {
                resp.StatusCode = 401;
                resp.ContentLength64 = 0;
                resp.Close();
                return;
            }

            string path = req.Url?.AbsolutePath ?? "/";
            string method = req.HttpMethod.ToUpperInvariant();

            if (method == "GET" && string.Equals(path, VisionConstants.RouteHello, StringComparison.OrdinalIgnoreCase))
            {
                HandleHello(resp);
            }
            else if (method == "GET" && string.Equals(path, VisionConstants.RouteCapabilities, StringComparison.OrdinalIgnoreCase))
            {
                WriteJsonResponse(resp, 200, JsonLite.Serialize(CapabilitiesDetector.Detect()));
            }
            else if (method == "GET" && string.Equals(path, VisionConstants.RouteScreenshot, StringComparison.OrdinalIgnoreCase))
            {
                HandleScreenshot(resp);
            }
            else if (method == "GET" && string.Equals(path, VisionConstants.RouteUiElements, StringComparison.OrdinalIgnoreCase))
            {
                HandleUiElements(resp);
            }
            else if (method == "POST" && string.Equals(path, VisionConstants.RouteInput, StringComparison.OrdinalIgnoreCase))
            {
                HandleInput(req, resp);
            }
            else if (method == "GET" && string.Equals(path, VisionConstants.RouteWaitIdle, StringComparison.OrdinalIgnoreCase))
            {
                HandleWaitIdle(req, resp);
            }
            else if (method == "POST" && string.Equals(path, VisionConstants.RouteResize, StringComparison.OrdinalIgnoreCase))
            {
                HandleResize(req, resp);
            }
            else if (method == "GET" && string.Equals(path, VisionConstants.RouteScene, StringComparison.OrdinalIgnoreCase))
            {
                HandleScene(resp);
            }
            else if ((method == "GET" || method == "POST") && string.Equals(path, VisionConstants.RouteSceneNodes, StringComparison.OrdinalIgnoreCase))
            {
                HandleSceneNodes(req, resp);
            }
            else if ((method == "GET" || method == "POST") && string.Equals(path, VisionConstants.RouteLoadedAssets, StringComparison.OrdinalIgnoreCase))
            {
                HandleLoadedAssets(req, resp);
            }
            else
            {
                WriteErrorResponse(resp, 404, "NOT_FOUND", "Unknown endpoint: " + method + " " + path, "Check Candied Vision Protocol v2 routes.");
            }
        }
        catch (TimeoutException ex)
        {
            WriteErrorResponse(resp, 504, "TIMEOUT", ex.Message, "Engine main thread may be blocked.");
        }
        catch (Exception ex)
        {
            WriteErrorResponse(resp, 500, "INTERNAL_ERROR", ex.Message, "Check runtime logs.");
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    private void HandleHello(HttpListenerResponse resp)
    {
        var hello = _dispatcher.Enqueue(() => new VisionHelloDto
        {
            ProtocolVersion = VisionConstants.ProtocolVersion,
            SensorVersion = VisionConstants.SensorVersion,
            EngineName = "Unity",
            EngineVariant = "Mono",
            EngineVersion = Application.unityVersion ?? string.Empty,
            ScreenWidth = Screen.width,
            ScreenHeight = Screen.height
        });

        WriteJsonResponse(resp, 200, JsonLite.Serialize(hello));
    }

    private void HandleScene(HttpListenerResponse resp)
    {
        var sceneInfo = _dispatcher.Enqueue(() =>
        {
            string sceneName = "Unknown";
            int sceneIndex = -1;
            int rootCount = 0;
            bool isLoaded = true;
            int loadedSceneCount = 1;

            try
            {
                Type? smType = CapabilitiesDetector.FindType("UnityEngine.SceneManagement.SceneManager");
                if (smType != null)
                {
                    object? scene = smType.GetMethod("GetActiveScene", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                    if (scene != null)
                    {
                        Type sceneType = scene.GetType();
                        sceneName = sceneType.GetProperty("name")?.GetValue(scene, null) as string ?? "Unknown";
                        if (sceneType.GetProperty("buildIndex")?.GetValue(scene, null) is int idx) sceneIndex = idx;
                        if (sceneType.GetProperty("isLoaded")?.GetValue(scene, null) is bool l) isLoaded = l;
                        if (sceneType.GetProperty("rootCount")?.GetValue(scene, null) is int r) rootCount = r;
                    }

                    if (smType.GetProperty("sceneCount", BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null) is int sc)
                    {
                        loadedSceneCount = sc;
                    }
                }
            }
            catch
            {
            }

            float elapsed = 0f;
            try { elapsed = Time.timeSinceLevelLoad; } catch { }

            return new VisionSceneInfoDto
            {
                ActiveSceneName = sceneName,
                ActiveSceneIndex = sceneIndex,
                RootCount = rootCount,
                IsLoaded = isLoaded,
                LoadedSceneCount = loadedSceneCount,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height,
                ActiveNodeCount = SceneObjectsInspector.CountActiveRenderers(),
                ElapsedSeconds = elapsed
            };
        });

        WriteJsonResponse(resp, 200, JsonLite.Serialize(sceneInfo));
    }

    private void HandleScreenshot(HttpListenerResponse resp)
    {
        ScreenshotResult? result = null;
        var doneEvent = new ManualResetEvent(false);

        _dispatcher.EnqueueAction(() =>
        {
            _coroutineHost.StartCoroutine(ScreenshotCapturer.CaptureCoroutine(res =>
            {
                result = res;
                doneEvent.Set();
            }));
        });

        if (!doneEvent.WaitOne(6000, false) || result == null)
        {
            WriteErrorResponse(resp, 504, "SCREENSHOT_TIMEOUT", "Screenshot capture timed out.", "Ensure the game is actively rendering frames.");
            return;
        }

        resp.ContentType = "image/png";
        resp.StatusCode = 200;
        resp.AddHeader(VisionConstants.HeaderUniformImage, result.IsUniform ? "true" : "false");
        resp.ContentLength64 = result.PngBytes.Length;
        resp.OutputStream.Write(result.PngBytes, 0, result.PngBytes.Length);
        resp.OutputStream.Flush();
    }

    private void HandleUiElements(HttpListenerResponse resp)
    {
        var elements = _dispatcher.Enqueue(() =>
        {
            var list = _uiProvider.GetInteractiveElements();
            return new VisionUiElementsResponse
            {
                Count = list.Count,
                Elements = new List<VisionUiElementDto>(list)
            };
        });

        WriteJsonResponse(resp, 200, JsonLite.Serialize(elements));
    }

    private void HandleInput(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string body = ReadRequestBody(req);
        if (string.IsNullOrEmpty(body))
        {
            WriteErrorResponse(resp, 400, "BAD_REQUEST", "Empty JSON request body.", "Provide a valid VisionInputRequest JSON payload.");
            return;
        }

        VisionInputRequest inputReq;
        try
        {
            inputReq = JsonLite.DeserializeInputRequest(body);
        }
        catch (Exception ex)
        {
            WriteErrorResponse(resp, 400, "INVALID_JSON", "Failed to parse input JSON: " + ex.Message, "Verify JSON syntax.");
            return;
        }

        string? methodOverride = req.QueryString["method"];
        VisionInputResponse? inputResult = null;
        var doneEvent = new ManualResetEvent(false);

        _dispatcher.EnqueueAction(() =>
        {
            _coroutineHost.StartCoroutine(InputDispatcher.DispatchCoroutine(
                inputReq,
                methodOverride,
                res =>
                {
                    inputResult = res;
                    doneEvent.Set();
                }));
        });

        if (!doneEvent.WaitOne(6000, false) || inputResult == null)
        {
            WriteErrorResponse(resp, 504, "INPUT_TIMEOUT", "Input dispatch timed out on Unity main thread.", "Verify game loop responsiveness.");
            return;
        }

        WriteJsonResponse(resp, inputResult.Success ? 200 : 422, JsonLite.Serialize(inputResult));
    }

    private void HandleWaitIdle(HttpListenerRequest req, HttpListenerResponse resp)
    {
        int maxFrames = ParseQueryInt(req.QueryString["frames"], 10);
        int timeoutMs = ParseQueryInt(req.QueryString["timeoutMs"], 5000);
        WriteJsonResponse(resp, 200, JsonLite.Serialize(_idleTracker.WaitForIdle(maxFrames, timeoutMs)));
    }

    private void HandleResize(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string body = ReadRequestBody(req);
        var resizeReq = JsonLite.DeserializeResizeRequest(body);
        if (resizeReq.Width <= 0 || resizeReq.Height <= 0)
        {
            WriteErrorResponse(resp, 400, "INVALID_DIMENSIONS", "Width and height must be positive.", "Provide valid pixel dimensions.");
            return;
        }

        var result = _dispatcher.Enqueue(() =>
        {
            Screen.SetResolution(resizeReq.Width, resizeReq.Height, resizeReq.Fullscreen);
            return new VisionResizeResponse
            {
                Success = true,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height,
                Message = "Resolution updated to " + resizeReq.Width + "x" + resizeReq.Height
            };
        });

        WriteJsonResponse(resp, 200, JsonLite.Serialize(result));
    }

    private void HandleSceneNodes(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string? nameFilter = req.QueryString["nameFilter"] ?? req.QueryString["name_filter"];
        bool includeInactive = ParseQueryBool(req.QueryString["includeInactive"] ?? req.QueryString["include_inactive"]);
        bool visibleOnly = ParseQueryBool(req.QueryString["visibleOnly"] ?? req.QueryString["visible_only"]);
        string? attrFilter = req.QueryString["attributeFilter"] ?? req.QueryString["attribute_filter"];
        int maxResults = ParseQueryInt(req.QueryString["maxResults"] ?? req.QueryString["max_results"], 100);

        var result = _dispatcher.Enqueue(() =>
            SceneObjectsInspector.Inspect(nameFilter, includeInactive, visibleOnly, attrFilter, maxResults));

        WriteJsonResponse(resp, 200, JsonLite.Serialize(result));
    }

    private void HandleLoadedAssets(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string? nameFilter = req.QueryString["nameFilter"] ?? req.QueryString["name_filter"];
        string? assetType = req.QueryString["assetType"] ?? req.QueryString["asset_type"];
        int maxResults = ParseQueryInt(req.QueryString["maxResults"] ?? req.QueryString["max_results"], 50);

        var result = _dispatcher.Enqueue(() =>
            RuntimeAssetsInspector.Inspect(nameFilter, assetType, maxResults));

        WriteJsonResponse(resp, 200, JsonLite.Serialize(result));
    }

    private static bool ParseQueryBool(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1", StringComparison.Ordinal);
    }

    private static int ParseQueryInt(string? value, int fallback)
    {
        if (!string.IsNullOrEmpty(value) && int.TryParse(value, out int parsed) && parsed > 0) return parsed;
        return fallback;
    }

    private static string ReadRequestBody(HttpListenerRequest req)
    {
        using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
        {
            return reader.ReadToEnd();
        }
    }

    private static void WriteJsonResponse(HttpListenerResponse resp, int statusCode, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        resp.StatusCode = statusCode;
        resp.ContentType = "application/json; charset=utf-8";
        resp.ContentLength64 = bytes.Length;
        resp.OutputStream.Write(bytes, 0, bytes.Length);
        resp.OutputStream.Flush();
    }

    private static void WriteErrorResponse(HttpListenerResponse resp, int statusCode, string code, string message, string suggestion)
    {
        WriteJsonResponse(resp, statusCode, JsonLite.Serialize(new VisionErrorResponse(code, message, suggestion)));
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            Stop();
            _isDisposed = true;
        }

        GC.SuppressFinalize(this);
    }
}
