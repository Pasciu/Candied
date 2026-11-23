using System;
using System.IO;
using Candied.Core.Analysis;
using Xunit;

namespace Candied.Tests.Unit;

public sealed class PurePngDecoderTests
{
    private static readonly string ArtifactDir =
        Environment.GetEnvironmentVariable("CANDIED_TEST_ARTIFACT_DIR")
        ?? Path.Combine(Path.GetTempPath(), "candied-test-artifacts");

    [Fact]
    public void CalculateDiffPercentage_WithIdenticalBytes_ReturnsZero()
    {
        byte[] dummyPng = new byte[64];
        dummyPng[0] = 0x89; dummyPng[1] = 0x50; dummyPng[2] = 0x4E; dummyPng[3] = 0x47;
        dummyPng[4] = 0x0D; dummyPng[5] = 0x0A; dummyPng[6] = 0x1A; dummyPng[7] = 0x0A;

        double diff = PurePngDecoder.CalculateDiffPercentage(dummyPng, dummyPng);
        Assert.Equal(0.0, diff);
    }

    [Fact]
    public void CalculateDiffPercentage_WithRealArtifactScreenshots_CalculatesExpectedDiff()
    {
        string p1 = Path.Combine(ArtifactDir, "screen_01_baseline_stable.png");
        string p2 = Path.Combine(ArtifactDir, "screen_02_negative_control.png");

        if (!File.Exists(p1) || !File.Exists(p2))
        {
            // Skip if artifacts not in current environment
            return;
        }

        byte[] bytes1 = File.ReadAllBytes(p1);
        byte[] bytes2 = File.ReadAllBytes(p2);

        double diff = PurePngDecoder.CalculateDiffPercentage(bytes1, bytes2);
        // Negative control diff was < 0.1%
        Assert.True(diff < 1.0, $"Expected diff < 1.0%, got {diff:F2}%");
    }
}
