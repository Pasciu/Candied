using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Candied.VisionBridge.Protocol;

public enum JsonType
{
    Null,
    Boolean,
    Number,
    String,
    Array,
    Object
}

public sealed class JsonValue
{
    public JsonType Type { get; }
    public object? Value { get; }

    public JsonValue(JsonType type, object? value)
    {
        Type = type;
        Value = value;
    }

    public static JsonValue Null => new JsonValue(JsonType.Null, null);
    public static JsonValue From(bool value) => new JsonValue(JsonType.Boolean, value);
    public static JsonValue From(double value) => new JsonValue(JsonType.Number, value);
    public static JsonValue From(string? value) => value == null ? Null : new JsonValue(JsonType.String, value);
    public static JsonValue From(List<JsonValue> array) => new JsonValue(JsonType.Array, array);
    public static JsonValue From(Dictionary<string, JsonValue> obj) => new JsonValue(JsonType.Object, obj);

    public bool AsBoolean(bool defaultValue = false) => Value is bool b ? b : defaultValue;
    public string AsString(string defaultValue = "") => Value?.ToString() ?? defaultValue;
    public int AsInt(int defaultValue = 0) => Value is double d ? (int)d : (int.TryParse(AsString(), out var parsed) ? parsed : defaultValue);
    public long AsLong(long defaultValue = 0) => Value is double d ? (long)d : (long.TryParse(AsString(), out var parsed) ? parsed : defaultValue);
    public float AsFloat(float defaultValue = 0f) => Value is double d ? (float)d : (float.TryParse(AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue);
    public double AsDouble(double defaultValue = 0.0) => Value is double d ? d : (double.TryParse(AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue);

    public List<JsonValue> AsArray() => Value as List<JsonValue> ?? new List<JsonValue>();
    public Dictionary<string, JsonValue> AsObject() => Value as Dictionary<string, JsonValue> ?? new Dictionary<string, JsonValue>();

    public JsonValue this[string key]
    {
        get
        {
            if (Value is Dictionary<string, JsonValue> dict)
            {
                foreach (var kvp in dict)
                {
                    if (string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
                    {
                        return kvp.Value;
                    }
                }
            }
            return Null;
        }
    }

    public JsonValue this[int index]
    {
        get
        {
            if (Value is List<JsonValue> list && index >= 0 && index < list.Count)
            {
                return list[index];
            }
            return Null;
        }
    }
}

public static class JsonLite
{
    public static string Serialize(object? obj)
    {
        var sb = new StringBuilder();
        WriteValue(sb, obj);
        return sb.ToString();
    }

    private static void WriteValue(StringBuilder sb, object? obj)
    {
        if (obj == null)
        {
            sb.Append("null");
            return;
        }

        if (obj is bool b)
        {
            sb.Append(b ? "true" : "false");
            return;
        }

        if (obj is string s)
        {
            WriteString(sb, s);
            return;
        }

        if (obj is int or long or short or byte)
        {
            sb.Append(Convert.ToString(obj, CultureInfo.InvariantCulture));
            return;
        }

        if (obj is float f)
        {
            sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
            return;
        }

        if (obj is double d)
        {
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            return;
        }

        if (obj is VisionHelloDto hello)
        {
            sb.Append("{");
            sb.Append("\"protocolVersion\":"); WriteString(sb, hello.ProtocolVersion);
            sb.Append(",\"pluginVersion\":"); WriteString(sb, hello.PluginVersion);
            sb.Append(",\"unityVersion\":"); WriteString(sb, hello.UnityVersion);
            sb.Append(",\"screenWidth\":").Append(hello.ScreenWidth.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"screenHeight\":").Append(hello.ScreenHeight.ToString(CultureInfo.InvariantCulture));
            sb.Append("}");
            return;
        }

        if (obj is VisionCapabilitiesDto cap)
        {
            sb.Append("{");
            sb.Append("\"ugui\":").Append(cap.Ugui ? "true" : "false");
            sb.Append(",\"eventSystem\":").Append(cap.EventSystem ? "true" : "false");
            sb.Append(",\"uiToolkit\":").Append(cap.UiToolkit ? "true" : "false");
            sb.Append(",\"legacyInput\":").Append(cap.LegacyInput ? "true" : "false");
            sb.Append(",\"inputSystem\":").Append(cap.InputSystem ? "true" : "false");
            sb.Append(",\"screenshot\":").Append(cap.Screenshot ? "true" : "false");
            sb.Append(",\"screenshotMethod\":"); WriteString(sb, cap.ScreenshotMethod);
            sb.Append(",\"screenshotLimitations\":"); WriteString(sb, cap.ScreenshotLimitations);
            sb.Append(",\"inputClickMethod\":"); WriteString(sb, cap.InputClickMethod);
            sb.Append(",\"inputClickStatus\":"); WriteString(sb, cap.InputClickStatus);
            sb.Append(",\"untestedMethods\":"); WriteString(sb, cap.UntestedMethods);
            sb.Append("}");
            return;
        }

        if (obj is VisionSceneInfoDto scene)
        {
            sb.Append("{");
            sb.Append("\"activeSceneName\":"); WriteString(sb, scene.ActiveSceneName);
            sb.Append(",\"activeSceneIndex\":").Append(scene.ActiveSceneIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"rootCount\":").Append(scene.RootCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"isLoaded\":").Append(scene.IsLoaded ? "true" : "false");
            sb.Append(",\"loadedSceneCount\":").Append(scene.LoadedSceneCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"screenWidth\":").Append(scene.ScreenWidth.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"screenHeight\":").Append(scene.ScreenHeight.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"activeRendererCount\":").Append(scene.ActiveRendererCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"timeSinceLevelLoad\":").Append(scene.TimeSinceLevelLoad.ToString("R", CultureInfo.InvariantCulture));
            sb.Append("}");
            return;
        }

        if (obj is VisionRectDto rect)
        {
            sb.Append("{");
            sb.Append("\"x\":").Append(rect.X.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"y\":").Append(rect.Y.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"width\":").Append(rect.Width.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"height\":").Append(rect.Height.ToString("R", CultureInfo.InvariantCulture));
            sb.Append("}");
            return;
        }

        if (obj is VisionUiElementDto elem)
        {
            sb.Append("{");
            sb.Append("\"id\":"); WriteString(sb, elem.Id);
            sb.Append(",\"type\":"); WriteString(sb, elem.Type);
            sb.Append(",\"text\":"); WriteString(sb, elem.Text);
            sb.Append(",\"pixelRect\":"); WriteValue(sb, elem.PixelRect);
            sb.Append(",\"normalizedRect\":"); WriteValue(sb, elem.NormalizedRect);
            sb.Append(",\"interactable\":").Append(elem.Interactable ? "true" : "false");
            sb.Append(",\"visible\":").Append(elem.Visible ? "true" : "false");
            sb.Append("}");
            return;
        }

        if (obj is VisionUiElementsResponse elementsResp)
        {
            sb.Append("{");
            sb.Append("\"count\":").Append(elementsResp.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"elements\":"); WriteValue(sb, elementsResp.Elements);
            sb.Append("}");
            return;
        }

        if (obj is VisionClickRequest clickReq)
        {
            sb.Append("{");
            sb.Append("\"id\":"); WriteValue(sb, clickReq.Id);
            sb.Append(",\"x\":"); WriteValue(sb, clickReq.X);
            sb.Append(",\"y\":"); WriteValue(sb, clickReq.Y);
            sb.Append(",\"button\":").Append(clickReq.Button.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"method\":"); WriteValue(sb, clickReq.Method);
            sb.Append(",\"action\":"); WriteValue(sb, clickReq.Action);
            sb.Append(",\"holdFrames\":").Append(clickReq.HoldFrames.ToString(CultureInfo.InvariantCulture));
            sb.Append("}");
            return;
        }

        if (obj is VisionClickResponse clickResp)
        {
            sb.Append("{");
            sb.Append("\"success\":").Append(clickResp.Success ? "true" : "false");
            sb.Append(",\"hitElementId\":"); WriteValue(sb, clickResp.HitElementId);
            sb.Append(",\"hitElementType\":"); WriteValue(sb, clickResp.HitElementType);
            sb.Append(",\"hitPointX\":").Append(clickResp.HitPointX.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"hitPointY\":").Append(clickResp.HitPointY.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"message\":"); WriteString(sb, clickResp.Message);
            sb.Append("}");
            return;
        }

        if (obj is VisionWaitIdleResponse idleResp)
        {
            sb.Append("{");
            sb.Append("\"idleReached\":").Append(idleResp.IdleReached ? "true" : "false");
            sb.Append(",\"elapsedFrames\":").Append(idleResp.ElapsedFrames.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"elapsedMs\":").Append(idleResp.ElapsedMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"reason\":"); WriteString(sb, idleResp.Reason);
            sb.Append("}");
            return;
        }

        if (obj is VisionResizeRequest resizeReq)
        {
            sb.Append("{");
            sb.Append("\"width\":").Append(resizeReq.Width.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"height\":").Append(resizeReq.Height.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"fullscreen\":").Append(resizeReq.Fullscreen ? "true" : "false");
            sb.Append("}");
            return;
        }

        if (obj is VisionResizeResponse resizeResp)
        {
            sb.Append("{");
            sb.Append("\"success\":").Append(resizeResp.Success ? "true" : "false");
            sb.Append(",\"screenWidth\":").Append(resizeResp.ScreenWidth.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"screenHeight\":").Append(resizeResp.ScreenHeight.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"message\":"); WriteString(sb, resizeResp.Message);
            sb.Append("}");
            return;
        }

        if (obj is VisionErrorResponse err)
        {
            sb.Append("{");
            sb.Append("\"code\":"); WriteString(sb, err.Code);
            sb.Append(",\"message\":"); WriteString(sb, err.Message);
            sb.Append(",\"suggestion\":"); WriteString(sb, err.Suggestion);
            sb.Append("}");
            return;
        }

        if (obj is VisionSceneObjectsResponse sor)
        {
            sb.Append("{");
            sb.Append("\"success\":").Append(sor.Success ? "true" : "false");
            sb.Append(",\"totalCameras\":").Append(sor.TotalCameras.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"totalRenderers\":").Append(sor.TotalRenderers.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"totalMatched\":").Append(sor.TotalMatched.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"truncated\":").Append(sor.Truncated ? "true" : "false");
            sb.Append(",\"message\":"); WriteString(sb, sor.Message);
            sb.Append(",\"cameras\":"); WriteValue(sb, sor.Cameras);
            sb.Append(",\"renderers\":"); WriteValue(sb, sor.Renderers);
            sb.Append("}");
            return;
        }

        if (obj is VisionSceneCameraDto cam)
        {
            sb.Append("{");
            sb.Append("\"name\":"); WriteString(sb, cam.Name);
            sb.Append(",\"position\":"); WriteValue(sb, cam.Position);
            sb.Append(",\"orthographic\":").Append(cam.Orthographic ? "true" : "false");
            sb.Append(",\"orthographicSize\":").Append(cam.OrthographicSize.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"cullingMask\":").Append(cam.CullingMask.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"depth\":").Append(cam.Depth.ToString("R", CultureInfo.InvariantCulture));
            sb.Append("}");
            return;
        }

        if (obj is VisionSceneRendererDto ren)
        {
            sb.Append("{");
            sb.Append("\"name\":"); WriteString(sb, ren.Name);
            sb.Append(",\"parentName\":"); WriteString(sb, ren.ParentName);
            sb.Append(",\"fullPath\":"); WriteString(sb, ren.FullPath);
            sb.Append(",\"activeSelf\":").Append(ren.ActiveSelf ? "true" : "false");
            sb.Append(",\"activeInHierarchy\":").Append(ren.ActiveInHierarchy ? "true" : "false");
            sb.Append(",\"rendererType\":"); WriteString(sb, ren.RendererType);
            sb.Append(",\"sortingLayerName\":"); WriteString(sb, ren.SortingLayerName);
            sb.Append(",\"sortingLayerId\":").Append(ren.SortingLayerId.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"sortingOrder\":").Append(ren.SortingOrder.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"localPosition\":"); WriteValue(sb, ren.LocalPosition);
            sb.Append(",\"localScale\":"); WriteValue(sb, ren.LocalScale);
            sb.Append(",\"center\":"); WriteValue(sb, ren.Center);
            sb.Append(",\"extents\":"); WriteValue(sb, ren.Extents);
            sb.Append(",\"spriteName\":"); WriteString(sb, ren.SpriteName);
            sb.Append(",\"spritePivot\":"); WriteValue(sb, ren.SpritePivot);
            sb.Append(",\"components\":"); WriteValue(sb, ren.Components);
            sb.Append(",\"materials\":"); WriteValue(sb, ren.Materials);
            sb.Append(",\"isVisible\":").Append(ren.IsVisible ? "true" : "false");
            sb.Append("}");
            return;
        }

        if (obj is VisionSceneMaterialDto mat)
        {
            sb.Append("{");
            sb.Append("\"materialName\":"); WriteString(sb, mat.MaterialName);
            sb.Append(",\"shaderName\":"); WriteString(sb, mat.ShaderName);
            sb.Append("}");
            return;
        }

        if (obj is VisionRuntimeAssetsResponse rar)
        {
            sb.Append("{");
            sb.Append("\"success\":").Append(rar.Success ? "true" : "false");
            sb.Append(",\"totalBundles\":").Append(rar.TotalBundles.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"totalLoadedAssets\":").Append(rar.TotalLoadedAssets.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"truncated\":").Append(rar.Truncated ? "true" : "false");
            sb.Append(",\"message\":"); WriteString(sb, rar.Message);
            sb.Append(",\"bundles\":"); WriteValue(sb, rar.Bundles);
            sb.Append(",\"loadedAssets\":"); WriteValue(sb, rar.LoadedAssets);
            sb.Append("}");
            return;
        }

        if (obj is VisionLoadedBundleDto bundle)
        {
            sb.Append("{");
            sb.Append("\"name\":"); WriteString(sb, bundle.Name);
            sb.Append(",\"isStreamedScene\":").Append(bundle.IsStreamedScene ? "true" : "false");
            sb.Append(",\"assetCount\":").Append(bundle.AssetCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"matchingAssetPaths\":"); WriteValue(sb, bundle.MatchingAssetPaths);
            sb.Append("}");
            return;
        }

        if (obj is VisionLoadedAssetDto loadedAsset)
        {
            sb.Append("{");
            sb.Append("\"name\":"); WriteString(sb, loadedAsset.Name);
            sb.Append(",\"assetType\":"); WriteString(sb, loadedAsset.AssetType);
            sb.Append(",\"instanceId\":").Append(loadedAsset.InstanceId.ToString(CultureInfo.InvariantCulture));
            sb.Append("}");
            return;
        }

        if (obj is IEnumerable enumerable)
        {
            sb.Append("[");
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first) sb.Append(",");
                WriteValue(sb, item);
                first = false;
            }
            sb.Append("]");
            return;
        }

        WriteString(sb, obj.ToString() ?? "");
    }

    private static void WriteString(StringBuilder sb, string? str)
    {
        if (str == null)
        {
            sb.Append("null");
            return;
        }

        sb.Append("\"");
        foreach (char c in str)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        sb.Append("\"");
    }

    public static JsonValue Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return JsonValue.Null;
        int index = 0;
        return ParseValue(json, ref index);
    }

    private static JsonValue ParseValue(string json, ref int index)
    {
        SkipWhitespace(json, ref index);
        if (index >= json.Length) return JsonValue.Null;

        char c = json[index];
        if (c == '{') return ParseObject(json, ref index);
        if (c == '[') return ParseArray(json, ref index);
        if (c == '"') return JsonValue.From(ParseString(json, ref index));
        if (char.IsDigit(c) || c == '-') return ParseNumber(json, ref index);
        if (c == 't' || c == 'T' || c == 'f' || c == 'F') return ParseBoolean(json, ref index);
        if (c == 'n' || c == 'N') return ParseNull(json, ref index);

        return JsonValue.Null;
    }

    private static JsonValue ParseObject(string json, ref int index)
    {
        index++; // skip '{'
        var dict = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase);

        while (index < json.Length)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) break;
            if (json[index] == '}')
            {
                index++;
                break;
            }

            string key = ParseString(json, ref index);
            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ':') index++; // skip ':'

            JsonValue val = ParseValue(json, ref index);
            dict[key] = val;

            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ',')
            {
                index++; // skip ','
            }
        }

        return JsonValue.From(dict);
    }

    private static JsonValue ParseArray(string json, ref int index)
    {
        index++; // skip '['
        var list = new List<JsonValue>();

        while (index < json.Length)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) break;
            if (json[index] == ']')
            {
                index++;
                break;
            }

            JsonValue val = ParseValue(json, ref index);
            list.Add(val);

            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ',')
            {
                index++; // skip ','
            }
        }

        return JsonValue.From(list);
    }

    private static string ParseString(string json, ref int index)
    {
        if (index >= json.Length || json[index] != '"') return string.Empty;
        index++; // skip opening '"'

        var sb = new StringBuilder();
        while (index < json.Length)
        {
            char c = json[index++];
            if (c == '"') break;
            if (c == '\\' && index < json.Length)
            {
                char esc = json[index++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 <= json.Length)
                        {
                            string hex = json.Substring(index, 4);
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int unicode))
                            {
                                sb.Append((char)unicode);
                            }
                            index += 4;
                        }
                        break;
                    default:
                        sb.Append(esc);
                        break;
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static JsonValue ParseNumber(string json, ref int index)
    {
        int start = index;
        if (index < json.Length && json[index] == '-') index++;
        while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '.' || json[index] == 'e' || json[index] == 'E' || json[index] == '+' || json[index] == '-'))
        {
            index++;
        }

        string numStr = json.Substring(start, index - start);
        if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double val))
        {
            return JsonValue.From(val);
        }

        return JsonValue.From(0.0);
    }

    private static JsonValue ParseBoolean(string json, ref int index)
    {
        if (index + 4 <= json.Length && json.Substring(index, 4).Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            index += 4;
            return JsonValue.From(true);
        }
        if (index + 5 <= json.Length && json.Substring(index, 5).Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            index += 5;
            return JsonValue.From(false);
        }
        return JsonValue.From(false);
    }

    private static JsonValue ParseNull(string json, ref int index)
    {
        if (index + 4 <= json.Length && json.Substring(index, 4).Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            index += 4;
        }
        return JsonValue.Null;
    }

    private static void SkipWhitespace(string json, ref int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
        {
            index++;
        }
    }

    // Typed Deserializer Helpers
    public static VisionHelloDto DeserializeHello(string json)
    {
        var root = Parse(json);
        return new VisionHelloDto
        {
            ProtocolVersion = root["protocolVersion"].AsString(VisionConstants.ProtocolVersion),
            PluginVersion = root["pluginVersion"].AsString(VisionConstants.PluginVersion),
            UnityVersion = root["unityVersion"].AsString(),
            ScreenWidth = root["screenWidth"].AsInt(),
            ScreenHeight = root["screenHeight"].AsInt()
        };
    }

    public static VisionCapabilitiesDto DeserializeCapabilities(string json)
    {
        var root = Parse(json);
        return new VisionCapabilitiesDto
        {
            Ugui = root["ugui"].AsBoolean(),
            EventSystem = root["eventSystem"].AsBoolean(),
            UiToolkit = root["uiToolkit"].AsBoolean(),
            LegacyInput = root["legacyInput"].AsBoolean(),
            InputSystem = root["inputSystem"].AsBoolean(),
            Screenshot = root["screenshot"].AsBoolean(),
            ScreenshotMethod = root["screenshotMethod"].AsString() ?? string.Empty,
            ScreenshotLimitations = root["screenshotLimitations"].AsString() ?? string.Empty,
            InputClickMethod = root["inputClickMethod"].AsString() ?? string.Empty,
            InputClickStatus = root["inputClickStatus"].AsString() ?? string.Empty,
            UntestedMethods = root["untestedMethods"].AsString() ?? string.Empty
        };
    }

    public static VisionSceneInfoDto DeserializeSceneInfo(string json)
    {
        var root = Parse(json);
        return new VisionSceneInfoDto
        {
            ActiveSceneName = root["activeSceneName"].AsString("Unknown"),
            ActiveSceneIndex = root["activeSceneIndex"].AsInt(-1),
            RootCount = root["rootCount"].AsInt(0),
            IsLoaded = root["isLoaded"].AsBoolean(true),
            LoadedSceneCount = root["loadedSceneCount"].AsInt(1),
            ScreenWidth = root["screenWidth"].AsInt(0),
            ScreenHeight = root["screenHeight"].AsInt(0),
            ActiveRendererCount = root["activeRendererCount"].AsInt(0),
            TimeSinceLevelLoad = root["timeSinceLevelLoad"].AsFloat(0f)
        };
    }

    public static VisionRuntimeAssetsResponse DeserializeRuntimeAssetsResponse(string json)
    {
        var root = Parse(json);
        var resp = new VisionRuntimeAssetsResponse
        {
            Success = root["success"].AsBoolean(true),
            TotalBundles = root["totalBundles"].AsInt(0),
            TotalLoadedAssets = root["totalLoadedAssets"].AsInt(0),
            Truncated = root["truncated"].AsBoolean(false),
            Message = root["message"].AsString()
        };

        foreach (var item in root["bundles"].AsArray())
        {
            var b = new VisionLoadedBundleDto
            {
                Name = item["name"].AsString(),
                IsStreamedScene = item["isStreamedScene"].AsBoolean(false),
                AssetCount = item["assetCount"].AsInt(0)
            };
            foreach (var pathVal in item["matchingAssetPaths"].AsArray())
            {
                b.MatchingAssetPaths.Add(pathVal.AsString());
            }
            resp.Bundles.Add(b);
        }

        foreach (var item in root["loadedAssets"].AsArray())
        {
            resp.LoadedAssets.Add(new VisionLoadedAssetDto
            {
                Name = item["name"].AsString(),
                AssetType = item["assetType"].AsString(),
                InstanceId = item["instanceId"].AsInt(0)
            });
        }

        return resp;
    }

    public static VisionUiElementsResponse DeserializeUiElements(string json)
    {
        var root = Parse(json);
        var resp = new VisionUiElementsResponse
        {
            Count = root["count"].AsInt()
        };

        var arr = root["elements"].AsArray();
        foreach (var item in arr)
        {
            var elem = new VisionUiElementDto
            {
                Id = item["id"].AsString(),
                Type = item["type"].AsString(),
                Text = item["text"].AsString(),
                Interactable = item["interactable"].AsBoolean(),
                Visible = item["visible"].AsBoolean(),
                PixelRect = ParseRect(item["pixelRect"]),
                NormalizedRect = ParseRect(item["normalizedRect"])
            };
            resp.Elements.Add(elem);
        }

        if (resp.Count == 0) resp.Count = resp.Elements.Count;
        return resp;
    }

    public static VisionClickRequest DeserializeClickRequest(string json)
    {
        var root = Parse(json);
        var req = new VisionClickRequest
        {
            Button = root["button"].AsInt(0)
        };

        var idVal = root["id"];
        if (idVal.Type == JsonType.String) req.Id = idVal.AsString();

        var xVal = root["x"];
        if (xVal.Type == JsonType.Number) req.X = xVal.AsFloat();

        var yVal = root["y"];
        if (yVal.Type == JsonType.Number) req.Y = yVal.AsFloat();

        var methodVal = root["method"];
        if (methodVal.Type == JsonType.String) req.Method = methodVal.AsString();

        var actionVal = root["action"];
        if (actionVal.Type == JsonType.String) req.Action = actionVal.AsString();

        var holdVal = root["holdFrames"];
        if (holdVal.Type == JsonType.Number) req.HoldFrames = holdVal.AsInt(5);

        return req;
    }

    public static VisionClickResponse DeserializeClickResponse(string json)
    {
        var root = Parse(json);
        return new VisionClickResponse
        {
            Success = root["success"].AsBoolean(),
            HitElementId = root["hitElementId"].Type != JsonType.Null ? root["hitElementId"].AsString() : null,
            HitElementType = root["hitElementType"].Type != JsonType.Null ? root["hitElementType"].AsString() : null,
            HitPointX = root["hitPointX"].AsFloat(),
            HitPointY = root["hitPointY"].AsFloat(),
            Message = root["message"].AsString()
        };
    }

    public static VisionWaitIdleResponse DeserializeWaitIdleResponse(string json)
    {
        var root = Parse(json);
        return new VisionWaitIdleResponse
        {
            IdleReached = root["idleReached"].AsBoolean(),
            ElapsedFrames = root["elapsedFrames"].AsInt(),
            ElapsedMs = root["elapsedMs"].AsLong(),
            Reason = root["reason"].AsString()
        };
    }

    public static VisionResizeRequest DeserializeResizeRequest(string json)
    {
        var root = Parse(json);
        return new VisionResizeRequest
        {
            Width = root["width"].AsInt(),
            Height = root["height"].AsInt(),
            Fullscreen = root["fullscreen"].AsBoolean(false)
        };
    }

    public static VisionResizeResponse DeserializeResizeResponse(string json)
    {
        var root = Parse(json);
        return new VisionResizeResponse
        {
            Success = root["success"].AsBoolean(),
            ScreenWidth = root["screenWidth"].AsInt(),
            ScreenHeight = root["screenHeight"].AsInt(),
            Message = root["message"].AsString()
        };
    }

    public static VisionErrorResponse DeserializeErrorResponse(string json)
    {
        var root = Parse(json);
        return new VisionErrorResponse
        {
            Code = root["code"].AsString("UNKNOWN_ERROR"),
            Message = root["message"].AsString(),
            Suggestion = root["suggestion"].AsString()
        };
    }

    private static VisionRectDto ParseRect(JsonValue val)
    {
        if (val.Type != JsonType.Object) return new VisionRectDto();
        return new VisionRectDto(
            val["x"].AsFloat(),
            val["y"].AsFloat(),
            val["width"].AsFloat(),
            val["height"].AsFloat());
    }
}
