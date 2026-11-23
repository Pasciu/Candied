using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Candied.Tests.Helpers;
using Xunit;

namespace Candied.Tests.Unit;

public class PixelDiffEvaluatorTests
{
    private static byte[] CreateSolidPng(int width, int height, Color color)
    {
        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, 0, 0, width, height);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void Evaluate_IdenticalImages_ReturnsZeroPercent()
    {
        var bytes = CreateSolidPng(100, 100, Color.Beige);
        var res = PixelDiffEvaluator.Evaluate(bytes, bytes, new Rectangle(10, 10, 50, 50), requiredPercentage: 15.0);

        Assert.Equal(0.0, res.DiffPercentage);
        Assert.False(res.MeetsCriterion);
        Assert.Equal(2500, res.TotalPixelsInRegion);
        Assert.Equal(0, res.ChangedPixels);
    }

    [Fact]
    public void Evaluate_CompletelyDifferentImages_ReturnsHundredPercent()
    {
        var pre = CreateSolidPng(100, 100, Color.White);
        var post = CreateSolidPng(100, 100, Color.Black);
        var res = PixelDiffEvaluator.Evaluate(pre, post, new Rectangle(0, 0, 100, 100), requiredPercentage: 15.0);

        Assert.Equal(100.0, res.DiffPercentage);
        Assert.True(res.MeetsCriterion);
        Assert.Equal(10000, res.TotalPixelsInRegion);
        Assert.Equal(10000, res.ChangedPixels);
    }
}
