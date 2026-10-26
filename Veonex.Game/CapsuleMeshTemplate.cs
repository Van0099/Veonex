// capsule mesh template

using Veonex.Mathematics;
using Veonex.Core;

namespace Veonex.Game;

public static class CapsuleMeshTemplate
{
	public static Mesh Create(
		double radius = 0.5,
		double height = 2.0,
		int segments = 24,
		int ringsPerHemisphere = 8,
		int cylinderSegments = 1)
	{
		if (!double.IsFinite(radius) || radius <= 0.0)
			throw new ArgumentOutOfRangeException(nameof(radius));

		if (!double.IsFinite(height) || height < radius * 2.0)
			throw new ArgumentOutOfRangeException(
				nameof(height),
				"Height must be at least 2 * radius.");

		if (segments < 3)
			throw new ArgumentOutOfRangeException(nameof(segments));

		if (ringsPerHemisphere < 1)
			throw new ArgumentOutOfRangeException(nameof(ringsPerHemisphere));

		if (cylinderSegments < 0)
			throw new ArgumentOutOfRangeException(nameof(cylinderSegments));

		double halfHeight = height * 0.5;
		double cylinderHalf = halfHeight - radius;

		var positions = new List<DVector3>();
		var normals = new List<DVector3>();
		var uvs = new List<DVector2>();
		var indices = new List<uint>();

		for (int ring = 0; ring <= ringsPerHemisphere; ring++)
		{
			double phi = (Math.PI * 0.5) * ring / ringsPerHemisphere;
			double sinPhi = Math.Sin(phi);
			double cosPhi = Math.Cos(phi);

			for (int seg = 0; seg <= segments; seg++)
			{
				double theta = 2.0 * Math.PI * seg / segments;
				double sinTheta = Math.Sin(theta);
				double cosTheta = Math.Cos(theta);

				// Unit sphere point (normal).
				var normal = new DVector3(
					sinPhi * cosTheta,
					cosPhi,
					sinPhi * sinTheta);

				positions.Add(new DVector3(
					normal.X * radius,
					cylinderHalf + normal.Y * radius,
					normal.Z * radius));

				normals.Add(normal);

				uvs.Add(new DVector2(
					(double)seg / segments,
					0.5 - (double)ring / (ringsPerHemisphere * 2.0) - 0.25));
			}
		}

		for (int cyl = 1; cyl <= cylinderSegments; cyl++)
		{
			double t = (double)cyl / cylinderSegments;

			for (int seg = 0; seg <= segments; seg++)
			{
				double theta = 2.0 * Math.PI * seg / segments;
				double sinTheta = Math.Sin(theta);
				double cosTheta = Math.Cos(theta);

				var normal = new DVector3(cosTheta, 0.0, sinTheta);

				positions.Add(new DVector3(
					normal.X * radius,
					cylinderHalf - t * (cylinderHalf * 2.0),
					normal.Z * radius));

				normals.Add(normal);

				uvs.Add(new DVector2(
					(double)seg / segments,
					0.5 - t * 0.5));
			}
		}

		for (int ring = 1; ring <= ringsPerHemisphere; ring++)
		{
			double phi = (Math.PI * 0.5) * ring / ringsPerHemisphere;
			double sinPhi = Math.Sin(phi);
			double cosPhi = Math.Cos(phi);

			for (int seg = 0; seg <= segments; seg++)
			{
				double theta = 2.0 * Math.PI * seg / segments;
				double sinTheta = Math.Sin(theta);
				double cosTheta = Math.Cos(theta);

				var normal = new DVector3(
					sinPhi * cosTheta,
					-cosPhi,
					sinPhi * sinTheta);

				positions.Add(new DVector3(
					normal.X * radius,
					-cylinderHalf + normal.Y * radius,
					normal.Z * radius));

				normals.Add(normal);

				uvs.Add(new DVector2(
					(double)seg / segments,
					0.0 - (double)ring / (ringsPerHemisphere * 2.0)));
			}
		}

		int ringCount = positions.Count / (segments + 1);

		for (int ring = 0; ring < ringCount - 1; ring++)
		{
			int rowStart = ring * (segments + 1);
			int nextRowStart = (ring + 1) * (segments + 1);

			for (int seg = 0; seg < segments; seg++)
			{
				uint a = (uint)(rowStart + seg);
				uint b = (uint)(rowStart + seg + 1);
				uint c = (uint)(nextRowStart + seg);
				uint d = (uint)(nextRowStart + seg + 1);

				indices.Add(a);
				indices.Add(c);
				indices.Add(b);

				indices.Add(b);
				indices.Add(c);
				indices.Add(d);
			}
		}

		MeshData data = new(
			positions.ToArray(),
			indices.ToArray(),
			normals.ToArray(),
			uvs.ToArray());

		return new Mesh("Capsule", data);
	}
}