using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Candied.Vision.Protocol;

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
    public int AsInt(int defaultValue = 0) => Value is double d ? (int)d : (int.TryParse(AsString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue);
    public long AsLong(long defaultValue = 0) => Value is double d ? (long)d : (long.TryParse(AsString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue);
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

        if (obj is IDictionary<string, string> strDict)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kvp in strDict)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                WriteString(sb, kvp.Key);
                sb.Append(':');
                WriteString(sb, kvp.Value);
                first = false;
            }

            sb.Append('}');
            return;
        }

        if (obj is VisionHelloDto hello)
        {
            sb.Append('{');
            sb.Append("\"protocolVersion\":"); WriteString(sb, hello.ProtocolVersion);
            sb.Append(",\"sensorVersion\":"); WriteString(sb, hello.SensorVersion);
            sb.Append(",\"engineName\":"); WriteString(sb, hello.EngineName);
            sb.Append(",\"engineVariant\":"); WriteString(sb, hello.EngineVariant);
            sb.Append(",\"engineVersion\":"); WriteString(sb, hello.EngineVersion);
            sb.Append(",\"screenWidth\":").Append(hello.ScreenWidth.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"screenHeight\":").Append(hello.ScreenHeight.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
            return;
        }

        if (obj is VisionCapabilitiesDto cap)
        {
            sb.Append('{');
            sb.Append("\"capabilities\":"); WriteValue(sb, cap.Capabilities);
            sb.Append(",\"screenshotMethod\":"); WriteString(sb, cap.ScreenshotMethod);
            sb.Append(",\"screenshotLimitations\":"); WriteString(sb, cap.ScreenshotLimitations);
            sb.Append(",\"pointerMethod\":"); WriteString(sb, cap.PointerMethod);
            sb.Append(",\"metadata\":"); WriteValue(sb, cap.Metadata);
            sb.Append('}');
            return;
        }

        if (obj is VisionSceneInfoDto scene)
        {
            sb.Append('{');
            sb.Append("\"activeSceneName\":"); WriteString(sb, scene.ActiveSceneName);
            sb.Append(",\"activeSceneIndex\":").Append(scene.ActiveSceneIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"rootCount\":").Append(scene.RootCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"isLoaded\":").Append(scene.IsLoaded ? "true" : "false");
            sb.Append(",\"loadedSceneCount\":").Append(scene.LoadedSceneCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"screenWidth\":").Append(scene.ScreenWidth.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"screenHeight\":").Append(scene.ScreenHeight.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"activeNodeCount\":").Append(scene.ActiveNodeCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"elapsedSeconds\":").Append(scene.ElapsedSeconds.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('}');
            return;
        }

        if (obj is VisionRectDto rect)
        {
            sb.Append('{');
            sb.Append("\"x\":").Append(rect.X.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"y\":").Append(rect.Y.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"width\":").Append(rect.Width.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"height\":").Append(rect.Height.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('}');
            return;
        }

        if (obj is VisionUiElementDto elem)
        {
            sb.Append('{');
            sb.Append("\"id\":"); WriteString(sb, elem.Id);
            sb.Append(",\"type\":"); WriteString(sb, elem.Type);
            sb.Append(",\"text\":"); WriteString(sb, elem.Text);
            sb.Append(",\"pixelRect\":"); WriteValue(sb, elem.PixelRect);
            sb.Append(",\"normalizedRect\":"); WriteValue(sb, elem.NormalizedRect);
            sb.Append(",\"interactable\":").Append(elem.Interactable ? "true" : "false");
            sb.Append(",\"visible\":").Append(elem.Visible ? "true" : "false");
            sb.Append('}');
            return;
        }

        if (obj is VisionUiElementsResponse elementsResp)
        {
            sb.Append('{');
            sb.Append("\"count\":").Append(elementsResp.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"elements\":"); WriteValue(sb, elementsResp.Elements);
            sb.Append('}');
            return;
        }

        if (obj is VisionInputRequest inputReq)
        {
            sb.Append('{');
            sb.Append("\"channel\":"); WriteString(sb, inputReq.Channel);
            sb.Append(",\"action\":"); WriteString(sb, inputReq.Action);
            sb.Append(",\"targetElementId\":"); WriteValue(sb, inputReq.TargetElementId);
            sb.Append(",\"x\":"); WriteValue(sb, inputReq.X);
            sb.Append(",\"y\":"); WriteValue(sb, inputReq.Y);
            sb.Append(",\"button\":").Append(inputReq.Button.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"method\":"); WriteValue(sb, inputReq.Method);
            sb.Append(",\"key\":"); WriteValue(sb, inputReq.Key);
            sb.Append(",\"modifiers\":"); WriteValue(sb, inputReq.Modifiers);
            sb.Append(",\"gamepadButton\":"); WriteValue(sb, inputReq.GamepadButton);
            sb.Append(",\"stick\":"); WriteValue(sb, inputReq.Stick);
            sb.Append(",\"stickX\":"); WriteValue(sb, inputReq.StickX);
            sb.Append(",\"stickY\":"); WriteValue(sb, inputReq.StickY);
            sb.Append(",\"holdFrames\":").Append(inputReq.HoldFrames.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
            return;
        }

        if (obj is VisionInputResponse inputResp)
        {
            sb.Append('{');
            sb.Append("\"success\":").Append(inputResp.Success ? "true" : "false");
            sb.Append(",\"channel\":"); WriteString(sb, inputResp.Channel);
            sb.Append(",\"action\":"); WriteString(sb, inputResp.Action);
            sb.Append(",\"hitElementId\":"); WriteValue(sb, inputResp.HitElementId);
            sb.Append(",\"hitElementType\":"); WriteValue(sb, inputResp.HitElementType);
            sb.Append(",\"hitPointX\":").Append(inputResp.HitPointX.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"hitPointY\":").Append(inputResp.HitPointY.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"message\":"); WriteString(sb, inputResp.Message);
            sb.Append('}');
            return;
        }

        if (obj is VisionWaitIdleResponse idleResp)
        {
            sb.Append('{');
            sb.Append("\"idleReached\":").Append(idleResp.IdleReached ? "true" : "false");
            sb.Append(",\"elapsedFrames\":").Append(idleResp.ElapsedFrames.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"elapsedMs\":").Append(idleResp.ElapsedMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"reason\":"); WriteString(sb, idleResp.Reason);
            sb.Append('}');
            return;
        }

        if (obj is VisionResizeRequest resizeReq)
        {
            sb.Append('{');
            sb.Append("\"width\":").Append(resizeReq.Width.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"height\":").Append(resizeReq.Height.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"fullscreen\":").Append(resizeReq.Fullscreen ? "true" : "false");
            sb.Append('}');
            return;
        }

        if (obj is VisionResizeResponse resizeResp)
        {
            sb.Append('{');
            sb.Append("\"success\":").Append(resizeResp.Success ? "true" : "false");
            sb.Append(",\"screenWidth\":").Append(resizeResp.ScreenWidth.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"screenHeight\":").Append(resizeResp.ScreenHeight.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"message\":"); WriteString(sb, resizeResp.Message);
            sb.Append('}');
            return;
        }

        if (obj is VisionErrorResponse err)
        {
            sb.Append('{');
            sb.Append("\"code\":"); WriteString(sb, err.Code);
            sb.Append(",\"message\":"); WriteString(sb, err.Message);
            sb.Append(",\"suggestion\":"); WriteString(sb, err.Suggestion);
            sb.Append('}');
            return;
        }

        if (obj is VisionSceneGraphResponse sgr)
        {
            sb.Append('{');
            sb.Append("\"success\":").Append(sgr.Success ? "true" : "false");
            sb.Append(",\"totalCameras\":").Append(sgr.TotalCameras.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"totalNodes\":").Append(sgr.TotalNodes.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"totalMatched\":").Append(sgr.TotalMatched.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"truncated\":").Append(sgr.Truncated ? "true" : "false");
            sb.Append(",\"message\":"); WriteString(sb, sgr.Message);
            sb.Append(",\"cameras\":"); WriteValue(sb, sgr.Cameras);
            sb.Append(",\"nodes\":"); WriteValue(sb, sgr.Nodes);
            sb.Append('}');
            return;
        }

        if (obj is VisionSceneCameraDto cam)
        {
            sb.Append('{');
            sb.Append("\"name\":"); WriteString(sb, cam.Name);
            sb.Append(",\"position\":"); WriteValue(sb, cam.Position);
            sb.Append(",\"orthographic\":").Append(cam.Orthographic ? "true" : "false");
            sb.Append(",\"orthographicSize\":").Append(cam.OrthographicSize.ToString("R", CultureInfo.InvariantCulture));
            sb.Append(",\"cullingMask\":").Append(cam.CullingMask.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"depth\":").Append(cam.Depth.ToString("R", CultureInfo.InvariantCulture));
            sb.Append('}');
            return;
        }

        if (obj is VisionSceneNodeDto node)
        {
            sb.Append('{');
            sb.Append("\"id\":"); WriteString(sb, node.Id);
            sb.Append(",\"name\":"); WriteString(sb, node.Name);
            sb.Append(",\"parentName\":"); WriteString(sb, node.ParentName);
            sb.Append(",\"fullPath\":"); WriteString(sb, node.FullPath);
            sb.Append(",\"nodeType\":"); WriteString(sb, node.NodeType);
            sb.Append(",\"activeSelf\":").Append(node.ActiveSelf ? "true" : "false");
            sb.Append(",\"activeInHierarchy\":").Append(node.ActiveInHierarchy ? "true" : "false");
            sb.Append(",\"isVisible\":").Append(node.IsVisible ? "true" : "false");
            sb.Append(",\"localPosition\":"); WriteValue(sb, node.LocalPosition);
            sb.Append(",\"localScale\":"); WriteValue(sb, node.LocalScale);
            sb.Append(",\"boundsCenter\":"); WriteValue(sb, node.BoundsCenter);
            sb.Append(",\"boundsExtents\":"); WriteValue(sb, node.BoundsExtents);
            sb.Append(",\"components\":"); WriteValue(sb, node.Components);
            sb.Append(",\"attributes\":"); WriteValue(sb, node.Attributes);
            sb.Append('}');
            return;
        }

        if (obj is VisionRuntimeAssetsResponse rar)
        {
            sb.Append('{');
            sb.Append("\"success\":").Append(rar.Success ? "true" : "false");
            sb.Append(",\"totalContainers\":").Append(rar.TotalContainers.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"totalLoadedAssets\":").Append(rar.TotalLoadedAssets.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"truncated\":").Append(rar.Truncated ? "true" : "false");
            sb.Append(",\"message\":"); WriteString(sb, rar.Message);
            sb.Append(",\"containers\":"); WriteValue(sb, rar.Containers);
            sb.Append(",\"loadedAssets\":"); WriteValue(sb, rar.LoadedAssets);
            sb.Append('}');
            return;
        }

        if (obj is VisionLoadedContainerDto container)
        {
            sb.Append('{');
            sb.Append("\"name\":"); WriteString(sb, container.Name);
            sb.Append(",\"isStreamedScene\":").Append(container.IsStreamedScene ? "true" : "false");
            sb.Append(",\"assetCount\":").Append(container.AssetCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"matchingAssetPaths\":"); WriteValue(sb, container.MatchingAssetPaths);
            sb.Append('}');
            return;
        }

        if (obj is VisionLoadedAssetDto loadedAsset)
        {
            sb.Append('{');
            sb.Append("\"name\":"); WriteString(sb, loadedAsset.Name);
            sb.Append(",\"assetType\":"); WriteString(sb, loadedAsset.AssetType);
            sb.Append(",\"assetId\":"); WriteString(sb, loadedAsset.AssetId);
            sb.Append('}');
            return;
        }

        if (obj is IEnumerable enumerable)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in enumerable)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                WriteValue(sb, item);
                first = false;
            }

            sb.Append(']');
            return;
        }

        WriteString(sb, obj.ToString() ?? string.Empty);
    }

    private static void WriteString(StringBuilder sb, string? str)
    {
        if (str == null)
        {
            sb.Append("null");
            return;
        }

        sb.Append('"');
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

        sb.Append('"');
    }

    public static JsonValue Parse(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return JsonValue.Null;
        }

        int index = 0;
        return ParseValue(json, ref index);
    }

    private static JsonValue ParseValue(string json, ref int index)
    {
        SkipWhitespace(json, ref index);
        if (index >= json.Length)
        {
            return JsonValue.Null;
        }

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
        index++;
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
            if (index < json.Length && json[index] == ':') index++;

            JsonValue val = ParseValue(json, ref index);
            dict[key] = val;

            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ',')
            {
                index++;
            }
        }

        return JsonValue.From(dict);
    }

    private static JsonValue ParseArray(string json, ref int index)
    {
        index++;
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
                index++;
            }
        }

        return JsonValue.From(list);
    }

    private static string ParseString(string json, ref int index)
    {
        if (index >= json.Length || json[index] != '"')
        {
            return string.Empty;
        }

        index++;
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

    public static VisionHelloDto DeserializeHello(string json)
    {
        var root = Parse(json);
        return new VisionHelloDto
        {
            ProtocolVersion = root["protocolVersion"].AsString(VisionConstants.ProtocolVersion),
            SensorVersion = root["sensorVersion"].AsString(VisionConstants.SensorVersion),
            EngineName = root["engineName"].AsString(),
            EngineVariant = root["engineVariant"].AsString(),
            EngineVersion = root["engineVersion"].AsString(),
            ScreenWidth = root["screenWidth"].AsInt(),
            ScreenHeight = root["screenHeight"].AsInt()
        };
    }

    public static VisionCapabilitiesDto DeserializeCapabilities(string json)
    {
        var root = Parse(json);
        var dto = new VisionCapabilitiesDto
        {
            ScreenshotMethod = root["screenshotMethod"].AsString(),
            ScreenshotLimitations = root["screenshotLimitations"].AsString(),
            PointerMethod = root["pointerMethod"].AsString()
        };

        foreach (var c in root["capabilities"].AsArray())
        {
            string s = c.AsString();
            if (!string.IsNullOrEmpty(s))
            {
                dto.Capabilities.Add(s);
            }
        }

        foreach (var kvp in root["metadata"].AsObject())
        {
            dto.Metadata[kvp.Key] = kvp.Value.AsString();
        }

        return dto;
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
            ActiveNodeCount = root["activeNodeCount"].AsInt(0),
            ElapsedSeconds = root["elapsedSeconds"].AsFloat(0f)
        };
    }

    public static VisionSceneGraphResponse DeserializeSceneGraph(string json)
    {
        var root = Parse(json);
        var resp = new VisionSceneGraphResponse
        {
            Success = root["success"].AsBoolean(true),
            TotalCameras = root["totalCameras"].AsInt(0),
            TotalNodes = root["totalNodes"].AsInt(0),
            TotalMatched = root["totalMatched"].AsInt(0),
            Truncated = root["truncated"].AsBoolean(false),
            Message = root["message"].AsString()
        };

        foreach (var item in root["cameras"].AsArray())
        {
            resp.Cameras.Add(new VisionSceneCameraDto
            {
                Name = item["name"].AsString(),
                Position = ParseFloatArray(item["position"], 3),
                Orthographic = item["orthographic"].AsBoolean(),
                OrthographicSize = item["orthographicSize"].AsFloat(),
                CullingMask = item["cullingMask"].AsInt(),
                Depth = item["depth"].AsFloat()
            });
        }

        foreach (var item in root["nodes"].AsArray())
        {
            var node = new VisionSceneNodeDto
            {
                Id = item["id"].AsString(),
                Name = item["name"].AsString(),
                ParentName = item["parentName"].Type != JsonType.Null ? item["parentName"].AsString() : null,
                FullPath = item["fullPath"].AsString(),
                NodeType = item["nodeType"].AsString(),
                ActiveSelf = item["activeSelf"].AsBoolean(true),
                ActiveInHierarchy = item["activeInHierarchy"].AsBoolean(true),
                IsVisible = item["isVisible"].AsBoolean(false),
                LocalPosition = ParseFloatArray(item["localPosition"], 3),
                LocalScale = ParseFloatArray(item["localScale"], 3),
                BoundsCenter = ParseFloatArray(item["boundsCenter"], 3),
                BoundsExtents = ParseFloatArray(item["boundsExtents"], 3)
            };

            foreach (var comp in item["components"].AsArray())
            {
                node.Components.Add(comp.AsString());
            }

            foreach (var attr in item["attributes"].AsObject())
            {
                node.Attributes[attr.Key] = attr.Value.AsString();
            }

            resp.Nodes.Add(node);
        }

        return resp;
    }

    public static VisionRuntimeAssetsResponse DeserializeRuntimeAssetsResponse(string json)
    {
        var root = Parse(json);
        var resp = new VisionRuntimeAssetsResponse
        {
            Success = root["success"].AsBoolean(true),
            TotalContainers = root["totalContainers"].AsInt(0),
            TotalLoadedAssets = root["totalLoadedAssets"].AsInt(0),
            Truncated = root["truncated"].AsBoolean(false),
            Message = root["message"].AsString()
        };

        foreach (var item in root["containers"].AsArray())
        {
            var c = new VisionLoadedContainerDto
            {
                Name = item["name"].AsString(),
                IsStreamedScene = item["isStreamedScene"].AsBoolean(false),
                AssetCount = item["assetCount"].AsInt(0)
            };
            foreach (var pathVal in item["matchingAssetPaths"].AsArray())
            {
                c.MatchingAssetPaths.Add(pathVal.AsString());
            }

            resp.Containers.Add(c);
        }

        foreach (var item in root["loadedAssets"].AsArray())
        {
            resp.LoadedAssets.Add(new VisionLoadedAssetDto
            {
                Name = item["name"].AsString(),
                AssetType = item["assetType"].AsString(),
                AssetId = item["assetId"].AsString()
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

        foreach (var item in root["elements"].AsArray())
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

        if (resp.Count == 0)
        {
            resp.Count = resp.Elements.Count;
        }

        return resp;
    }

    public static VisionInputRequest DeserializeInputRequest(string json)
    {
        var root = Parse(json);
        var req = new VisionInputRequest
        {
            Channel = root["channel"].AsString(VisionConstants.ChannelPointer),
            Action = root["action"].AsString("click"),
            Button = root["button"].AsInt(0),
            HoldFrames = root["holdFrames"].AsInt(6)
        };

        if (root["targetElementId"].Type == JsonType.String) req.TargetElementId = root["targetElementId"].AsString();
        if (root["x"].Type == JsonType.Number) req.X = root["x"].AsFloat();
        if (root["y"].Type == JsonType.Number) req.Y = root["y"].AsFloat();
        if (root["method"].Type == JsonType.String) req.Method = root["method"].AsString();
        if (root["key"].Type == JsonType.String) req.Key = root["key"].AsString();
        if (root["gamepadButton"].Type == JsonType.String) req.GamepadButton = root["gamepadButton"].AsString();
        if (root["stick"].Type == JsonType.String) req.Stick = root["stick"].AsString();
        if (root["stickX"].Type == JsonType.Number) req.StickX = root["stickX"].AsFloat();
        if (root["stickY"].Type == JsonType.Number) req.StickY = root["stickY"].AsFloat();

        foreach (var mod in root["modifiers"].AsArray())
        {
            string s = mod.AsString();
            if (!string.IsNullOrEmpty(s))
            {
                req.Modifiers.Add(s);
            }
        }

        return req;
    }

    public static VisionInputResponse DeserializeInputResponse(string json)
    {
        var root = Parse(json);
        return new VisionInputResponse
        {
            Success = root["success"].AsBoolean(),
            Channel = root["channel"].AsString(VisionConstants.ChannelPointer),
            Action = root["action"].AsString(),
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
            Message = root["message"].AsString("Unknown error occurred."),
            Suggestion = root["suggestion"].AsString()
        };
    }

    private static VisionRectDto ParseRect(JsonValue val)
    {
        if (val.Type != JsonType.Object)
        {
            return new VisionRectDto();
        }

        return new VisionRectDto(
            val["x"].AsFloat(),
            val["y"].AsFloat(),
            val["width"].AsFloat(),
            val["height"].AsFloat());
    }

    private static float[] ParseFloatArray(JsonValue val, int fallbackLength)
    {
        var arr = val.AsArray();
        if (arr.Count == 0)
        {
            return new float[fallbackLength];
        }

        var result = new float[arr.Count];
        for (int i = 0; i < arr.Count; i++)
        {
            result[i] = arr[i].AsFloat();
        }

        return result;
    }
}
