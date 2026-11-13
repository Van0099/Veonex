using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NeoVeldrid;
using Veonex.Render;

namespace Veonex.Render.TextureCompression;

internal readonly record struct GpuTextureMip(
    byte[] Pixels,
    int Width,
    int Height);

internal sealed class GpuTextureCompressor : IDisposable
{
    private readonly ResourceFactory _factory;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly ResourceLayout _resourceLayout;
    private readonly Shader _shader;
    private readonly Pipeline _pipeline;

    private bool _disposed;

    [StructLayout(LayoutKind.Sequential)]
    private struct EncodeParameters
    {
        public uint Width;
        public uint Height;
        public uint BlocksX;
        public uint BlocksY;
        public uint SourcePixelOffset;
        public uint OutputBlockOffset;
        public uint Padding0;
        public uint Padding1;
    }

    public GpuTextureCompressor(
        ResourceFactory factory,
        GraphicsDevice graphicsDevice,
        string shaderPath)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        if (string.IsNullOrWhiteSpace(shaderPath))
            throw new ArgumentException("Shader path cannot be empty.", nameof(shaderPath));

        if (!graphicsDevice.Features.ComputeShader)
            throw new NotSupportedException("The graphics device does not support compute shaders.");

        if (!graphicsDevice.Features.StructuredBuffer)
            throw new NotSupportedException("The graphics device does not support structured buffers.");

        _factory = factory;
        _graphicsDevice = graphicsDevice;

        Shader shader = ShaderLoader.LoadCompute(factory, shaderPath);
        ResourceLayout? layout = null;
        Pipeline? pipeline = null;

        try
        {
            layout = factory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription(
                        "SourcePixels",
                        ResourceKind.StructuredBufferReadOnly,
                        ShaderStages.Compute),
                    new ResourceLayoutElementDescription(
                        "CompressedBlocks",
                        ResourceKind.StructuredBufferReadWrite,
                        ShaderStages.Compute),
                    new ResourceLayoutElementDescription(
                        "EncodeParameters",
                        ResourceKind.UniformBuffer,
                        ShaderStages.Compute)));

            pipeline = factory.CreateComputePipeline(
                new ComputePipelineDescription(
                    shader,
                    layout!,
                    16,
                    1,
                    1));
        }
        catch
        {
            pipeline?.Dispose();
            layout?.Dispose();
            shader.Dispose();
            throw;
        }

        _shader = shader;
        _resourceLayout = layout!;
        _pipeline = pipeline!;
    }

    public byte[][] CompressMipChain(IReadOnlyList<GpuTextureMip> mipChain)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(mipChain);

        if (mipChain.Count == 0)
            throw new ArgumentException("Mip chain cannot be empty.", nameof(mipChain));

        int totalPixelCount = 0;
        int totalBlockCount = 0;

        int[] sourceOffsets = new int[mipChain.Count];
        int[] outputSizes = new int[mipChain.Count];
        EncodeParameters[] parameters = new EncodeParameters[mipChain.Count];

        for (int level = 0; level < mipChain.Count; level++)
        {
            GpuTextureMip mip = mipChain[level];

            ArgumentNullException.ThrowIfNull(mip.Pixels);

            if (mip.Width <= 0 || mip.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(mipChain), "Mip dimensions must be positive.");

            int expectedPixelBytes = checked(mip.Width * mip.Height * 4);
            if (mip.Pixels.Length != expectedPixelBytes)
            {
                throw new InvalidDataException(
                    $"Invalid RGBA8 data size for mip {level}. " +
                    $"Expected {expectedPixelBytes} bytes, received {mip.Pixels.Length}.");
            }

            int blocksX = checked((mip.Width + 3) / 4);
            int blocksY = checked((mip.Height + 3) / 4);
            int blockCount = checked(blocksX * blocksY);

            sourceOffsets[level] = totalPixelCount;
            outputSizes[level] = checked(blockCount * 16);

            parameters[level] = new EncodeParameters
            {
                Width = (uint)mip.Width,
                Height = (uint)mip.Height,
                BlocksX = (uint)blocksX,
                BlocksY = (uint)blocksY,
                SourcePixelOffset = (uint)totalPixelCount,
                OutputBlockOffset = (uint)totalBlockCount
            };

            totalPixelCount = checked(totalPixelCount + mip.Width * mip.Height);
            totalBlockCount = checked(totalBlockCount + blockCount);
        }

        uint[] packedSourcePixels = new uint[totalPixelCount];

        for (int level = 0; level < mipChain.Count; level++)
        {
            ReadOnlySpan<byte> source = mipChain[level].Pixels;
            int pixelCount = checked(mipChain[level].Width * mipChain[level].Height);
            int destinationOffset = sourceOffsets[level];

            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                packedSourcePixels[destinationOffset + pixel] =
                    BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(pixel * 4, 4));
            }
        }

        int outputByteCount = checked(totalBlockCount * 16);
        if (outputByteCount == 0)
            throw new InvalidOperationException("BC7 output buffer cannot be empty.");

        uint sourceBufferSize = checked((uint)(packedSourcePixels.Length * sizeof(uint)));
        uint outputBufferSize = checked((uint)outputByteCount);

        using DeviceBuffer sourceBuffer = _factory.CreateBuffer(
            new BufferDescription(
                sourceBufferSize,
                BufferUsage.StructuredBufferReadOnly,
                sizeof(uint)));

        using DeviceBuffer outputBuffer = _factory.CreateBuffer(
            new BufferDescription(
                outputBufferSize,
                BufferUsage.StructuredBufferReadWrite,
                16));

        using DeviceBuffer readbackBuffer = _factory.CreateBuffer(
            new BufferDescription(
                outputBufferSize,
                BufferUsage.Staging));

        using DeviceBuffer parametersBuffer = _factory.CreateBuffer(
            new BufferDescription(
                (uint)Marshal.SizeOf<EncodeParameters>(),
                BufferUsage.UniformBuffer));

        using ResourceSet resourceSet = _factory.CreateResourceSet(
            new ResourceSetDescription(
                _resourceLayout,
                sourceBuffer,
                outputBuffer,
                parametersBuffer));

        using CommandList commandList = _factory.CreateCommandList();

        commandList.Begin();
        commandList.UpdateBuffer(sourceBuffer, 0, packedSourcePixels);
        commandList.SetPipeline(_pipeline);
        commandList.SetComputeResourceSet(0, resourceSet);

        for (int level = 0; level < parameters.Length; level++)
        {
            EncodeParameters levelParameters = parameters[level];
            commandList.UpdateBuffer(
                parametersBuffer,
                0,
                ref levelParameters);

            uint groupCount = checked(
                levelParameters.BlocksX * levelParameters.BlocksY);

            commandList.Dispatch(groupCount, 1, 1);
        }

        commandList.CopyBuffer(
            outputBuffer,
            0,
            readbackBuffer,
            0,
            outputBufferSize);

        commandList.End();

        _graphicsDevice.SubmitCommands(commandList);
        _graphicsDevice.WaitForIdle();

        byte[] combinedOutput = new byte[outputByteCount];
        MappedResource mapped = _graphicsDevice.Map(readbackBuffer, MapMode.Read);

        try
        {
            Marshal.Copy(
                mapped.Data,
                combinedOutput,
                0,
                combinedOutput.Length);
        }
        finally
        {
            _graphicsDevice.Unmap(readbackBuffer);
        }

        byte[][] compressedMipLevels = new byte[mipChain.Count][];
        int outputByteOffset = 0;

        for (int level = 0; level < mipChain.Count; level++)
        {
            int mipOutputSize = outputSizes[level];
            byte[] mipOutput = new byte[mipOutputSize];

            Buffer.BlockCopy(
                combinedOutput,
                outputByteOffset,
                mipOutput,
                0,
                mipOutputSize);

            compressedMipLevels[level] = mipOutput;
            outputByteOffset = checked(outputByteOffset + mipOutputSize);
        }

        return compressedMipLevels;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _pipeline.Dispose();
        _shader.Dispose();
        _resourceLayout.Dispose();
    }
}
