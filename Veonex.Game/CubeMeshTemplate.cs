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
            new(0, 1),
	        new(1, 1),
	        new(1, 0),
	        new(0, 0),

            // Back
            new(0, 1),
	        new(1, 1),
	        new(1, 0),
	        new(0, 0),

            // Left
            new(0, 1),
	        new(1, 1),
	        new(1, 0),
	        new(0, 0),

            // Right
            new(0, 1),
	        new(1, 1),
	        new(1, 0),
	        new(0, 0),

            // Top
            new(0, 1),
	        new(1, 1),
	        new(1, 0),
	        new(0, 0),

            // Bottom
            new(0, 1),
	        new(1, 1),
	        new(1, 0),
	        new(0, 0)
        ];


		uint[] indices =
		[
			// Front (+Z)
			0, 2, 1,
	        0, 3, 2,

            // Back (-Z)
            4, 6, 5,
	        4, 7, 6,

            // Left (-X)
            8, 10, 9,
	        8, 11, 10,

            // Right (+X)
            12, 14, 13,
	        12, 15, 14,

            // Top (+Y)
            16, 18, 17,
	        16, 19, 18,

            // Bottom (-Y)
            20, 22, 21,
	        20, 23, 22
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