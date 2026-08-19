using System.Text;
using NeoVeldrid;
using NeoVeldrid.SPIRV;

namespace Veonex.Render;

public static class ShaderLoader
{
    public static Shader[] Load(
        ResourceFactory factory,
        string path)
    {
        if (factory == null)
            throw new ArgumentNullException(nameof(factory));

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException(
                "Shader path cannot be empty.",
                nameof(path));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Shader file was not found: {path}",
                path);
        }

        string source =
            File.ReadAllText(path);

        const string vertexMarker = "###VERT";
        const string fragmentMarker = "###FRAG";

        int vertexIndex =
            source.IndexOf(
                vertexMarker,
                StringComparison.Ordinal);

        int fragmentIndex =
            source.IndexOf(
                fragmentMarker,
                StringComparison.Ordinal);

        if (vertexIndex < 0)
        {
            throw new InvalidDataException(
                $"Shader '{path}' does not contain {vertexMarker}.");
        }

        if (fragmentIndex < 0)
        {
            throw new InvalidDataException(
                $"Shader '{path}' does not contain {fragmentMarker}.");
        }

        if (fragmentIndex <= vertexIndex)
        {
            throw new InvalidDataException(
                $"Shader '{path}' has invalid section order. " +
                $"{vertexMarker} must come before {fragmentMarker}.");
        }

        string vertexSource =
            source[
                (vertexIndex + vertexMarker.Length)..fragmentIndex]
            .Trim();

        string fragmentSource =
            source[
                (fragmentIndex + fragmentMarker.Length)..]
            .Trim();

        if (vertexSource.Length == 0)
        {
            throw new InvalidDataException(
                $"Shader '{path}' has an empty vertex shader.");
        }

        if (fragmentSource.Length == 0)
        {
            throw new InvalidDataException(
                $"Shader '{path}' has an empty fragment shader.");
        }

        ShaderDescription vertexShader =
            new(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(vertexSource),
                "main");

        ShaderDescription fragmentShader =
            new(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(fragmentSource),
                "main");

        return factory.CreateFromSpirv(
            vertexShader,
            fragmentShader);
    }
}