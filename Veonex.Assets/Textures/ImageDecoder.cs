using StbImageSharp;

namespace Veonex.Assets;

public static class ImageDecoder
{
	public static DecodedImage Load(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException(
				"Image path cannot be empty.",
				nameof(path));
		}

		string fullPath = Path.GetFullPath(path);
		string extension = Path.GetExtension(fullPath);

		if (!IsSupportedExtension(extension))
		{
			throw new NotSupportedException(
				$"Image format '{extension}' is not supported.");
		}

		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException(
				"Image file was not found.",
				fullPath);
		}

		ImageResult image;

		using (FileStream stream = File.OpenRead(fullPath))
		{
			image = ImageResult.FromStream(
				stream,
				ColorComponents.RedGreenBlueAlpha);
		}

		return new DecodedImage(
			image.Width,
			image.Height,
			image.Data);
	}

	private static bool IsSupportedExtension(string extension)
	{
		return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".tga", StringComparison.OrdinalIgnoreCase);
	}
}