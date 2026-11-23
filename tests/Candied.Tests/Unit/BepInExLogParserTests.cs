using Candied.Core.Models;
using Candied.Workspace.Parsing;
using Xunit;

namespace Candied.Tests.Unit;

public class BepInExLogParserTests
{
    [Fact]
    public void ParseLines_Parses_Standard_Headers_And_Levels()
    {
        var lines = new[]
        {
            "[Message:   BepInEx] Chainloader ready",
            "[Info   :TestPlugin] Plugin initialized successfully.",
            "[Warning: Unity] High memory usage detected.",
            "[Error  :TestPlugin] NullReferenceException occurred."
        };

        var entries = BepInExLogParser.ParseLines(lines);

        Assert.Equal(4, entries.Count);
        Assert.Equal(LogLevel.Info, entries[0].Level);
        Assert.Equal("BepInEx", entries[0].Source);
        Assert.Equal("Chainloader ready", entries[0].Message);

        Assert.Equal(LogLevel.Info, entries[1].Level);
        Assert.Equal("TestPlugin", entries[1].Source);

        Assert.Equal(LogLevel.Warning, entries[2].Level);
        Assert.Equal(LogLevel.Error, entries[3].Level);
    }

    [Fact]
    public void ParseLines_Stitches_Multiline_StackTraces()
    {
        var lines = new[]
        {
            "[Error  :TestPlugin] System.NullReferenceException: Object reference not set",
            "  at TestPlugin.Plugin.Update () [0x00000] in <filename unknown>:0",
            "  at UnityEngine.Component.CallUpdate () [0x00000] in <filename unknown>:0",
            "[Message:   BepInEx] Normal log continues"
        };

        var entries = BepInExLogParser.ParseLines(lines);

        Assert.Equal(2, entries.Count);
        Assert.Equal(LogLevel.Error, entries[0].Level);
        Assert.NotNull(entries[0].StackTrace);
        Assert.Contains("TestPlugin.Plugin.Update", entries[0].StackTrace);
        Assert.Equal("Normal log continues", entries[1].Message);
    }

    [Fact]
    public void ParseLines_Parses_Timestamps_Correctly()
    {
        var lines = new[]
        {
            "[12:34:56][Info   :MyPlugin] Log with short time",
            "[2026-11-02 10:15:30.123][Error  :MyPlugin] Log with full ISO date"
        };

        var entries = BepInExLogParser.ParseLines(lines);

        Assert.Equal(2, entries.Count);
        Assert.Equal(12, entries[0].Timestamp.Hour);
        Assert.Equal(34, entries[0].Timestamp.Minute);
        Assert.Equal(2026, entries[1].Timestamp.Year);
        Assert.Equal(11, entries[1].Timestamp.Month);
    }
}
