using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using NeoVeldrid;
using Veonex.Assets;
using Veonex.Core.Logging;
using BcnPixelFormat = BCnEncoder.Encoder.PixelFormat;
using GraphicsPixelFormat = NeoVeldrid.PixelFormat;

namespace Veonex.Render;

internal static class TextureProcessor
{
	private const int Bc7BlockSize = 16;
	private const bool EnableBc7Compression = false;

	internal static Texture CreateTexture(
		ResourceFactory factory,
		GraphicsDevice graphicsDevice,
		DecodedImage image,
		string sourceName,
		out int mipLevels)
	{
		ArgumentNullException.ThrowIfNull(factory);
		ArgumentNullException.ThrowIfNull(graphicsDevice);
		ArgumentNullException.ThrowIfNull(image);

		bool supportsBc7 = EnableBc7Compression &&
			graphicsDevice.GetPixelFormatSupport(
				GraphicsPixelFormat.BC7_UNorm,
				TextureType.Texture2D,
				TextureUsage.Sampled);

		GraphicsPixelFormat pixelFormat = supportsBc7
			? GraphicsPixelFormat.BC7_UNorm
			: GraphicsPixelFormat.R8_G8_B8_A8_UNorm;

		if (!supportsBc7)
		{
			bool supportsRgba8 = graphicsDevice.GetPixelFormatSupport(
				GraphicsPixelFormat.R8_G8_B8_A8_UNorm,
				TextureType.Texture2D,
				TextureUsage.Sampled);

			if (!supportsRgba8)
			{
				throw new NotSupportedException(
					"The graphics device does not support BC7 or RGBA8 sampled textures.");
			}

			Logger.Warning(
				"Textures",
				$"BC7 is not supported for '{Path.GetFileName(sourceName)}'. Using RGBA8 UNorm.");
		}

		mipLevels = CalculateMipLevels(
			image.Width,
			image.Height);

		Texture texture = factory.CreateTexture(
			TextureDescription.Texture2D(
				(uint)image.Width,
				(uint)image.Height,
				(uint)mipLevels,
				1,
				pixelFormat,
				TextureUsage.Sampled));

		try
		{
			BcEncoder? encoder = null;

			if (supportsBc7)
			{
				encoder = new BcEncoder(CompressionFormat.Bc7);
				encoder.OutputOptions.GenerateMipMaps = false;
				encoder.OutputOptions.Quality = CompressionQuality.Balanced;
			}

			byte[] pixels = image.Pixels;

			int width = image.Width;
			int height = image.Height;

			for (int level = 0; level < mipLevels; level++)
			{
				byte[] uploadData;

				if (encoder is not null)
				{
					byte[][] encodedLevels = encoder.EncodeToRawBytes(
						pixels,
						width,
						height,
						BcnPixelFormat.Rgba32);

					if (encodedLevels.Length != 1)
					{
						throw new InvalidOperationException(
							"BCnEncoder returned an unexpected number of mip levels.");
					}

					uploadData = encodedLevels[0];

					int expectedSize = checked(
						((width + 3) / 4) *
						((height + 3) / 4) *
						Bc7BlockSize);

					if (uploadData.Length != expectedSize)
					{
						throw new InvalidOperationException(
							$"Invalid BC7 data size at mip {level}. " +
							$"Expected {expectedSize} bytes, received {uploadData.Length}.");
					}
				}
				else
				{
					uploadData = pixels;

					int expectedSize = checked(width * height * 4);

					if (uploadData.Length != expectedSize)
					{
						throw new InvalidOperationException(
							$"Invalid RGBA8 data size at mip {level}.");
					}
				}

				graphicsDevice.UpdateTexture(
					texture,
					uploadData,
					0,
					0,
					0,
					(uint)width,
					(uint)height,
					1,
					(uint)level,
					0);

				if (level + 1 >= mipLevels)
					break;

				int nextWidth = Math.Max(1, width / 2);
				int nextHeight = Math.Max(1, height / 2);

				pixels = DownsampleUnorm(
					pixels,
					width,
					height,
					nextWidth,
					nextHeight);

				width = nextWidth;
				height = nextHeight;
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

	private static byte[] DownsampleUnorm(
		byte[] source,
		int sourceWidth,
		int sourceHeight,
		int destinationWidth,
		int destinationHeight)
	{
		byte[] destination = new byte[
			checked(destinationWidth * destinationHeight * 4)];

		for (int destinationY = 0; destinationY < destinationHeight; destinationY++)
		{
			double sourceY0 =
				(double)destinationY * sourceHeight / destinationHeight;

			double sourceY1 =
				(double)(destinationY + 1) * sourceHeight / destinationHeight;

			int startY = (int)Math.Floor(sourceY0);
			int endY = Math.Min(
				sourceHeight,
				(int)Math.Ceiling(sourceY1));

			for (int destinationX = 0; destinationX < destinationWidth; destinationX++)
			{
				double sourceX0 =
					(double)destinationX * sourceWidth / destinationWidth;

				double sourceX1 =
					(double)(destinationX + 1) * sourceWidth / destinationWidth;

				int startX = (int)Math.Floor(sourceX0);
				int endX = Math.Min(
					sourceWidth,
					(int)Math.Ceiling(sourceX1));

				double totalWeight = 0.0;
				double alphaWeight = 0.0;

				double red = 0.0;
				double green = 0.0;
				double blue = 0.0;

				for (int sourceY = startY; sourceY < endY; sourceY++)
				{
					double weightY =
						Math.Min(sourceY1, sourceY + 1.0) -
						Math.Max(sourceY0, sourceY);

					for (int sourceX = startX; sourceX < endX; sourceX++)
					{
						double weightX =
							Math.Min(sourceX1, sourceX + 1.0) -
							Math.Max(sourceX0, sourceX);

						double weight = weightX * weightY;

						int sourceIndex =
							(sourceY * sourceWidth + sourceX) * 4;

						double alpha = source[sourceIndex + 3] / 255.0;

						totalWeight += weight;
						alphaWeight += alpha * weight;

						red += source[sourceIndex] * alpha * weight;
						green += source[sourceIndex + 1] * alpha * weight;
						blue += source[sourceIndex + 2] * alpha * weight;
					}
				}

				int destinationIndex =
					(destinationY * destinationWidth + destinationX) * 4;

				if (alphaWeight > 1e-12)
				{
					destination[destinationIndex] =
						ToByte(red / alphaWeight);

					destination[destinationIndex + 1] =
						ToByte(green / alphaWeight);

					destination[destinationIndex + 2] =
						ToByte(blue / alphaWeight);
				}
				else
				{
					destination[destinationIndex] = 0;
					destination[destinationIndex + 1] = 0;
					destination[destinationIndex + 2] = 0;
				}

				destination[destinationIndex + 3] =
					ToByte(alphaWeight / totalWeight * 255.0);
			}
		}

		return destination;
	}

	private static byte ToByte(double value)
	{
		return (byte)Math.Clamp(
			(int)Math.Round(value),
			0,
			255);
	}
}