namespace Candied.Tests.Unit;

using Candied.VisionBridge.Protocol;
using Xunit;

public sealed class CoordinateConverterTests
{
    [Fact]
    public void UnityToImagePixelRect_ConvertsBottomLeftToTopLeftCorrectly()
    {
        // Unity screen: 1920x1080. Bottom-left origin.
        // A button at x=100, y=200, width=300, height=100 in Unity space.
        // In image space (top-left origin):
        // Image Y = screenHeight - (unityY + height) = 1080 - (200 + 100) = 780.
        int screenHeight = 1080;

        var rect = CoordinateConverter.UnityToImagePixelRect(100f, 200f, 300f, 100f, screenHeight);

        Assert.Equal(100f, rect.X);
        Assert.Equal(780f, rect.Y);
        Assert.Equal(300f, rect.Width);
        Assert.Equal(100f, rect.Height);
    }

    [Fact]
    public void ToNormalizedRect_CalculatesNormalizedBoundsBetween0And1()
    {
        int screenWidth = 1920;
        int screenHeight = 1080;
        var pixelRect = new VisionRectDto(192f, 108f, 960f, 540f);

        var normalized = CoordinateConverter.ToNormalizedRect(pixelRect, screenWidth, screenHeight);

        Assert.Equal(0.1f, normalized.X, 3);
        Assert.Equal(0.1f, normalized.Y, 3);
        Assert.Equal(0.5f, normalized.Width, 3);
        Assert.Equal(0.5f, normalized.Height, 3);
    }

    [Fact]
    public void ImageToUnityScreenPoint_InvertsTopLeftToBottomLeft()
    {
        int screenHeight = 1080;

        // Click at top-left image coord (150, 250)
        // Unity Y should be: 1080 - 250 = 830
        var unityPoint = CoordinateConverter.ImageToUnityScreenPoint(150f, 250f, screenHeight);

        Assert.Equal(150f, unityPoint.X);
        Assert.Equal(830f, unityPoint.Y);
    }
}
