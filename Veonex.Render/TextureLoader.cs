using NeoVeldrid;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Veonex.Render;

public static class TextureLoader
{
	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		string path)
	{
		if (string.IsNullOrWhiteSpace(path))
			throw new ArgumentException(
				"Texture path cannot be empty.",
				nameof(path));

		if (!File.Exists(path))
			throw new FileNotFoundException(
				"Texture file was not found.",
				path);

		if (!string.Equals(
				Path.GetExtension(path),
				".png",
				StringComparison.OrdinalIgnoreCase))
		{
			throw new NotSupportedException(
				"Only PNG textures are supported.");
		}

		using Image<Rgba32> image =
			Image.Load<Rgba32>(path);

		byte[] pixels =
			new byte[image.Width * image.Height * 4];

		image.CopyPixelDataTo(pixels);

		Texture texture =
			factory.CreateTexture(
				TextureDescription.Texture2D(
					(uint)image.Width,
					(uint)image.Height,
					1,
					1,
					PixelFormat.R8_G8_B8_A8_UNorm,
					TextureUsage.Sampled));

		graphicsDevice.UpdateTexture(
			texture,
			pixels,
			0,
			0,
			0,
			(uint)image.Width,
			(uint)image.Height,
			1,
			0,
			0);

		return texture;
	}
}