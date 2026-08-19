using NeoVeldrid;

namespace Veonex.Render;

public sealed class MeshBuffer : IDisposable
{
    public DeviceBuffer PositionBuffer { get; }
    public DeviceBuffer? NormalBuffer { get; }
    public DeviceBuffer? UVBuffer { get; }
    public DeviceBuffer IndexBuffer { get; }


    public uint IndexCount { get; }


    public MeshBuffer(
        DeviceBuffer positionBuffer,
        DeviceBuffer indexBuffer,
        uint indexCount,
        DeviceBuffer? normalBuffer = null,
        DeviceBuffer? uvBuffer = null)
    {
        PositionBuffer = positionBuffer;

        IndexBuffer = indexBuffer;
        IndexCount = indexCount;

        NormalBuffer = normalBuffer;
        UVBuffer = uvBuffer;
    }


    public void Dispose()
    {
        PositionBuffer.Dispose();

        NormalBuffer?.Dispose();
        UVBuffer?.Dispose();

        IndexBuffer.Dispose();
    }
}