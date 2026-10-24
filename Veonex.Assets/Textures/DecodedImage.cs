namespace Veonex.Assets;

public sealed class DecodedImage
{
	public int Width { get; }

	public int Height { get; }

	public byte[] Pixels { get; }

	public bool HasAlpha { get; }

	internal DecodedImage(
		int width,
		int height,
		byte[] pixels)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
		ArgumentNullException.ThrowIfNull(pixels);

		int expectedLength = checked(width * height * 4);

		if (pixels.Length != expectedLength)
		{
			throw new ArgumentException(
				"Pixel data must contain RGBA values.",
				nameof(pixels));
		}

		Width = width;
		Height = height;
		Pixels = pixels;

		HasAlpha = DetectAlpha(pixels);
	}

	private static bool DetectAlpha(byte[] pixels)
	{
		for (int i = 3; i < pixels.Length; i += 4)
		{
			if (pixels[i] < byte.MaxValue)
				return true;
		}

		return false;
	}
}