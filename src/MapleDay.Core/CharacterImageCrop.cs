namespace MapleDay.Core;

public sealed record CroppedCharacterImage(byte[] Pixels, int Width, int Height);

public static class CharacterImageCrop
{
    // Chocomarin occupies (109,123)-(176,202) on the original 300x300 canvas.
    // Use this one origin for every character, with 20 pixels around the reference.
    public const int OriginX = 89;
    public const int OriginY = 103;
    public const int FrameWidth = 108;
    public const int FrameHeight = 120;

    /// <summary>Crops the same original canvas coordinates for every character without resizing or recentering.</summary>
    public static CroppedCharacterImage CropReferenceFrame(byte[] pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (pixels.Length != checked(width * height * 4))
            throw new ArgumentException("Pixel buffer size does not match image dimensions.", nameof(pixels));

        var croppedPixels = new byte[FrameWidth * FrameHeight * 4];
        var copyWidth = Math.Clamp(width - OriginX, 0, FrameWidth);
        var copyHeight = Math.Clamp(height - OriginY, 0, FrameHeight);
        if (copyWidth == 0 || copyHeight == 0)
            return new CroppedCharacterImage(croppedPixels, FrameWidth, FrameHeight);
        for (var y = 0; y < copyHeight; y++)
        {
            Buffer.BlockCopy(pixels, ((OriginY + y) * width + OriginX) * 4,
                croppedPixels, y * FrameWidth * 4, copyWidth * 4);
        }
        return new CroppedCharacterImage(croppedPixels, FrameWidth, FrameHeight);
    }
}
