using System;
using System.Collections.Generic;
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
		double cylinderHeight = cylinderHalf * 2.0;

		double topCapLength = Math.PI * radius * 0.5;
		double totalLength = Math.PI * radius + cylinderHeight;

		int actualCylinderSegments =
			cylinderHalf > 0.0 ? cylinderSegments : 0;

		var positions = new List<DVector3>();
		var normals = new List<DVector3>();
		var uvs = new List<DVector2>();
		var indices = new List<uint>();

		for (int ring = 0; ring <= ringsPerHemisphere; ring++)
		{
			double phi =
				(Math.PI * 0.5) * ring / ringsPerHemisphere;

			double sinPhi = Math.Sin(phi);
			double cosPhi = Math.Cos(phi);
			double v = radius * phi / totalLength;

			for (int seg = 0; seg <= segments; seg++)
			{
				double theta = 2.0 * Math.PI * seg / segments;
				double sinTheta = Math.Sin(theta);
				double cosTheta = Math.Cos(theta);

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
					v));
			}
		}

		for (int cyl = 1; cyl <= actualCylinderSegments; cyl++)
		{
			double t = (double)cyl / actualCylinderSegments;
			double y = cylinderHalf - t * cylinderHeight;
			double v = (topCapLength + t * cylinderHeight) / totalLength;

			for (int seg = 0; seg <= segments; seg++)
			{
				double theta = 2.0 * Math.PI * seg / segments;
				double sinTheta = Math.Sin(theta);
				double cosTheta = Math.Cos(theta);

				var normal = new DVector3(
					cosTheta,
					0.0,
					sinTheta);

				positions.Add(new DVector3(
					normal.X * radius,
					y,
					normal.Z * radius));

				normals.Add(normal);

				uvs.Add(new DVector2(
					(double)seg / segments,
					v));
			}
		}

		for (int ring = 1; ring <= ringsPerHemisphere; ring++)
		{
			double alpha =
				(Math.PI * 0.5) * ring / ringsPerHemisphere;

			double sinAlpha = Math.Sin(alpha);
			double cosAlpha = Math.Cos(alpha);

			double v =
				(topCapLength + cylinderHeight + radius * alpha)
				/ totalLength;

			for (int seg = 0; seg <= segments; seg++)
			{
				double theta = 2.0 * Math.PI * seg / segments;
				double sinTheta = Math.Sin(theta);
				double cosTheta = Math.Cos(theta);

				var normal = new DVector3(
					cosAlpha * cosTheta,
					-sinAlpha,
					cosAlpha * sinTheta);

				positions.Add(new DVector3(
					normal.X * radius,
					-cylinderHalf + normal.Y * radius,
					normal.Z * radius));

				normals.Add(normal);

				uvs.Add(new DVector2(
					(double)seg / segments,
					v));
			}
		}

		int rowSize = segments + 1;
		int ringCount = positions.Count / rowSize;

		for (int ring = 0; ring < ringCount - 1; ring++)
		{
			int rowStart = ring * rowSize;
			int nextRowStart = (ring + 1) * rowSize;

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