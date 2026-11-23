using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Candied.Agent;
using Candied.Agent.Mcp;
using Candied.Core.UseCases;
using Candied.Decompiler;
using Candied.Workspace;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class McpServerTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly McpServer _server;

    public McpServerTests()
    {
        var services = new ServiceCollection();
        services.AddDecompilerInfrastructure();
        services.AddWorkspaceInfrastructure();
        services.AddTransient<InspectGameEnvironmentUseCase>();
        services.AddTransient<SetupGameEnvironmentUseCase>();
        services.AddTransient<RollbackGameEnvironmentUseCase>();
        services.AddTransient<DecompileAssemblyUseCase>();
        services.AddTransient<SearchGameSymbolsUseCase>();
        services.AddTransient<SaveResearchNotesUseCase>();
        services.AddTransient<ScaffoldModUseCase>();
        services.AddTransient<BuildModUseCase>();
        services.AddTransient<ReadLogsUseCase>();
        services.AddTransient<GetTypeDetailUseCase>();
        services.AddTransient<GetGameProcessStatusUseCase>();
        services.AddTransient<LaunchGameUseCase>();
        services.AddTransient<StopGameUseCase>();
        services.AddTransient<DiscoverAssetsUseCase>();
        services.AddTransient<ExtractAssetsUseCase>();
        services.AddTransient<IndexAssetsUseCase>();
        services.AddTransient<AgentWorkbenchService>();
        services.AddTransient<McpServer>();

        _serviceProvider = services.BuildServiceProvider();
        _server = _serviceProvider.GetRequiredService<McpServer>();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task HandleMessageAsync_Initialize_Returns_ProtocolVersion_And_ServerInfo()
    {
        // Arrange
        var request = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}""";

        // Act
        var responseJson = await _server.HandleMessageAsync(request);

        // Assert
        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(1, root.GetProperty("id").GetInt32());

        var result = root.GetProperty("result");
        Assert.Equal("2024-11-05", result.GetProperty("protocolVersion").GetString());

        var serverInfo = result.GetProperty("serverInfo");
        Assert.Equal("Candied-workbench", serverInfo.GetProperty("name").GetString());
        Assert.Equal("1.0.0", serverInfo.GetProperty("version").GetString());
        Assert.True(result.TryGetProperty("capabilities", out _));
        Assert.True(result.TryGetProperty("instructions", out var instructions) && !string.IsNullOrWhiteSpace(instructions.GetString()));
    }

    [Fact]
    public async Task HandleMessageAsync_Ping_Returns_Empty_Result()
    {
        var request = """{"jsonrpc":"2.0","id":2,"method":"ping"}""";

        var responseJson = await _server.HandleMessageAsync(request);

        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("id").GetInt32());
        Assert.True(root.TryGetProperty("result", out _));
    }

    [Fact]
    public async Task HandleMessageAsync_NotificationsInitialized_Returns_Null()
    {
        var notification = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

        var responseJson = await _server.HandleMessageAsync(notification);

        Assert.Null(responseJson);
    }

    [Fact]
    public async Task HandleMessageAsync_ToolsList_Returns_Advertised_Workbench_Tools()
    {
        // Arrange
        var request = """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""";

        // Act
        var responseJson = await _server.HandleMessageAsync(request);

        // Assert
        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");

        Assert.Equal(29, tools.GetArrayLength());

        var toolNames = new List<string>();
        foreach (var t in tools.EnumerateArray())
        {
            toolNames.Add(t.GetProperty("name").GetString()!);
        }

        Assert.Contains("inspect", toolNames);
        Assert.Contains("setup", toolNames);
        Assert.Contains("rollback", toolNames);
        Assert.Contains("decompile", toolNames);
        Assert.Contains("query", toolNames);
        Assert.Contains("search-code", toolNames);
        Assert.Contains("notes", toolNames);
        Assert.Contains("type", toolNames);
        Assert.Contains("scaffold", toolNames);
        Assert.Contains("add-reference", toolNames);
        Assert.Contains("build", toolNames);
        Assert.Contains("logs", toolNames);
        Assert.Contains("status", toolNames);
        Assert.Contains("launch", toolNames);
        Assert.Contains("stop", toolNames);
        Assert.Contains("index-assets", toolNames);
        Assert.Contains("discover-assets", toolNames);
        Assert.Contains("extract-assets", toolNames);
        Assert.Contains("capabilities", toolNames);
        Assert.Contains("screenshot", toolNames);
        Assert.Contains("click", toolNames);
        Assert.Contains("wait-idle", toolNames);
        Assert.Contains("scene-info", toolNames);
        Assert.Contains("inspect-scene-objects", toolNames);
        Assert.Contains("inspect-runtime-assets", toolNames);
        Assert.Contains("list-ui", toolNames);
        Assert.Contains("list-games", toolNames);
        Assert.Contains("game-launch", toolNames);
        Assert.Contains("game-close", toolNames);
    }

    [Fact]
    public async Task HandleMessageAsync_ToolsCall_Rejects_PreRenameToolNames()
    {
        var request = """
{
    "jsonrpc": "2.0",
    "id": 8,
    "method": "tools/call",
    "params": {
        "name": "inspect_game_environment",
        "arguments": {
            "gameDirectory": "C:\\non_existent_game_dir"
        }
    }
}
""";

        var responseJson = await _server.HandleMessageAsync(request);

        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        Assert.Equal(
            JsonRpcErrorCodes.MethodNotFound,
            doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task HandleMessageAsync_ToolsCall_Returns_Standard_Mcp_Content_Envelope()
    {
        // Arrange: calling logs with a non-existent file
        var request = """
{
    "jsonrpc": "2.0",
    "id": 4,
    "method": "tools/call",
    "params": {
        "name": "logs",
        "arguments": {
            "logFilePath": "C:\\fake\\non_existent_log.log"
        }
    }
}
""";

        // Act
        var responseJson = await _server.HandleMessageAsync(request);

        // Assert
        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal(4, root.GetProperty("id").GetInt32());
        var result = root.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());

        var content = result.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("text", content[0].GetProperty("type").GetString());

        var text = content[0].GetProperty("text").GetString();
        Assert.NotNull(text);
        Assert.Contains("does not exist", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleMessageAsync_ToolsCall_InspectGameEnvironment_Dispatches_Correctly()
    {
        var request = """
{
    "jsonrpc": "2.0",
    "id": 5,
    "method": "tools/call",
    "params": {
        "name": "inspect",
        "arguments": {
            "gameDirectory": "C:\\non_existent_game_dir"
        }
    }
}
""";

        var responseJson = await _server.HandleMessageAsync(request);

        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal(5, root.GetProperty("id").GetInt32());
        var result = root.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var content = result.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Contains("does not exist", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task HandleMessageAsync_ToolsCall_SetupGameEnvironment_Dispatches_Correctly()
    {
        var request = """
{
    "jsonrpc": "2.0",
    "id": 7,
    "method": "tools/call",
    "params": {
        "name": "setup",
        "arguments": {
            "gameDirectory": "C:\\non_existent_game_dir"
        }
    }
}
""";

        var responseJson = await _server.HandleMessageAsync(request);

        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal(7, root.GetProperty("id").GetInt32());
        var result = root.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
        var content = result.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Contains("does not exist", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task HandleMessageAsync_ToolsCall_GameProcessStatus_Dispatches_Correctly()
    {
        var request = """
{
    "jsonrpc": "2.0",
    "id": 8,
    "method": "tools/call",
    "params": {
        "name": "status",
        "arguments": {
            "gameDirectory": "C:\\non_existent_game_dir"
        }
    }
}
""";

        var responseJson = await _server.HandleMessageAsync(request);

        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal(8, root.GetProperty("id").GetInt32());
        var result = root.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task HandleMessageAsync_ToolsCall_DiscoverGameAssets_Dispatches_Correctly()
    {
        var request = """
{
    "jsonrpc": "2.0",
    "id": 9,
    "method": "tools/call",
    "params": {
        "name": "discover-assets",
        "arguments": {
            "searchPattern": "Beard",
            "gameDirectory": "C:\\non_existent_game_dir"
        }
    }
}
""";

        var responseJson = await _server.HandleMessageAsync(request);

        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal(9, root.GetProperty("id").GetInt32());
        var result = root.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task HandleMessageAsync_ToolsCall_ExtractGameAssets_Dispatches_Correctly()
    {
        var request = """
{
    "jsonrpc": "2.0",
    "id": 10,
    "method": "tools/call",
    "params": {
        "name": "extract-assets",
        "arguments": {
            "searchPattern": "WalterWhite_Avatar",
            "gameDirectory": "C:\\non_existent_game_dir"
        }
    }
}
""";

        var responseJson = await _server.HandleMessageAsync(request);

        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.Equal(10, root.GetProperty("id").GetInt32());
        var result = root.GetProperty("result");
        Assert.True(result.GetProperty("isError").GetBoolean());
    }

    [Fact]
    public async Task HandleMessageAsync_UnknownMethod_Returns_MethodNotFound_Error()
    {
        var request = """{"jsonrpc":"2.0","id":6,"method":"unknown/endpoint"}""";

        var responseJson = await _server.HandleMessageAsync(request);

        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var error = doc.RootElement.GetProperty("error");

        Assert.Equal(JsonRpcErrorCodes.MethodNotFound, error.GetProperty("code").GetInt32());
        Assert.Contains("Method not found", error.GetProperty("message").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleMessageAsync_MalformedJson_Returns_ParseError()
    {
        var malformedRequest = "THIS IS NOT VALID JSON";

        var responseJson = await _server.HandleMessageAsync(malformedRequest);

        Assert.NotNull(responseJson);
        using var doc = JsonDocument.Parse(responseJson);
        var error = doc.RootElement.GetProperty("error");

        Assert.Equal(JsonRpcErrorCodes.ParseError, error.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task RunAsync_Processes_Stream_And_Exits_On_EOF()
    {
        // Arrange
        var inputLines = """
{"jsonrpc":"2.0","id":1,"method":"ping"}
{"jsonrpc":"2.0","id":2,"method":"initialize","params":{}}
""";
        using var reader = new StringReader(inputLines);
        using var writer = new StringWriter();

        // Act
        await _server.RunAsync(reader, writer);

        // Assert
        var output = writer.ToString();
        Assert.Contains("\"id\":1", output, StringComparison.Ordinal);
        Assert.Contains("\"id\":2", output, StringComparison.Ordinal);
        Assert.Contains("\"protocolVersion\":\"2024-11-05\"", output, StringComparison.Ordinal);
    }
}
