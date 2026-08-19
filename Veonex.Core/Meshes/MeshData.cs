using Veonex.Mathematics;

namespace Veonex.Core;

public sealed class MeshData
{
    public DVector3[] Positions { get; }

    public DVector3[] Normals { get; }

    public DVector2[] UVs { get; }

    public uint[] Indices { get; }


    public int VertexCount
        => Positions.Length;


    public int IndexCount
        => Indices.Length;


    public bool HasNormals
        => Normals.Length > 0;


    public bool HasUVs
        => UVs.Length > 0;


    public MeshData(
        DVector3[] positions,
        uint[] indices,
        DVector3[]? normals = null,
        DVector2[]? uvs = null)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(indices);


        if (positions.Length == 0)
            throw new ArgumentException(
                "Mesh must contain positions.",
                nameof(positions));


        if (indices.Length == 0)
            throw new ArgumentException(
                "Mesh must contain indices.",
                nameof(indices));


        if (normals != null &&
            normals.Length != positions.Length)
        {
            throw new ArgumentException(
                "Normals count must match positions count.",
                nameof(normals));
        }


        if (uvs != null &&
            uvs.Length != positions.Length)
        {
            throw new ArgumentException(
                "UV count must match positions count.",
                nameof(uvs));
        }


        Positions = positions;

        Indices = indices;

        Normals = normals ?? [];

        UVs = uvs ?? [];
    }
}