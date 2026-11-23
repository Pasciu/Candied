using Candied.Core.Models;
using Candied.Workspace.Adapters;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class BepInExLogMonitorAdapterTests : IDisposable
{
    private readonly string _tempDir;

    public BepInExLogMonitorAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Candied_LogTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ParseLines_Parses_Standard_And_MultiLine_StackTraces()
    {
        // Arrange
        var sampleLines = new[]
        {
            "[Message:   BepInEx] BepInEx 5.4.21.0 - GameName",
            "[Info   :   BepInEx] Loaded 1 patcher",
            "[Warning:  SpeedMod] Missing optional config parameter",
            "[Error  :  SpeedMod] Unhandled exception occurred",
            "System.NullReferenceException: Object reference not set to an instance of an object",
            "  at SpeedMod.Plugin.Awake () [0x00000] in <00000>:0",
            "[Debug  :   Harmony] Patching GameManager.Update"
        };

        // Act
        var entries = BepInExLogMonitorAdapter.ParseLines(sampleLines);

        // Assert
        Assert.Equal(5, entries.Count);

        // 1. Message -> Info
        Assert.Equal(LogLevel.Info, entries[0].Level);
        Assert.Equal("BepInEx", entries[0].Source);
        Assert.Equal("BepInEx 5.4.21.0 - GameName", entries[0].Message);
        Assert.Null(entries[0].StackTrace);

        // 2. Info
        Assert.Equal(LogLevel.Info, entries[1].Level);
        Assert.Equal("BepInEx", entries[1].Source);

        // 3. Warning
        Assert.Equal(LogLevel.Warning, entries[2].Level);
        Assert.Equal("SpeedMod", entries[2].Source);
        Assert.Equal("Missing optional config parameter", entries[2].Message);

        // 4. Error with StackTrace
        Assert.Equal(LogLevel.Error, entries[3].Level);
        Assert.Equal("SpeedMod", entries[3].Source);
        Assert.Equal("Unhandled exception occurred", entries[3].Message);
        Assert.NotNull(entries[3].StackTrace);
        Assert.Contains("System.NullReferenceException", entries[3].StackTrace, StringComparison.Ordinal);
        Assert.Contains("SpeedMod.Plugin.Awake", entries[3].StackTrace, StringComparison.Ordinal);

        // 5. Debug
        Assert.Equal(LogLevel.Debug, entries[4].Level);
        Assert.Equal("Harmony", entries[4].Source);
    }

    [Fact]
    public async Task ReadRecentLogsAsync_Returns_Error_For_Missing_File()
    {
        var adapter = new BepInExLogMonitorAdapter();
        var missingPath = Path.Combine(_tempDir, "MissingLog.log");

        var result = await adapter.ReadRecentLogsAsync(missingPath);

        Assert.True(result.IsFailure);
        Assert.Contains("does not exist", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadRecentLogsAsync_Tails_Only_Requested_Lines()
    {
        // Arrange
        var logFile = Path.Combine(_tempDir, "LogOutput.log");
        var lines = new List<string>();
        for (var i = 1; i <= 20; i++)
        {
            lines.Add($"[Info   :   Mod{i}] Message number {i}");
        }
        await File.WriteAllLinesAsync(logFile, lines);

        var adapter = new BepInExLogMonitorAdapter();

        // Act
        var result = await adapter.ReadRecentLogsAsync(logFile, maxLines: 5);

        // Assert
        Assert.True(result.IsSuccess);
        var entries = result.Value;
        Assert.Equal(5, entries.Count);
        Assert.Equal("Mod16", entries[0].Source);
        Assert.Equal("Mod20", entries[4].Source);
    }

    [Fact]
    public async Task StreamLogsAsync_Streams_Newly_Appended_Entries()
    {
        // Arrange
        var logFile = Path.Combine(_tempDir, "LiveStream.log");
        await File.WriteAllTextAsync(logFile, "[Info: Init] Initial log line before stream\n");

        var adapter = new BepInExLogMonitorAdapter();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var stream = adapter.StreamLogsAsync(logFile, cts.Token);
        var streamEnumerator = stream.GetAsyncEnumerator(cts.Token);

        // Act: Start reading in task, and periodically append lines until readTask captures one
        var readTask = Task.Run(async () =>
        {
            if (await streamEnumerator.MoveNextAsync())
            {
                return streamEnumerator.Current;
            }
            return null;
        }, cts.Token);

        var writeTask = Task.Run(async () =>
        {
            await Task.Delay(100, cts.Token);
            while (!readTask.IsCompleted && !cts.IsCancellationRequested)
            {
                try
                {
                    using (var fs = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    using (var writer = new StreamWriter(fs))
                    {
                        await writer.WriteLineAsync("[Error: LiveMod] Dynamic error occurred during stream test");
                        await writer.WriteLineAsync("[Info: StreamDone] Flusher line");
                        await writer.FlushAsync(cts.Token);
                    }
                }
                catch (IOException)
                {
                    // Ignore transient file lock
                }
                await Task.Delay(150, cts.Token);
            }
        }, cts.Token);

        var entry = await readTask;

        // Assert
        Assert.NotNull(entry);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal("LiveMod", entry.Source);
        Assert.Equal("Dynamic error occurred during stream test", entry.Message);

        await streamEnumerator.DisposeAsync();
    }
}
