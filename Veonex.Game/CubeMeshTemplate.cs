// cube mesh template

using Veonex.Mathematics;
using Veonex.Core;

namespace Veonex.Game;

public static class CubeMeshTemplate
{
    public static Mesh Create()
    {
        DVector3[] positions =
        [
            // Front
            new(-1, -1,  1),
            new( 1, -1,  1),
            new( 1,  1,  1),
            new(-1,  1,  1),

            // Back
            new( 1, -1, -1),
            new(-1, -1, -1),
            new(-1,  1, -1),
            new( 1,  1, -1),

            // Left
            new(-1, -1, -1),
            new(-1, -1,  1),
            new(-1,  1,  1),
            new(-1,  1, -1),

            // Right
            new( 1, -1,  1),
            new( 1, -1, -1),
            new( 1,  1, -1),
            new( 1,  1,  1),

            // Top
            new(-1,  1,  1),
            new( 1,  1,  1),
            new( 1,  1, -1),
            new(-1,  1, -1),

            // Bottom
            new(-1, -1, -1),
            new( 1, -1, -1),
            new( 1, -1,  1),
            new(-1, -1,  1)
        ];

        DVector3[] normals =
        [
            // Front
            new( 0,  0,  1),
            new( 0,  0,  1),
            new( 0,  0,  1),
            new( 0,  0,  1),

            // Back
            new( 0,  0, -1),
            new( 0,  0, -1),
            new( 0,  0, -1),
            new( 0,  0, -1),

            // Left
            new(-1,  0,  0),
            new(-1,  0,  0),
            new(-1,  0,  0),
            new(-1,  0,  0),

            // Right
            new( 1,  0,  0),
            new( 1,  0,  0),
            new( 1,  0,  0),
            new( 1,  0,  0),

            // Top
            new( 0,  1,  0),
            new( 0,  1,  0),
            new( 0,  1,  0),
            new( 0,  1,  0),

            // Bottom
            new( 0, -1,  0),
            new( 0, -1,  0),
            new( 0, -1,  0),
            new( 0, -1,  0)
        ];

        DVector2[] uvs =
        [
            // Front
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1),

            // Back
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1),

            // Left
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1),

            // Right
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1),

            // Top
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1),

            // Bottom
            new(0, 0),
            new(1, 0),
            new(1, 1),
            new(0, 1)
        ];

        uint[] indices =
        [
            // Front
            0, 1, 2,
            0, 2, 3,

            // Back
            4, 5, 6,
            4, 6, 7,

            // Left
            8, 9, 10,
            8, 10, 11,

            // Right
            12, 13, 14,
            12, 14, 15,

            // Top
            16, 17, 18,
            16, 18, 19,

            // Bottom
            20, 21, 22,
            20, 22, 23
        ];

        MeshData data =
            new(
                positions,
                indices,
                normals,
                uvs);

        return new Veonex.Core.Mesh(
            "Cube",
            data);
    }
}