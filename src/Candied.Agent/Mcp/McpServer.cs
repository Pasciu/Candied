using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Candied.Core.Models;
using Candied.Vision.Controls;
using Candied.Vision.Protocol;

namespace Candied.Agent.Mcp;

internal sealed class McpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        WriteIndented = false
    };

    private readonly AgentWorkbenchService _workbench;

    public McpServer(AgentWorkbenchService workbench)
    {
        _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
    }

    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct = default)
    {
        string? line;
        var activeTasks = new List<Task>();
        using var writeLock = new SemaphoreSlim(1, 1);

        while (!ct.IsCancellationRequested && (line = await input.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string currentLine = line;
            var task = Task.Run(async () =>
            {
                string? responseJson = await HandleMessageAsync(currentLine, ct).ConfigureAwait(false);
                if (responseJson != null)
                {
                    await writeLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await output.WriteLineAsync(responseJson).ConfigureAwait(false);
                        await output.FlushAsync(ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        writeLock.Release();
                    }
                }
            }, ct);

            activeTasks.Add(task);
        }

        await Task.WhenAll(activeTasks).ConfigureAwait(false);
    }

    public async Task<string?> HandleMessageAsync(string requestJson, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return null;
        }

        requestJson = requestJson.TrimStart('\uFEFF').Trim();
        if (string.IsNullOrWhiteSpace(requestJson))
        {
            return null;
        }

        JsonRpcRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(requestJson, JsonOptions);
        }
        catch (JsonException)
        {
            var parseErrorResponse = JsonRpcResponse.CreateError(
                null,
                JsonRpcErrorCodes.ParseError,
                "Parse error: invalid JSON received.");
            return JsonSerializer.Serialize(parseErrorResponse, JsonOptions);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            var invalidRequest = JsonRpcResponse.CreateError(
                null,
                JsonRpcErrorCodes.InvalidRequest,
                "Invalid Request: missing method name.");
            return JsonSerializer.Serialize(invalidRequest, JsonOptions);
        }

        if (request.Method.Equals("notifications/initialized", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "ping" => HandlePing(request),
                "tools/list" => HandleToolsList(request),
                "tools/call" => await HandleToolsCallAsync(request, ct).ConfigureAwait(false),
                _ => HandleMethodNotFound(request)
            };
        }
        catch (Exception ex)
        {
            var internalError = JsonRpcResponse.CreateError(
                request.Id,
                JsonRpcErrorCodes.InternalError,
                $"Internal server error: {ex.Message}");
            return JsonSerializer.Serialize(internalError, JsonOptions);
        }
    }

    private string HandleInitialize(JsonRpcRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Candied is a multi-platform autonomous reverse-engineering, live vision, and modding workbench for PC and emulated games.");
        sb.AppendLine("Standard Modding & Live Perception Pipeline:");
        sb.AppendLine("1. Call 'inspect(targetPath, runnerPath?)' to detect platform capabilities, mod loader status, and code modules.");
        sb.AppendLine("2. If EnvironmentSetup is supported and modLoader.isInstalled is false, call 'setup()'.");
        sb.AppendLine("3. Call 'extract-code(moduleName)' on the primary CoreLogic module, then query symbols/code via 'query', 'search-code', and 'type'.");
        sb.AppendLine("4. Launch any game or emulator directly in Candied Studio via 'game-launch(targetPath, runnerPath?)' — Tier 1 BaseWindow screenshot & input work immediately; Tier 2 SensorBridge connects automatically when a sensor is present.");
        sb.AppendLine("5. Interact via 'screenshot', 'send-input' (pointer, keyboard, gamepad), or semantic 'trigger-action' / 'get-controls' / 'set-controls'.");
        sb.AppendLine("6. Call 'scaffold(modName)', author your mod code in 'mods/<ModName>/', compile via 'build(projectFilePath)', and verify via 'logs' and 'screenshot'.");

        foreach (var guide in _workbench.GetPlatformGuides())
        {
            sb.AppendLine();
            sb.AppendLine(guide.MarkdownContent);
        }

        var result = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "Candied-workbench",
                version = "2.0.0"
            },
            instructions = sb.ToString()
        };

        var response = JsonRpcResponse.Success(request.Id, result);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static string HandlePing(JsonRpcRequest request)
    {
        var response = JsonRpcResponse.Success(request.Id, new { });
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private string HandleToolsList(JsonRpcRequest request)
    {
        var tools = McpToolDefinitions.GetAllTools(_workbench.GetPlatformTools());
        var response = JsonRpcResponse.Success(request.Id, tools);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<string> HandleToolsCallAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (request.Params is null || request.Params.Value.ValueKind != JsonValueKind.Object)
        {
            var invalidParams = JsonRpcResponse.CreateError(
                request.Id,
                JsonRpcErrorCodes.InvalidParams,
                "Missing parameters object for tools/call.");
            return JsonSerializer.Serialize(invalidParams, JsonOptions);
        }

        var paramsObj = request.Params.Value;
        if (!paramsObj.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
        {
            var invalidParams = JsonRpcResponse.CreateError(
                request.Id,
                JsonRpcErrorCodes.InvalidParams,
                "Missing 'name' property in tools/call parameters.");
            return JsonSerializer.Serialize(invalidParams, JsonOptions);
        }

        string toolName = nameProp.GetString()!;
        JsonElement arguments = paramsObj.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.Object
            ? argsProp
            : default;

        string? toolOutputJson = await DispatchCoreToolAsync(toolName, arguments, ct).ConfigureAwait(false);
        if (toolOutputJson == null)
        {
            bool isPlatformTool = _workbench.GetPlatformTools()
                .Any(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

            if (!isPlatformTool)
            {
                var unknownTool = JsonRpcResponse.CreateError(
                    request.Id,
                    JsonRpcErrorCodes.MethodNotFound,
                    $"Unknown tool name: '{toolName}'.");
                return JsonSerializer.Serialize(unknownTool, JsonOptions);
            }

            var dictArgs = ExtractStringDictionary(arguments);
            toolOutputJson = await _workbench.ExecutePlatformToolAsync(toolName, dictArgs, ct).ConfigureAwait(false);
        }

        bool isError = false;
        try
        {
            using var doc = JsonDocument.Parse(toolOutputJson);
            if (doc.RootElement.TryGetProperty("success", out var successProp) &&
                (successProp.ValueKind == JsonValueKind.True || successProp.ValueKind == JsonValueKind.False))
            {
                isError = !successProp.GetBoolean();
            }
        }
        catch (JsonException)
        {
        }

        var mcpResult = new
        {
            content = new object[]
            {
                new
                {
                    type = "text",
                    text = toolOutputJson
                }
            },
            isError
        };

        var response = JsonRpcResponse.Success(request.Id, mcpResult);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private async Task<string?> DispatchCoreToolAsync(string toolName, JsonElement arguments, CancellationToken ct)
    {
        switch (toolName)
        {
            case "inspect":
                return await _workbench.InspectTargetAsync(
                    GetStringArg(arguments, "targetPath", "target_path") ?? string.Empty,
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    ct).ConfigureAwait(false);

            case "setup":
                return await _workbench.SetupEnvironmentAsync(
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    GetBoolArg(arguments, "forceReinstall", "force_reinstall") ?? false,
                    GetBoolArg(arguments, "enableConsoleLogging", "enable_console_logging") ?? true,
                    ct).ConfigureAwait(false);

            case "rollback":
                return await _workbench.RollbackEnvironmentAsync(
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    ct).ConfigureAwait(false);

            case "extract-code":
            case "extract_code":
                return await _workbench.ExtractCodeAsync(
                    GetStringArg(arguments, "moduleName", "module_name") ?? string.Empty,
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    ct).ConfigureAwait(false);

            case "query":
                return await _workbench.QuerySymbolsAsync(
                    GetStringArg(arguments, "searchTerm", "search_term", "query") ?? string.Empty,
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    GetStringArg(arguments, "kind"),
                    GetStringArg(arguments, "namespacePrefix", "namespace_prefix"),
                    GetStringArg(arguments, "moduleFilter", "module_filter"),
                    GetIntArg(arguments, "limit", "maxResults", "max_results") ?? 25,
                    ct).ConfigureAwait(false);

            case "search-code":
            case "search_code":
                return await _workbench.SearchCodeAsync(
                    GetStringArg(arguments, "pattern", "query") ?? string.Empty,
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    GetStringArg(arguments, "namespacePrefix", "namespace_prefix"),
                    GetStringArg(arguments, "moduleFilter", "module_filter"),
                    GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 25,
                    ct).ConfigureAwait(false);

            case "type":
                return await _workbench.GetTypeDetailAsync(
                    GetStringArg(arguments, "fullTypeName", "full_type_name") ?? string.Empty,
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    GetBoolArg(arguments, "signaturesOnly", "signatures_only") ?? false,
                    GetStringArg(arguments, "memberFilter", "member_filter"),
                    ct).ConfigureAwait(false);

            case "notes":
                return await _workbench.SaveResearchNotesAsync(
                    GetStringArg(arguments, "notes") ?? string.Empty,
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    ct).ConfigureAwait(false);

            case "index-assets":
            case "index_assets":
                return await _workbench.IndexAssetsAsync(
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    GetBoolArg(arguments, "forceReindex", "force_reindex") ?? false,
                    ct).ConfigureAwait(false);

            case "discover-assets":
            case "discover_assets":
                return await _workbench.DiscoverAssetsAsync(
                    GetStringArg(arguments, "searchPattern", "search_pattern"),
                    GetStringArg(arguments, "assetType", "asset_type"),
                    GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 50,
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    GetStringArg(arguments, "containerName", "container_name"),
                    GetStringArg(arguments, "assetKey", "asset_key"),
                    GetIntArg(arguments, "offset") ?? 0,
                    ct).ConfigureAwait(false);

            case "extract-assets":
            case "extract_assets":
                return await _workbench.ExtractAssetsAsync(
                    GetStringArg(arguments, "searchPattern", "search_pattern"),
                    GetStringArg(arguments, "assetType", "asset_type"),
                    GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 50,
                    GetStringArg(arguments, "outputDirectory", "output_directory"),
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    GetStringArg(arguments, "containerName", "container_name"),
                    GetStringArg(arguments, "assetKey", "asset_key"),
                    GetBoolArg(arguments, "disambiguateCollisions", "disambiguate_collisions") ?? true,
                    ct).ConfigureAwait(false);

            case "scaffold":
                return await ExecuteScaffoldToolAsync(arguments, ct).ConfigureAwait(false);

            case "build":
                return await _workbench.BuildModAsync(
                    GetStringArg(arguments, "projectFilePath", "project_file_path") ?? string.Empty,
                    GetStringArg(arguments, "configuration", "config") ?? "Debug",
                    ct).ConfigureAwait(false);

            case "logs":
                LogLevel? minLevel = null;
                string? levelStr = GetStringArg(arguments, "minLevel", "min_level");
                if (!string.IsNullOrWhiteSpace(levelStr) && Enum.TryParse<LogLevel>(levelStr, ignoreCase: true, out var parsedLevel))
                {
                    minLevel = parsedLevel;
                }

                return await _workbench.ReadLogsAsync(
                    GetStringArg(arguments, "logFilePath", "log_file_path"),
                    GetIntArg(arguments, "maxLines", "max_lines") ?? 200,
                    minLevel,
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    ct).ConfigureAwait(false);

            case "game-launch":
            case "game_launch":
                return await _workbench.LaunchInStudioAsync(
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    GetStringArg(arguments, "extraArguments", "extra_arguments"),
                    GetStringArg(arguments, "waitForLog", "wait_for_log"),
                    GetIntArg(arguments, "stabilityTimeoutSeconds", "stability_timeout_seconds") ?? 30,
                    ct).ConfigureAwait(false);

            case "game-close":
            case "game_close":
                return await _workbench.CloseStudioTargetAsync(ct).ConfigureAwait(false);

            case "recent-targets":
            case "recent_targets":
                return await _workbench.ListRecentTargetsAsync(ct).ConfigureAwait(false);

            case "status":
                return await _workbench.GetTargetStatusAsync(
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    ct).ConfigureAwait(false);

            case "capabilities":
                return await _workbench.GetCapabilitiesAsync(
                    GetIntArg(arguments, "port"),
                    GetStringArg(arguments, "token"),
                    ct).ConfigureAwait(false);

            case "screenshot":
                return await _workbench.CaptureScreenshotAsync(
                    GetStringArg(arguments, "savePath", "save_path"),
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetIntArg(arguments, "port"),
                    GetStringArg(arguments, "token"),
                    ct).ConfigureAwait(false);

            case "send-input":
            case "send_input":
                return await ExecuteSendInputToolAsync(arguments, ct).ConfigureAwait(false);

            case "get-controls":
            case "get_controls":
                return await _workbench.GetControlsAsync(
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    ct).ConfigureAwait(false);

            case "set-controls":
            case "set_controls":
                return await ExecuteSetControlsToolAsync(arguments, ct).ConfigureAwait(false);

            case "trigger-action":
            case "trigger_action":
                return await _workbench.TriggerActionAsync(
                    GetStringArg(arguments, "actionName", "action_name") ?? string.Empty,
                    GetStringArg(arguments, "targetPath", "target_path"),
                    GetStringArg(arguments, "runnerPath", "runner_path"),
                    GetIntArg(arguments, "port"),
                    GetStringArg(arguments, "token"),
                    ct).ConfigureAwait(false);

            case "wait-idle":
            case "wait_idle":
                return await _workbench.WaitIdleAsync(
                    GetIntArg(arguments, "maxFrames", "max_frames") ?? 10,
                    GetIntArg(arguments, "timeoutMs", "timeout_ms") ?? 3000,
                    GetIntArg(arguments, "port"),
                    GetStringArg(arguments, "token"),
                    ct).ConfigureAwait(false);

            case "list-ui":
            case "list_ui":
                return await _workbench.ListUiElementsAsync(
                    GetIntArg(arguments, "port"),
                    GetStringArg(arguments, "token"),
                    ct).ConfigureAwait(false);

            case "scene-info":
            case "scene_info":
                return await _workbench.GetSceneInfoAsync(
                    GetIntArg(arguments, "port"),
                    GetStringArg(arguments, "token"),
                    ct).ConfigureAwait(false);

            case "inspect-scene":
            case "inspect_scene":
                return await _workbench.InspectSceneAsync(
                    GetStringArg(arguments, "nameFilter", "name_filter"),
                    GetBoolArg(arguments, "includeInactive", "include_inactive") ?? false,
                    GetBoolArg(arguments, "visibleOnly", "visible_only") ?? false,
                    GetStringArg(arguments, "attributeFilter", "attribute_filter"),
                    GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 100,
                    GetIntArg(arguments, "port"),
                    GetStringArg(arguments, "token"),
                    ct).ConfigureAwait(false);

            case "inspect-runtime-assets":
            case "inspect_runtime_assets":
                return await _workbench.InspectRuntimeAssetsAsync(
                    GetStringArg(arguments, "nameFilter", "name_filter"),
                    GetStringArg(arguments, "assetType", "asset_type"),
                    GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 50,
                    GetIntArg(arguments, "port"),
                    GetStringArg(arguments, "token"),
                    ct).ConfigureAwait(false);

            default:
                return null;
        }
    }

    private async Task<string> ExecuteScaffoldToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var platformOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if ((arguments.TryGetProperty("platformOptions", out var po) || arguments.TryGetProperty("platform_options", out po)) &&
            po.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in po.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    platformOptions[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
        }

        return await _workbench.ScaffoldModAsync(
            modName: GetStringArg(arguments, "modName", "mod_name") ?? string.Empty,
            modId: GetStringArg(arguments, "modId", "mod_id"),
            version: GetStringArg(arguments, "version") ?? "1.0.0",
            outputDirectory: GetStringArg(arguments, "outputDirectory", "output_directory"),
            targetPath: GetStringArg(arguments, "targetPath", "target_path"),
            runnerPath: GetStringArg(arguments, "runnerPath", "runner_path"),
            includeAssetPipeline: GetBoolArg(arguments, "includeAssetPipeline", "include_asset_pipeline") ?? false,
            platformOptions: platformOptions.Count > 0 ? platformOptions : null,
            ct: ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteSendInputToolAsync(JsonElement arguments, CancellationToken ct)
    {
        string channel = GetStringArg(arguments, "channel") ?? VisionConstants.ChannelPointer;
        string defaultAction = string.Equals(channel, VisionConstants.ChannelPointer, StringComparison.OrdinalIgnoreCase)
            ? "click"
            : "press";

        var req = new VisionInputRequest
        {
            Channel = channel,
            Action = GetStringArg(arguments, "action") ?? defaultAction,
            X = GetFloatArg(arguments, "x"),
            Y = GetFloatArg(arguments, "y"),
            Button = GetIntArg(arguments, "button") ?? 0,
            Key = GetStringArg(arguments, "key"),
            GamepadButton = GetStringArg(arguments, "gamepadButton", "gamepad_button"),
            Stick = GetStringArg(arguments, "stick"),
            StickX = GetFloatArg(arguments, "stickX", "stick_x"),
            StickY = GetFloatArg(arguments, "stickY", "stick_y"),
            HoldFrames = GetIntArg(arguments, "holdFrames", "hold_frames") ?? 6,
            Method = GetStringArg(arguments, "method")
        };

        return await _workbench.SendInputAsync(
            req,
            GetIntArg(arguments, "port"),
            GetStringArg(arguments, "token"),
            ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteSetControlsToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var entries = new List<ControlActionEntry>();
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty("actions", out var arr) &&
            arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arr.EnumerateArray())
            {
                string? actionName = GetStringArg(item, "actionName", "action_name");
                if (string.IsNullOrWhiteSpace(actionName))
                {
                    continue;
                }

                string displayName = GetStringArg(item, "displayName", "display_name") ?? actionName;
                string channelStr = GetStringArg(item, "channel") ?? "Keyboard";
                if (!Enum.TryParse<InputChannel>(channelStr, ignoreCase: true, out var parsedChannel))
                {
                    parsedChannel = InputChannel.Keyboard;
                }

                var binding = new InputBinding(
                    Channel: parsedChannel,
                    Key: GetStringArg(item, "key"),
                    GamepadButton: GetStringArg(item, "gamepadButton", "gamepad_button"),
                    MouseButton: GetIntArg(item, "mouseButton", "mouse_button") ?? 0,
                    NormalizedX: GetFloatArg(item, "normalizedX", "normalized_x"),
                    NormalizedY: GetFloatArg(item, "normalizedY", "normalized_y"),
                    HoldFrames: GetIntArg(item, "holdFrames", "hold_frames") ?? 6,
                    Description: GetStringArg(item, "description"));

                entries.Add(new ControlActionEntry(actionName, displayName, "agent", [binding]));
            }
        }

        return await _workbench.SetControlsAsync(
            entries,
            overwriteUserBindings: GetBoolArg(arguments, "overwriteUserBindings", "overwrite_user_bindings") ?? false,
            targetPath: GetStringArg(arguments, "targetPath", "target_path"),
            runnerPath: GetStringArg(arguments, "runnerPath", "runner_path"),
            ct: ct).ConfigureAwait(false);
    }

    private static Dictionary<string, string?> ExtractStringDictionary(JsonElement arguments)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return dict;
        }

        foreach (var prop in arguments.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => prop.Value.GetRawText(),
                _ => null
            };
        }

        return dict;
    }

    private static string? GetStringArg(JsonElement arguments, params string[] propertyNames)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in propertyNames)
        {
            if (arguments.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                string? val = prop.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    return val;
                }
            }
        }

        return null;
    }

    private static int? GetIntArg(JsonElement arguments, params string[] propertyNames)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in propertyNames)
        {
            if (arguments.TryGetProperty(name, out var prop) && prop.TryGetInt32(out int val))
            {
                return val;
            }
        }

        return null;
    }

    private static bool? GetBoolArg(JsonElement arguments, params string[] propertyNames)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in propertyNames)
        {
            if (arguments.TryGetProperty(name, out var prop) &&
                (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            {
                return prop.GetBoolean();
            }
        }

        return null;
    }

    private static float? GetFloatArg(JsonElement arguments, params string[] propertyNames)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in propertyNames)
        {
            if (arguments.TryGetProperty(name, out var prop) && prop.TryGetDouble(out double val))
            {
                return (float)val;
            }
        }

        return null;
    }

    private static string HandleMethodNotFound(JsonRpcRequest request)
    {
        var errorResponse = JsonRpcResponse.CreateError(
            request.Id,
            JsonRpcErrorCodes.MethodNotFound,
            $"Method not found: '{request.Method}'.");
        return JsonSerializer.Serialize(errorResponse, JsonOptions);
    }
}
