using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Candied.Agent;
using Candied.Core.Models;
using Candied.Core.UseCases;
using Candied.Decompiler;
using Candied.Workspace;
using Xunit;

namespace Candied.Tests.Integration;

public sealed class ReadLogsIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;

    public ReadLogsIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_ReadLogsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddDecompilerInfrastructure();
        services.AddWorkspaceInfrastructure();
        services.AddTransient<ScaffoldModUseCase>();
        services.AddTransient<BuildModUseCase>();
        services.AddTransient<ReadLogsUseCase>();
        services.AddTransient<GetTypeDetailUseCase>();
        services.AddTransient<AgentWorkbenchService>();

        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AgentWorkbenchService_Can_Read_Logs_And_Filter_By_MinLevel()
    {
        // Arrange
        var logFile = Path.Combine(_tempDir, "LogOutput.log");
        var logLines = new[]
        {
            "[Debug  :   BepInEx] Loading config",
            "[Info   :   BepInEx] Patcher loaded",
            "[Warning:  SpeedMod] Deprecated method used",
            "[Error  :  SpeedMod] Crash in Harmony prefix patch"
        };
        await File.WriteAllLinesAsync(logFile, logLines);

        var workbench = _serviceProvider.GetRequiredService<AgentWorkbenchService>();

        // Act - request only Warning and above
        var responseJson = await workbench.ReadLogsAsync(logFile, maxLines: 10, minLevel: LogLevel.Warning);

        // Assert
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal(2, root.GetProperty("count").GetInt32());

        var entries = root.GetProperty("entries");
        Assert.Equal(2, entries.GetArrayLength());

        var first = entries[0];
        Assert.Equal("warning", first.GetProperty("level").GetString()?.ToLowerInvariant());
        Assert.Equal("SpeedMod", first.GetProperty("source").GetString());

        var second = entries[1];
        Assert.Equal("error", second.GetProperty("level").GetString()?.ToLowerInvariant());
        Assert.Equal("SpeedMod", second.GetProperty("source").GetString());
    }
}
