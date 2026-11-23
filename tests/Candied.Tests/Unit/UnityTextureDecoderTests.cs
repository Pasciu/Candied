using Candied.Decompiler.Internal.Assets;
using Xunit;

namespace Candied.Tests.Unit;

public class UnityTextureDecoderTests
{
    [Fact]
    public void RotateBgra90Clockwise_Rotates_Dimensions_And_Pixels_Correctly()
    {
        // 2 wide, 1 high (2 pixels): Pixel 0 (Red), Pixel 1 (Blue)
        int srcW = 2;
        int srcH = 1;
        byte[] src = new byte[]
        {
            0, 0, 255, 255, // Pixel (0,0) Red (B=0, G=0, R=255, A=255)
            255, 0, 0, 255  // Pixel (1,0) Blue (B=255, G=0, R=0, A=255)
        };

        var rotated = UnityTextureDecoder.RotateBgra90Clockwise(src, srcW, srcH);

        // Resulting dimensions: width = 1, height = 2
        Assert.Equal(8, rotated.Length);

        // In 90 deg clockwise:
        // (0,0) moves to (srcH - 1 - 0, 0) = (0, 0)
        // (1,0) moves to (srcH - 1 - 0, 1) = (0, 1)
        // Top pixel (0,0) should be Red
        Assert.Equal(255, rotated[2]); // R
        Assert.Equal(0, rotated[0]);   // B

        // Bottom pixel (0,1) should be Blue
        Assert.Equal(0, rotated[6]);   // R
        Assert.Equal(255, rotated[4]); // B
    }

    [Fact]
    public void CropSprite_Crops_Rectangular_Subregion()
    {
        // 4x4 texture with unique colors
        int texW = 4;
        int texH = 4;
        byte[] bgra = new byte[texW * texH * 4];

        // Fill pixel (1, 1) in bottom-left coords (Unity: row in raster is texH - (y + h) + r = 4 - (1 + 1) + 0 = 2)
        int rasterY = 2;
        int rasterX = 1;
        int idx = (rasterY * texW + rasterX) * 4;
        bgra[idx] = 123; // B
        bgra[idx + 1] = 45; // G
        bgra[idx + 2] = 67; // R
        bgra[idx + 3] = 255; // A

        // Crop 1x1 sprite at (1, 1)
        var cropped = UnityTextureDecoder.CropSprite(bgra, texW, texH, 1, 1, 1, 1, isPackedRotated: false);

        Assert.Equal(4, cropped.Length);
        Assert.Equal(123, cropped[0]);
        Assert.Equal(45, cropped[1]);
        Assert.Equal(67, cropped[2]);
        Assert.Equal(255, cropped[3]);
    }
}
