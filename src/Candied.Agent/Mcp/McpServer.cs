using System.Text.Json;
using System.Text.Json.Serialization;
using Candied.Core.Models;

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
        _workbench = workbench;
    }

    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct = default)
    {
        string? line;
        var activeTasks = new List<Task>();
        var writeLock = new SemaphoreSlim(1, 1);

        while (!ct.IsCancellationRequested && (line = await input.ReadLineAsync(ct).ConfigureAwait(false)) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var currentLine = line;
            var task = Task.Run(async () =>
            {
                var responseJson = await HandleMessageAsync(currentLine, ct).ConfigureAwait(false);
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

        // Notification: client initialized acknowledgement
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

    private static string HandleInitialize(JsonRpcRequest request)
    {
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
                version = "1.0.0"
            },
            instructions = "Candied is an autonomous reverse-engineering and modding workbench for Unity Mono games.\n" +
                "When requested to create or verify a mod, follow this strict pipeline:\n" +
                "Step 1: Call 'inspect(gameDirectory)' to audit engine, architecture, BepInEx presence, and discovered assemblies.\n" +
                "Step 2: If 'bepInEx.isInstalled' is false or missing, call 'setup(gameDirectory)' to provision the mod loader.\n" +
                "Step 3: Call 'decompile(assemblyName)' targeting the primary CoreGame assembly (e.g. 'Assembly-CSharp' or '<GameName>.Scripts').\n" +
                "Step 4: Use 'query' and 'type' to find target classes, methods, and fields to patch.\n" +
                "Optional Step 4.5: For live runtime inspection, call 'launch', 'capabilities', 'screenshot', and 'inspect-scene-objects' where applicable.\n" +
                "Step 5: Call 'scaffold(...)'. Set 'includeAssetPipeline: true' ONLY if the mod requires custom sprites/textures; otherwise leave it false for code/logic mods.\n" +
                "Step 6: Write Harmony patches in the generated 'mods/<ModName>/Patches/' folder.\n" +
                "Step 7: Call 'build(projectFilePath)'. If compilation fails, inspect structured diagnostics, fix code, and rebuild until successful.\n" +
                "Step 8: Call 'logs(logFilePath)' to inspect runtime BepInEx logs and verify patch execution."
        };

        var response = JsonRpcResponse.Success(request.Id, result);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static string HandlePing(JsonRpcRequest request)
    {
        var response = JsonRpcResponse.Success(request.Id, new { });
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static string HandleToolsList(JsonRpcRequest request)
    {
        var tools = McpToolDefinitions.GetAllTools();
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

        var toolName = nameProp.GetString()!;
        var arguments = paramsObj.TryGetProperty("arguments", out var argsProp) && argsProp.ValueKind == JsonValueKind.Object
            ? argsProp
            : default;

        string toolOutputJson;

        switch (toolName)
        {
            case "inspect":
                toolOutputJson = await ExecuteInspectGameEnvironmentToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "setup":
                toolOutputJson = await ExecuteSetupGameEnvironmentToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "rollback":
                toolOutputJson = await ExecuteRollbackGameEnvironmentToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "decompile":
                toolOutputJson = await ExecuteDecompileAssemblyToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "query":
                toolOutputJson = await ExecuteSearchGameSymbolsToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "search-code":
            case "search_code":
                toolOutputJson = await ExecuteSearchGameCodeToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "notes":
                toolOutputJson = await ExecuteSaveResearchNotesToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "type":
                toolOutputJson = await ExecuteGetTypeDetailToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "scaffold":
                toolOutputJson = await ExecuteScaffoldModToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "add-reference":
            case "add_reference":
                toolOutputJson = await ExecuteAddProjectReferenceToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "build":
                toolOutputJson = await ExecuteBuildModToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "logs":
                toolOutputJson = await ExecuteReadLogsToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "status":
                toolOutputJson = await ExecuteGameProcessStatusToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "launch":
                toolOutputJson = await ExecuteLaunchGameToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "stop":
                toolOutputJson = await ExecuteStopGameToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "index-assets":
                toolOutputJson = await ExecuteIndexGameAssetsToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "discover-assets":
                toolOutputJson = await ExecuteDiscoverGameAssetsToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "extract-assets":
                toolOutputJson = await ExecuteExtractGameAssetsToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "capabilities":
                toolOutputJson = await ExecuteGameCapabilitiesToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "screenshot":
                toolOutputJson = await ExecuteGameScreenshotToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "click":
                toolOutputJson = await ExecuteGameClickAtToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "wait-idle":
                toolOutputJson = await ExecuteGameWaitIdleToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "scene-info":
                toolOutputJson = await ExecuteGameSceneInfoToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "inspect-scene-objects":
                toolOutputJson = await ExecuteGameInspectSceneObjectsToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "inspect-runtime-assets":
            case "inspect_runtime_assets":
                toolOutputJson = await ExecuteGameInspectRuntimeAssetsToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "list-ui":
                toolOutputJson = await ExecuteGameListUiToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "list-games":
                toolOutputJson = await _workbench.ListRegisteredGamesAsync(ct).ConfigureAwait(false);
                break;

            case "game-launch":
                toolOutputJson = await ExecuteGameLaunchToolAsync(arguments, ct).ConfigureAwait(false);
                break;

            case "game-close":
                toolOutputJson = await _workbench.CloseGameAsync(ct).ConfigureAwait(false);
                break;

            default:
                var unknownTool = JsonRpcResponse.CreateError(
                    request.Id,
                    JsonRpcErrorCodes.MethodNotFound,
                    $"Unknown tool name: '{toolName}'.");
                return JsonSerializer.Serialize(unknownTool, JsonOptions);
        }

        var isError = false;
        try
        {
            using var doc = JsonDocument.Parse(toolOutputJson);
            if (doc.RootElement.TryGetProperty("success", out var successProp))
            {
                isError = !successProp.GetBoolean();
            }
        }
        catch (JsonException)
        {
            // If output is not standard JSON, keep isError as false
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
            isError = isError
        };

        var response = JsonRpcResponse.Success(request.Id, mcpResult);
        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static string? GetStringArg(JsonElement arguments, params string[] propertyNames)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (arguments.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var val = prop.GetString();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        return null;
    }

    private static int? GetIntArg(JsonElement arguments, params string[] propertyNames)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (arguments.TryGetProperty(name, out var prop) && prop.TryGetInt32(out var val))
            {
                return val;
            }
        }
        return null;
    }

    private static long? GetLongArg(JsonElement arguments, params string[] propertyNames)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (arguments.TryGetProperty(name, out var prop) && prop.TryGetInt64(out var val))
            {
                return val;
            }
        }
        return null;
    }

    private static bool? GetBoolArg(JsonElement arguments, params string[] propertyNames)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (arguments.TryGetProperty(name, out var prop) && (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False))
            {
                return prop.GetBoolean();
            }
        }
        return null;
    }

    private static float? GetFloatArg(JsonElement arguments, params string[] propertyNames)
    {
        if (arguments.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in propertyNames)
        {
            if (arguments.TryGetProperty(name, out var prop) && prop.TryGetDouble(out var val))
            {
                return (float)val;
            }
        }
        return null;
    }

    private async Task<string> ExecuteInspectGameEnvironmentToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir") ?? string.Empty;
        return await _workbench.InspectGameEnvironmentAsync(gameDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteSetupGameEnvironmentToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        var forceReinstall = GetBoolArg(arguments, "forceReinstall", "force_reinstall") ?? false;
        var enableConsoleLogging = GetBoolArg(arguments, "enableConsoleLogging", "enable_console_logging") ?? true;

        return await _workbench.SetupGameEnvironmentAsync(gameDirectory, forceReinstall, enableConsoleLogging, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteRollbackGameEnvironmentToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        return await _workbench.RollbackGameEnvironmentAsync(gameDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteDecompileAssemblyToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var assemblyName = GetStringArg(arguments, "assemblyName", "assembly_name", "assemblyPath", "assembly_path") ?? string.Empty;
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");

        return await _workbench.DecompileAssemblyAsync(gameDirectory, assemblyName, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteSearchGameSymbolsToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var searchTerm = GetStringArg(arguments, "searchTerm", "search_term", "query") ?? string.Empty;
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        var kind = GetStringArg(arguments, "kind", "symbolKind", "symbol_kind");
        var namespacePrefix = GetStringArg(arguments, "namespacePrefix", "namespace_prefix", "namespace");
        var limit = GetIntArg(arguments, "limit", "maxResults", "max_results") ?? 25;

        return await _workbench.SearchGameSymbolsOnlyAsync(
            searchTerm, gameDirectory, kind, namespacePrefix, limit, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteSearchGameCodeToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var pattern = GetStringArg(arguments, "pattern", "searchTerm", "search_term", "query") ?? string.Empty;
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        var namespacePrefix = GetStringArg(arguments, "namespacePrefix", "namespace_prefix", "namespace");
        var assemblyName = GetStringArg(arguments, "assemblyName", "assembly_name", "assembly");
        var maxResults = GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 25;

        return await _workbench.SearchGameCodeAsync(
            pattern, gameDirectory, namespacePrefix, assemblyName, maxResults, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteSaveResearchNotesToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var notes = GetStringArg(arguments, "notes") ?? string.Empty;
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");

        return await _workbench.SaveResearchNotesAsync(notes, gameDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGetTypeDetailToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var fullTypeName = GetStringArg(arguments, "fullTypeName", "full_type_name", "typeName", "type_name") ?? string.Empty;
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        var signaturesOnly = GetBoolArg(arguments, "signaturesOnly", "signatures_only") ?? false;
        var memberFilter = GetStringArg(arguments, "memberFilter", "member_filter", "member");

        return await _workbench.GetTypeDetailAsync(
            fullTypeName, gameDirectory, signaturesOnly, memberFilter, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteAddProjectReferenceToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var projectFilePath = GetStringArg(arguments, "projectFilePath", "project_file_path", "project", "path") ?? string.Empty;
        var assemblyName = GetStringArg(arguments, "assemblyName", "assembly_name", "assembly") ?? string.Empty;
        var publicize = GetBoolArg(arguments, "publicize") ?? false;
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");

        return await _workbench.AddProjectReferenceAsync(
            projectFilePath, assemblyName, publicize, gameDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteScaffoldModToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var modName = GetStringArg(arguments, "modName", "mod_name") ?? string.Empty;
        var pluginGuid = GetStringArg(arguments, "pluginGuid", "plugin_guid") ?? string.Empty;
        var version = GetStringArg(arguments, "version") ?? "1.0.0";
        var outputDirectory = GetStringArg(arguments, "outputDirectory", "output_directory", "out") ?? string.Empty;
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir") ?? string.Empty;

        var publicizedList = new List<string>();
        if ((arguments.TryGetProperty("publicizedAssemblies", out var pubProp) || arguments.TryGetProperty("publicized_assemblies", out pubProp)) && pubProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pubProp.EnumerateArray())
            {
                var val = item.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    publicizedList.Add(val);
                }
            }
        }

        var includeAssets = GetBoolArg(arguments, "includeAssetPipeline", "include_asset_pipeline", "withAssets", "with_assets") ?? false;

        return await _workbench.ScaffoldModByPathAsync(
            modName,
            pluginGuid,
            version,
            outputDirectory,
            gameDirectory,
            publicizedList,
            includeAssets,
            ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteBuildModToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var projectFilePath = GetStringArg(arguments, "projectFilePath", "project_file_path", "project", "path") ?? string.Empty;
        var configuration = GetStringArg(arguments, "configuration", "config") ?? "Debug";

        return await _workbench.BuildModAsync(projectFilePath, configuration, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteReadLogsToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var logFilePath = GetStringArg(arguments, "logFilePath", "log_file_path", "path");
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        var maxLines = GetIntArg(arguments, "maxLines", "max_lines") ?? 200;

        LogLevel? minLevel = null;
        var levelStr = GetStringArg(arguments, "minLevel", "min_level");
        if (!string.IsNullOrWhiteSpace(levelStr) && Enum.TryParse<LogLevel>(levelStr, ignoreCase: true, out var parsedLevel))
        {
            minLevel = parsedLevel;
        }

        return await _workbench.ReadLogsAsync(logFilePath, maxLines, minLevel, gameDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameProcessStatusToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        return await _workbench.GetGameProcessStatusAsync(gameDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteLaunchGameToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        var windowed = GetBoolArg(arguments, "windowed") ?? true;
        var width = GetIntArg(arguments, "screenWidth", "screen_width") ?? 1280;
        var height = GetIntArg(arguments, "screenHeight", "screen_height") ?? 720;

        return await _workbench.LaunchGameAsync(gameDirectory, windowed, width, height, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteStopGameToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        return await _workbench.StopGameAsync(gameDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteIndexGameAssetsToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        return await _workbench.IndexGameAssetsAsync(gameDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteDiscoverGameAssetsToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var searchPattern = GetStringArg(arguments, "searchPattern", "search_pattern", "pattern");
        var assetType = GetStringArg(arguments, "assetType", "asset_type", "type");
        var maxResults = GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 50;
        var offset = GetIntArg(arguments, "offset") ?? 0;
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        var containerName = GetStringArg(arguments, "containerName", "container_name", "container");
        var pathId = GetLongArg(arguments, "pathId", "path_id");

        return await _workbench.DiscoverGameAssetsAsync(
            searchPattern, assetType, maxResults, gameDirectory, containerName, pathId, offset, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteExtractGameAssetsToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var searchPattern = GetStringArg(arguments, "searchPattern", "search_pattern", "pattern");
        var assetType = GetStringArg(arguments, "assetType", "asset_type", "type");
        var maxResults = GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 50;
        var outputDirectory = GetStringArg(arguments, "outputDirectory", "output_directory", "out");
        var gameDirectory = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        var containerName = GetStringArg(arguments, "containerName", "container_name", "container");
        var pathId = GetLongArg(arguments, "pathId", "path_id");
        var disambiguateCollisions = GetBoolArg(arguments, "disambiguateCollisions", "disambiguate_collisions") ?? true;

        return await _workbench.ExtractGameAssetsAsync(
            searchPattern, assetType, maxResults, outputDirectory, gameDirectory, containerName, pathId, disambiguateCollisions, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameCapabilitiesToolAsync(JsonElement arguments, CancellationToken ct)
    {
        int? port = GetIntArg(arguments, "port");
        string? token = GetStringArg(arguments, "token");
        return await _workbench.GetGameCapabilitiesAsync(port, token, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameScreenshotToolAsync(JsonElement arguments, CancellationToken ct)
    {
        string? savePath = GetStringArg(arguments, "savePath", "save_path");
        string? gameDir = GetStringArg(arguments, "gameDirectory", "game_directory", "dir");
        int? port = GetIntArg(arguments, "port");
        string? token = GetStringArg(arguments, "token");
        return await _workbench.CaptureGameScreenshotAsync(port, token, savePath, gameDir, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameClickAtToolAsync(JsonElement arguments, CancellationToken ct)
    {
        var xVal = GetFloatArg(arguments, "x");
        var yVal = GetFloatArg(arguments, "y");
        if (xVal == null || yVal == null)
        {
            return JsonSerializer.Serialize(new
            {
                success = false,
                error = "Parametri 'x' e 'y' obbligatori mancanti per click.",
                suggestion = "Fornisci coordinate x e y in pixel relative allo screenshot (origine in alto a sinistra)."
            }, JsonOptions);
        }

        float x = xVal.Value;
        float y = yVal.Value;
        int button = GetIntArg(arguments, "button") ?? 0;
        int holdFrames = GetIntArg(arguments, "holdFrames", "hold_frames") ?? 6;
        string? method = GetStringArg(arguments, "method");
        int? port = GetIntArg(arguments, "port");
        string? token = GetStringArg(arguments, "token");

        return await _workbench.ClickGameAtAsync(x, y, button, holdFrames, method, port, token, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameWaitIdleToolAsync(JsonElement arguments, CancellationToken ct)
    {
        int maxFrames = GetIntArg(arguments, "maxFrames", "max_frames") ?? 10;
        int timeoutMs = GetIntArg(arguments, "timeoutMs", "timeout_ms") ?? 3000;
        int? port = GetIntArg(arguments, "port");
        string? token = GetStringArg(arguments, "token");

        return await _workbench.WaitGameIdleAsync(maxFrames, timeoutMs, port, token, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameSceneInfoToolAsync(JsonElement arguments, CancellationToken ct)
    {
        int? port = GetIntArg(arguments, "port");
        string? token = GetStringArg(arguments, "token");

        return await _workbench.GetGameSceneInfoAsync(port, token, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameInspectSceneObjectsToolAsync(JsonElement arguments, CancellationToken ct)
    {
        string? nameFilter = GetStringArg(arguments, "nameFilter", "name_filter", "filter", "name");
        bool includeInactive = GetBoolArg(arguments, "includeInactive", "include_inactive") ?? false;
        bool visibleOnly = GetBoolArg(arguments, "visibleOnly", "visible_only") ?? false;
        string? sortingLayer = GetStringArg(arguments, "sortingLayer", "sorting_layer");
        int maxResults = GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 100;
        int? port = GetIntArg(arguments, "port");
        string? token = GetStringArg(arguments, "token");

        return await _workbench.InspectSceneObjectsAsync(
            nameFilter, port, token, includeInactive, visibleOnly, sortingLayer, maxResults, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameInspectRuntimeAssetsToolAsync(JsonElement arguments, CancellationToken ct)
    {
        string? nameFilter = GetStringArg(arguments, "nameFilter", "name_filter", "pattern", "filter", "name");
        string? assetType = GetStringArg(arguments, "assetType", "asset_type", "type");
        int maxResults = GetIntArg(arguments, "maxResults", "max_results", "limit") ?? 50;
        int? port = GetIntArg(arguments, "port");
        string? token = GetStringArg(arguments, "token");

        return await _workbench.InspectRuntimeAssetsAsync(
            nameFilter, assetType, maxResults, port, token, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameListUiToolAsync(JsonElement arguments, CancellationToken ct)
    {
        int? port = GetIntArg(arguments, "port");
        string? token = GetStringArg(arguments, "token");

        return await _workbench.ListGameUiElementsAsync(port, token, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGameLaunchToolAsync(JsonElement arguments, CancellationToken ct)
    {
        string? name = GetStringArg(arguments, "name");
        string? waitForLog = GetStringArg(arguments, "waitForLog", "wait_for_log");
        return await _workbench.LaunchGameByNameAsync(name ?? string.Empty, waitForLog, ct).ConfigureAwait(false);
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
