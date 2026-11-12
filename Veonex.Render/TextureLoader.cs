using NeoVeldrid;
using Veonex.Assets;

namespace Veonex.Render;

public static class TextureLoader
{
	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		string path)
	{
		return Load(factory, graphicsDevice, path, out _);
	}

	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		string path,
		out bool hasAlpha)
	{
		ArgumentNullException.ThrowIfNull(factory);
		ArgumentNullException.ThrowIfNull(graphicsDevice);

		Texture texture = LoadCore(
			factory,
			graphicsDevice,
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
		return Load(factory, graphicsDevice, commandList, path, out _, out _);
	}

	public static Texture Load(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		CommandList commandList,
		string path,
		out bool hasAlpha)
	{
		return Load(factory, graphicsDevice, commandList, path, out hasAlpha, out _);
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

		return LoadCore(
			factory,
			graphicsDevice,
			path,
			out hasAlpha,
			out mipLevels);
	}

	private static Texture LoadCore(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		string path,
		out bool hasAlpha,
		out int mipLevels)
	{
		string fullPath = Path.GetFullPath(path);
		DecodedImage image = ImageDecoder.Load(fullPath);

		hasAlpha = image.HasAlpha;

		return Ktx2.Load(
			factory,
			graphicsDevice,
			image,
			fullPath,
			out mipLevels);
	}
}
