using NeoVeldrid;
using Veonex.Assets;
using Veonex.Core.Logging;
using Veonex.Render.TextureCompression;
using GraphicsPixelFormat = NeoVeldrid.PixelFormat;

namespace Veonex.Render;

internal static class TextureProcessor
{
    private const int Bc7BlockSize = 16;
    private const bool EnableBc7Compression = true;

    internal static Texture CreateTexture(
        ResourceFactory factory,
        GraphicsDevice graphicsDevice,
        DecodedImage image,
        string sourceName,
        GpuTextureCompressor? gpuTextureCompressor,
        out int mipLevels)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(graphicsDevice);
        ArgumentNullException.ThrowIfNull(image);

        bool supportsBc7 = EnableBc7Compression &&
            gpuTextureCompressor is not null &&
            graphicsDevice.GetPixelFormatSupport(
                GraphicsPixelFormat.BC7_UNorm,
                TextureType.Texture2D,
                TextureUsage.Sampled);

        bool supportsRgba8 = graphicsDevice.GetPixelFormatSupport(
            GraphicsPixelFormat.R8_G8_B8_A8_UNorm,
            TextureType.Texture2D,
            TextureUsage.Sampled);

        if (!supportsBc7 && !supportsRgba8)
        {
            throw new NotSupportedException(
                "The graphics device does not support BC7 sampled textures or RGBA8 sampled textures.");
        }

        if (!supportsBc7)
        {
            Logger.Warning(
                "Textures",
                $"GPU BC7 compression is unavailable for '{Path.GetFileName(sourceName)}'. Using RGBA8 UNorm.");
        }

        mipLevels = CalculateMipLevels(image.Width, image.Height);

        GpuTextureMip[] mipChain = BuildMipChain(
            image.Pixels,
            image.Width,
            image.Height,
            mipLevels);

        byte[][] uploadLevels;

        if (supportsBc7)
        {
            try
            {
                uploadLevels = gpuTextureCompressor!.CompressMipChain(mipChain);
            }
            catch (Exception ex)
            {
                if (!supportsRgba8)
                    throw new NotSupportedException(
                        $"GPU BC7 compression failed for '{sourceName}', and RGBA8 fallback is unavailable.",
                        ex);

                Logger.Warning(
                    "Textures",
                    $"GPU BC7 compression failed for '{Path.GetFileName(sourceName)}'. Using RGBA8 UNorm: {ex.Message}",
                    ex);

                supportsBc7 = false;
                uploadLevels = GetUncompressedMipLevels(mipChain);
            }
        }
        else
        {
            uploadLevels = GetUncompressedMipLevels(mipChain);
        }

        GraphicsPixelFormat pixelFormat = supportsBc7
            ? GraphicsPixelFormat.BC7_UNorm
            : GraphicsPixelFormat.R8_G8_B8_A8_UNorm;

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
            for (int level = 0; level < mipLevels; level++)
            {
                GpuTextureMip mip = mipChain[level];
                byte[] uploadData = uploadLevels[level];

                int expectedSize = supportsBc7
                    ? checked(
                        ((mip.Width + 3) / 4) *
                        ((mip.Height + 3) / 4) *
                        Bc7BlockSize)
                    : checked(mip.Width * mip.Height * 4);

                if (uploadData.Length != expectedSize)
                {
                    throw new InvalidDataException(
                        $"Invalid {(supportsBc7 ? "BC7" : "RGBA8")} data size at mip {level}. " +
                        $"Expected {expectedSize} bytes, received {uploadData.Length}.");
                }

                graphicsDevice.UpdateTexture(
                    texture,
                    uploadData,
                    0,
                    0,
                    0,
                    (uint)mip.Width,
                    (uint)mip.Height,
                    1,
                    (uint)level,
                    0);
            }

            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    private static GpuTextureMip[] BuildMipChain(
        byte[] basePixels,
        int baseWidth,
        int baseHeight,
        int mipLevels)
    {
        ArgumentNullException.ThrowIfNull(basePixels);

        GpuTextureMip[] mipChain = new GpuTextureMip[mipLevels];

        byte[] pixels = basePixels;
        int width = baseWidth;
        int height = baseHeight;

        for (int level = 0; level < mipLevels; level++)
        {
            int expectedSize = checked(width * height * 4);
            if (pixels.Length != expectedSize)
            {
                throw new InvalidDataException(
                    $"Invalid RGBA8 source data at mip {level}. " +
                    $"Expected {expectedSize} bytes, received {pixels.Length}.");
            }

            mipChain[level] = new GpuTextureMip(pixels, width, height);

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

        return mipChain;
    }

    private static byte[][] GetUncompressedMipLevels(GpuTextureMip[] mipChain)
    {
        byte[][] levels = new byte[mipChain.Length][];

        for (int level = 0; level < mipChain.Length; level++)
            levels[level] = mipChain[level].Pixels;

        return levels;
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
                    destination[destinationIndex] = ToByte(red / alphaWeight);
                    destination[destinationIndex + 1] = ToByte(green / alphaWeight);
                    destination[destinationIndex + 2] = ToByte(blue / alphaWeight);
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
