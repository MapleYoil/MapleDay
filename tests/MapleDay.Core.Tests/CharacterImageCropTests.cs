using MapleDay.Core;

namespace MapleDay.Core.Tests;

public sealed class CharacterImageCropTests
{
    [Fact]
    public void ReferenceCharacterHasTwentyPixelMargins()
    {
        var pixels = new byte[300 * 300 * 4];
        pixels[(123 * 300 + 109) * 4 + 3] = 255;
        pixels[(202 * 300 + 176) * 4 + 3] = 1;
        var cropped = CharacterImageCrop.CropReferenceFrame(pixels, 300, 300);
        Assert.Equal(108, cropped.Width);
        Assert.Equal(120, cropped.Height);
        Assert.Equal(255, cropped.Pixels[(20 * 108 + 20) * 4 + 3]);
        Assert.Equal(1, cropped.Pixels[(99 * 108 + 87) * 4 + 3]);
    }

    [Fact]
    public void MountOutsideReferenceDoesNotMoveTheCharacter()
    {
        var normal = new byte[300 * 300 * 4];
        normal[(150 * 300 + 150) * 4] = 37;
        normal[(150 * 300 + 150) * 4 + 3] = 255;
        var riding = (byte[])normal.Clone();
        riding[(220 * 300 + 30) * 4 + 3] = 255;
        riding[(240 * 300 + 240) * 4 + 3] = 255;
        var normalCrop = CharacterImageCrop.CropReferenceFrame(normal, 300, 300);
        var ridingCrop = CharacterImageCrop.CropReferenceFrame(riding, 300, 300);
        Assert.Equal(normalCrop.Pixels, ridingCrop.Pixels);
        Assert.Equal(37, ridingCrop.Pixels[(47 * 108 + 61) * 4]);
    }

    [Fact]
    public void ImageSmallerThanReferenceFrameIsPaddedWithTransparentPixels()
    {
        var cropped = CharacterImageCrop.CropReferenceFrame(new byte[16], 2, 2);
        Assert.Equal(108, cropped.Width);
        Assert.Equal(120, cropped.Height);
        Assert.All(cropped.Pixels, pixel => Assert.Equal(0, pixel));
    }
}
