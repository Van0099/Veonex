using Assimp;
using Veonex.Core;
using Veonex.Mathematics;

using CoreMesh = Veonex.Core.Mesh;

namespace Veonex.Assets;

public static class ModelLoader
{
	public static IReadOnlyList<CoreMesh> Load(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			throw new ArgumentException(
				"Model path cannot be empty.",
				nameof(path));
		}

		string fullPath = Path.GetFullPath(path);

		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException(
				"Model file was not found.",
				fullPath);
		}

		using AssimpContext importer = new();

		Assimp.Scene scene = importer.ImportFile(
			fullPath,
			PostProcessSteps.Triangulate
			| PostProcessSteps.GenerateSmoothNormals
			| PostProcessSteps.PreTransformVertices
			| PostProcessSteps.ValidateDataStructure);

		List<CoreMesh> meshes = [];

		string modelName = Path.GetFileNameWithoutExtension(fullPath);

		for (int meshIndex = 0; meshIndex < scene.Meshes.Count; meshIndex++)
		{
			Assimp.Mesh source = scene.Meshes[meshIndex];

			if (source.VertexCount == 0 || source.FaceCount == 0)
				continue;

			DVector3[] positions = new DVector3[source.VertexCount];

			for (int i = 0; i < source.VertexCount; i++)
			{
				var vertex = source.Vertices[i];

				positions[i] = new DVector3(
					vertex.X,
					vertex.Y,
					vertex.Z);
			}

			DVector3[]? normals = null;

			if (source.HasNormals)
			{
				normals = new DVector3[source.VertexCount];

				for (int i = 0; i < source.VertexCount; i++)
				{
					var normal = source.Normals[i];

					normals[i] = new DVector3(
						normal.X,
						normal.Y,
						normal.Z);
				}
			}

			DVector2[]? uvs = null;

			if (source.HasTextureCoords(0))
			{
				uvs = new DVector2[source.VertexCount];

				for (int i = 0; i < source.VertexCount; i++)
				{
					var uv = source.TextureCoordinateChannels[0][i];

					uvs[i] = new DVector2(
						uv.X,
						uv.Y);
				}
			}

			List<uint> indices = [];

			foreach (Assimp.Face face in source.Faces)
			{
				if (face.Indices.Count < 3)
					continue;

				int first = face.Indices[0];

				for (int i = 1; i < face.Indices.Count - 1; i++)
				{
					indices.Add((uint)first);
					indices.Add((uint)face.Indices[i + 1]);
					indices.Add((uint)face.Indices[i]);
				}
			}

			if (indices.Count == 0)
				continue;

			string meshName = string.IsNullOrWhiteSpace(source.Name)
				? $"{modelName}_{meshIndex}"
				: source.Name;

			MeshData data = new(
				positions,
				indices.ToArray(),
				normals,
				uvs);

			meshes.Add(new CoreMesh(meshName, data));
		}

		if (meshes.Count == 0)
		{
			throw new InvalidDataException(
				$"No triangle meshes were found in '{fullPath}'.");
		}

		return meshes;
	}
}