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

        DecodedImage image = ImageDecoder.Load(path);

        hasAlpha = image.HasAlpha;

        Texture texture = factory.CreateTexture(
            TextureDescription.Texture2D(
                (uint)image.Width,
                (uint)image.Height,
                1,
                1,
                PixelFormat.R8_G8_B8_A8_UNorm,
                TextureUsage.Sampled));

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

            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }
}