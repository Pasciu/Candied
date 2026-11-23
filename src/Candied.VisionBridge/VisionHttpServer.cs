using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Candied.VisionBridge.Protocol;
using UnityEngine;

namespace Candied.VisionBridge;

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
            Name = "Candied.VisionBridge.HttpServer"
        };
        _listenerThread.Start();
    }

    public void Stop()
    {
        _isRunning = false;
        try
        {
            _listener?.Stop();
        }
        catch { }

        try
        {
            _listener?.Close();
        }
        catch { }

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
                // Listener stopped
                break;
            }
            catch (Exception ex)
            {
                if (!_isRunning) break;
                Debug.LogWarning("[Candied.VisionBridge] Errore in ListenLoop: " + ex.Message);
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        try
        {
            // 1. Security Check: Host validation (strictly bound to 127.0.0.1)
            string? hostHeader = req.Headers["Host"];
            if (string.IsNullOrEmpty(hostHeader) ||
                !hostHeader.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                resp.StatusCode = 403;
                resp.ContentLength64 = 0;
                resp.Close();
                return;
            }

            // 2. Security Check: Origin validation (Reject unexpected Origins, no CORS)
            string? originHeader = req.Headers["Origin"];
            if (!string.IsNullOrEmpty(originHeader))
            {
                if (!originHeader.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase))
                {
                    resp.StatusCode = 403;
                    resp.ContentLength64 = 0;
                    resp.Close();
                    return;
                }
            }

            // 3. Security Check: Token validation (required on every request, 401 with no details if missing/wrong)
            string? token = req.Headers[VisionConstants.HeaderToken];
            if (string.IsNullOrEmpty(token) || !string.Equals(token, _expectedToken, StringComparison.Ordinal))
            {
                resp.StatusCode = 401;
                resp.ContentLength64 = 0;
                resp.Close();
                return;
            }

            // 4. Route Dispatching
            string path = req.Url?.AbsolutePath ?? "/";
            string method = req.HttpMethod.ToUpperInvariant();

            if (method == "GET" && string.Equals(path, VisionConstants.RouteHello, StringComparison.OrdinalIgnoreCase))
            {
                HandleHello(resp);
            }
            else if (method == "GET" && string.Equals(path, VisionConstants.RouteCapabilities, StringComparison.OrdinalIgnoreCase))
            {
                HandleCapabilities(resp);
            }
            else if (method == "GET" && string.Equals(path, VisionConstants.RouteScreenshot, StringComparison.OrdinalIgnoreCase))
            {
                HandleScreenshot(resp);
            }
            else if (method == "GET" && string.Equals(path, VisionConstants.RouteUiElements, StringComparison.OrdinalIgnoreCase))
            {
                HandleUiElements(resp);
            }
            else if (method == "POST" && string.Equals(path, VisionConstants.RouteClick, StringComparison.OrdinalIgnoreCase))
            {
                HandleClick(req, resp);
            }
            else if (method == "POST" && string.Equals(path, VisionConstants.RouteInputClick, StringComparison.OrdinalIgnoreCase))
            {
                HandleInputClick(req, resp);
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
            else if ((method == "GET" || method == "POST") && string.Equals(path, VisionConstants.RouteSceneObjects, StringComparison.OrdinalIgnoreCase))
            {
                HandleSceneObjects(req, resp);
            }
            else if ((method == "GET" || method == "POST") && string.Equals(path, VisionConstants.RouteLoadedAssets, StringComparison.OrdinalIgnoreCase))
            {
                HandleLoadedAssets(req, resp);
            }
            else
            {
                WriteErrorResponse(resp, 404, "NOT_FOUND", "Endpoint inesistente: " + method + " " + path, "Verifica gli endpoint supportati da Candied VisionBridge.");
            }
        }
        catch (TimeoutException ex)
        {
            WriteErrorResponse(resp, 504, "TIMEOUT", ex.Message, "Il main thread Unity potrebbe essere bloccato.");
        }
        catch (Exception ex)
        {
            WriteErrorResponse(resp, 500, "INTERNAL_ERROR", ex.Message, "Verifica i log Unity/BepInEx.");
        }
        finally
        {
            try
            {
                resp.Close();
            }
            catch { }
        }
    }

    private void HandleHello(HttpListenerResponse resp)
    {
        var hello = _dispatcher.Enqueue(() =>
        {
            return new VisionHelloDto
            {
                ProtocolVersion = VisionConstants.ProtocolVersion,
                PluginVersion = VisionConstants.PluginVersion,
                UnityVersion = Application.unityVersion ?? string.Empty,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height
            };
        });

        WriteJsonResponse(resp, 200, JsonLite.Serialize(hello));
    }

    private static void HandleCapabilities(HttpListenerResponse resp)
    {
        var caps = CapabilitiesDetector.Detect();
        WriteJsonResponse(resp, 200, JsonLite.Serialize(caps));
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
                    MethodInfo? getActiveScene = smType.GetMethod("GetActiveScene", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (getActiveScene != null)
                    {
                        object? scene = getActiveScene.Invoke(null, null);
                        if (scene != null)
                        {
                            Type sceneType = scene.GetType();
                            sceneName = sceneType.GetProperty("name")?.GetValue(scene, null) as string ?? "Unknown";
                            object? idxObj = sceneType.GetProperty("buildIndex")?.GetValue(scene, null);
                            if (idxObj is int idx) sceneIndex = idx;
                            object? loadedObj = sceneType.GetProperty("isLoaded")?.GetValue(scene, null);
                            if (loadedObj is bool l) isLoaded = l;
                            object? rootObj = sceneType.GetProperty("rootCount")?.GetValue(scene, null);
                            if (rootObj is int r) rootCount = r;
                        }
                    }
                    PropertyInfo? countProp = smType.GetProperty("sceneCount", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (countProp != null)
                    {
                        object? c = countProp.GetValue(null, null);
                        if (c is int sc) loadedSceneCount = sc;
                    }
                }
                else
                {
                    Type appType = typeof(Application);
                    PropertyInfo? nameProp = appType.GetProperty("loadedLevelName", BindingFlags.Public | BindingFlags.Static);
                    if (nameProp != null)
                    {
                        sceneName = nameProp.GetValue(null, null) as string ?? "Unknown";
                    }
                    PropertyInfo? levelProp = appType.GetProperty("loadedLevel", BindingFlags.Public | BindingFlags.Static);
                    if (levelProp != null)
                    {
                        object? lvl = levelProp.GetValue(null, null);
                        if (lvl is int li) sceneIndex = li;
                    }
                }
            }
            catch { }

            int activeRendererCount = SceneObjectsInspector.CountActiveRenderers();
            float timeSinceLevelLoad = 0f;
            try
            {
                timeSinceLevelLoad = Time.timeSinceLevelLoad;
            }
            catch { }

            return new VisionSceneInfoDto
            {
                ActiveSceneName = sceneName,
                ActiveSceneIndex = sceneIndex,
                RootCount = rootCount,
                IsLoaded = isLoaded,
                LoadedSceneCount = loadedSceneCount,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height,
                ActiveRendererCount = activeRendererCount,
                TimeSinceLevelLoad = timeSinceLevelLoad
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
            WriteErrorResponse(resp, 504, "SCREENSHOT_TIMEOUT", "Cattura dello screenshot fallita o andata in timeout.", "Assicurati che il gioco stia renderizzando frame attivi.");
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
                Elements = new System.Collections.Generic.List<VisionUiElementDto>(list)
            };
        });

        WriteJsonResponse(resp, 200, JsonLite.Serialize(elements));
    }

    private void HandleClick(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string body = ReadRequestBody(req);
        var clickReq = JsonLite.DeserializeClickRequest(body);
        var clickResp = _dispatcher.Enqueue(() =>
        {
            return UguiClickDispatcher.Click(clickReq);
        });

        int statusCode = clickResp.Success ? 200 : 422;
        WriteJsonResponse(resp, statusCode, JsonLite.Serialize(clickResp));
    }

    private void HandleInputClick(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string body = ReadRequestBody(req);
        if (string.IsNullOrEmpty(body))
        {
            WriteErrorResponse(resp, 400, "BAD_REQUEST", "Corpo richiesta JSON vuoto.", "Invia un JSON con coordinate x e y.");
            return;
        }

        VisionClickRequest clickReq;
        try
        {
            clickReq = JsonLite.DeserializeClickRequest(body);
        }
        catch (Exception ex)
        {
            WriteErrorResponse(resp, 400, "INVALID_JSON", "Errore nel parsing del JSON di input/click: " + ex.Message, "Verifica la sintassi JSON.");
            return;
        }

        if (!clickReq.X.HasValue || !clickReq.Y.HasValue)
        {
            WriteErrorResponse(resp, 400, "MISSING_COORDINATES", "Coordinate 'x' e 'y' obbligatorie per /input/click.", "Specifica 'x' e 'y' in coordinate dello screenshot.");
            return;
        }

        string? methodParam = req.QueryString["method"];
        string? method = !string.IsNullOrEmpty(methodParam) ? methodParam : clickReq.Method;

        VisionClickResponse? clickResult = null;
        var doneEvent = new ManualResetEvent(false);

        _dispatcher.EnqueueAction(() =>
        {
            _coroutineHost.StartCoroutine(InputClicker.ClickCoroutine(
                clickReq.X.Value,
                clickReq.Y.Value,
                clickReq.Button,
                method,
                clickReq.Action,
                clickReq.HoldFrames > 0 ? clickReq.HoldFrames : 5,
                res =>
                {
                    clickResult = res;
                    doneEvent.Set();
                }));
        });

        if (!doneEvent.WaitOne(6000, false) || clickResult == null)
        {
            WriteErrorResponse(resp, 504, "INPUT_CLICK_TIMEOUT", "Esecuzione dell'input click andata in timeout sul main thread Unity.", "Verifica che il loop di gioco Unity non sia bloccato.");
            return;
        }

        int statusCode = clickResult.Success ? 200 : 422;
        WriteJsonResponse(resp, statusCode, JsonLite.Serialize(clickResult));
    }

    private void HandleWaitIdle(HttpListenerRequest req, HttpListenerResponse resp)
    {
        int maxFrames = 10;
        int timeoutMs = 5000;

        string? qFrames = req.QueryString["frames"];
        if (int.TryParse(qFrames, out int f) && f > 0) maxFrames = f;

        string? qTimeout = req.QueryString["timeoutMs"];
        if (int.TryParse(qTimeout, out int t) && t > 0) timeoutMs = t;

        var idleResp = _idleTracker.WaitForIdle(maxFrames, timeoutMs);
        WriteJsonResponse(resp, 200, JsonLite.Serialize(idleResp));
    }

    private void HandleResize(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string body = ReadRequestBody(req);
        if (string.IsNullOrEmpty(body))
        {
            WriteErrorResponse(resp, 400, "BAD_REQUEST", "Corpo richiesta JSON vuoto.", "Invia un JSON con width e height.");
            return;
        }

        VisionResizeRequest resizeReq;
        try
        {
            resizeReq = JsonLite.DeserializeResizeRequest(body);
        }
        catch (Exception ex)
        {
            WriteErrorResponse(resp, 400, "INVALID_JSON", "Errore nel parsing del JSON di resize: " + ex.Message, "Verifica la sintassi JSON.");
            return;
        }

        if (resizeReq.Width <= 0 || resizeReq.Height <= 0)
        {
            WriteErrorResponse(resp, 400, "INVALID_DIMENSIONS", "Width e height devono essere maggiori di zero.", "Specifica dimensioni valide.");
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
                Message = "Screen.SetResolution invocato per " + resizeReq.Width + "x" + resizeReq.Height
            };
        });

        WriteJsonResponse(resp, 200, JsonLite.Serialize(result));
    }

    private void HandleSceneObjects(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string? nameFilter = req.QueryString["nameFilter"] ?? req.QueryString["name_filter"];
        bool includeInactive = ParseQueryBool(req.QueryString["includeInactive"] ?? req.QueryString["include_inactive"]);
        bool visibleOnly = ParseQueryBool(req.QueryString["visibleOnly"] ?? req.QueryString["visible_only"]);
        string? sortingLayer = req.QueryString["sortingLayer"] ?? req.QueryString["sorting_layer"];
        int maxResults = ParseQueryInt(req.QueryString["maxResults"] ?? req.QueryString["max_results"], 100);

        if (req.HttpMethod == "POST" && req.HasEntityBody)
        {
            var body = ReadRequestBody(req);
            var json = JsonLite.Parse(body);
            string f = json["nameFilter"].AsString();
            if (string.IsNullOrEmpty(f)) f = json["name_filter"].AsString();
            if (!string.IsNullOrEmpty(f)) nameFilter = f;

            if (json["includeInactive"].Type == JsonType.Boolean) includeInactive = json["includeInactive"].AsBoolean();
            else if (json["include_inactive"].Type == JsonType.Boolean) includeInactive = json["include_inactive"].AsBoolean();

            if (json["visibleOnly"].Type == JsonType.Boolean) visibleOnly = json["visibleOnly"].AsBoolean();
            else if (json["visible_only"].Type == JsonType.Boolean) visibleOnly = json["visible_only"].AsBoolean();

            string sl = json["sortingLayer"].AsString();
            if (string.IsNullOrEmpty(sl)) sl = json["sorting_layer"].AsString();
            if (!string.IsNullOrEmpty(sl)) sortingLayer = sl;

            if (json["maxResults"].Type == JsonType.Number) maxResults = json["maxResults"].AsInt(maxResults);
            else if (json["max_results"].Type == JsonType.Number) maxResults = json["max_results"].AsInt(maxResults);
        }

        var result = _dispatcher.Enqueue(() =>
            SceneObjectsInspector.Inspect(nameFilter, includeInactive, visibleOnly, sortingLayer, maxResults));

        WriteJsonResponse(resp, 200, JsonLite.Serialize(result));
    }

    private void HandleLoadedAssets(HttpListenerRequest req, HttpListenerResponse resp)
    {
        string? nameFilter = req.QueryString["nameFilter"] ?? req.QueryString["name_filter"];
        string? assetType = req.QueryString["assetType"] ?? req.QueryString["asset_type"];
        int maxResults = ParseQueryInt(req.QueryString["maxResults"] ?? req.QueryString["max_results"], 50);

        if (req.HttpMethod == "POST" && req.HasEntityBody)
        {
            var body = ReadRequestBody(req);
            var json = JsonLite.Parse(body);
            string f = json["nameFilter"].AsString();
            if (string.IsNullOrEmpty(f)) f = json["name_filter"].AsString();
            if (!string.IsNullOrEmpty(f)) nameFilter = f;

            string t = json["assetType"].AsString();
            if (string.IsNullOrEmpty(t)) t = json["asset_type"].AsString();
            if (!string.IsNullOrEmpty(t)) assetType = t;

            if (json["maxResults"].Type == JsonType.Number) maxResults = json["maxResults"].AsInt(maxResults);
            else if (json["max_results"].Type == JsonType.Number) maxResults = json["max_results"].AsInt(maxResults);
        }

        var result = _dispatcher.Enqueue(() =>
            RuntimeAssetsInspector.Inspect(nameFilter, assetType, maxResults));

        WriteJsonResponse(resp, 200, JsonLite.Serialize(result));
    }

    private static bool ParseQueryBool(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.Ordinal);
    }

    private static int ParseQueryInt(string? value, int fallback)
    {
        if (!string.IsNullOrEmpty(value) && int.TryParse(value, out int parsed) && parsed > 0)
        {
            return parsed;
        }
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
        var err = new VisionErrorResponse(code, message, suggestion);
        string json = JsonLite.Serialize(err);
        WriteJsonResponse(resp, statusCode, json);
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
