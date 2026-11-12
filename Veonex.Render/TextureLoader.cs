using NeoVeldrid;
using Veonex.Assets;
using Veonex.Core.Logging;

namespace Veonex.Render;

public static class TextureLoader
{
	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		string path)
	{
		return Load(
			factory,
			graphicsDevice,
			path,
			out _);
	}

	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		string path,
		out bool hasAlpha)
	{
		ArgumentNullException.ThrowIfNull(factory);
		ArgumentNullException.ThrowIfNull(graphicsDevice);

		using CommandList commandList = factory.CreateCommandList();

		Texture texture = Load(
			factory,
			graphicsDevice,
			commandList,
			path,
			out hasAlpha,
			out _);

		graphicsDevice.WaitForIdle();
		return texture;
	}

	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		CommandList commandList,
		string path)
	{
		return Load(
			factory,
			graphicsDevice,
			commandList,
			path,
			out _,
			out _);
	}

	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		CommandList commandList,
		string path,
		out bool hasAlpha)
	{
		return Load(
			factory,
			graphicsDevice,
			commandList,
			path,
			out hasAlpha,
			out _);
	}

	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		CommandList commandList,
		string path,
		out bool hasAlpha,
		out int mipLevels)
	{
		ArgumentNullException.ThrowIfNull(factory);
		ArgumentNullException.ThrowIfNull(graphicsDevice);
		ArgumentNullException.ThrowIfNull(commandList);

		DecodedImage image = ImageDecoder.Load(path);

		hasAlpha = image.HasAlpha;

		int requestedMipLevels = CalculateMipLevels(
			image.Width,
			image.Height);

		bool generateMipmaps = requestedMipLevels > 1 &&
			graphicsDevice.GetPixelFormatSupport(
				PixelFormat.R8_G8_B8_A8_UNorm,
				TextureType.Texture2D,
				TextureUsage.Sampled | TextureUsage.GenerateMipmaps);

		mipLevels = generateMipmaps
			? requestedMipLevels
			: 1;

		TextureUsage usage = TextureUsage.Sampled;

		if (generateMipmaps)
			usage |= TextureUsage.GenerateMipmaps;
		else if (requestedMipLevels > 1)
			Logger.Warning(
				"Textures",
				$"GPU mipmap generation is not supported for '{Path.GetFileName(path)}'. Using one mip level.");

		Texture texture = factory.CreateTexture(
			TextureDescription.Texture2D(
				(uint)image.Width,
				(uint)image.Height,
				(uint)mipLevels,
				1,
				PixelFormat.R8_G8_B8_A8_UNorm,
				usage));

		try
		{
			graphicsDevice.UpdateTexture(
				texture,
				image.Pixels,
				0,
				0,
				0,
				(uint)image.Width,
				(uint)image.Height,
				1,
				0,
				0);

			if (generateMipmaps)
			{
				commandList.Begin();
				commandList.GenerateMipmaps(texture);
				commandList.End();
				graphicsDevice.SubmitCommands(commandList);
			}

			return texture;
		}
		catch
		{
			texture.Dispose();
			throw;
		}
	}

	private static int CalculateMipLevels(int width, int height)
	{
		if (width <= 0)
			throw new ArgumentOutOfRangeException(nameof(width));

		if (height <= 0)
			throw new ArgumentOutOfRangeException(nameof(height));

		int dimension = Math.Max(width, height);
		int mipLevels = 1;

		while (dimension > 1)
		{
			dimension >>= 1;
			mipLevels++;
		}

		return mipLevels;
	}
}
