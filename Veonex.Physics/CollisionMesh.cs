using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Veonex.Core;
using Veonex.Mathematics;
using SourceMesh = Veonex.Core.Mesh;
using BepuMesh = BepuPhysics.Collidables.Mesh;
using NVector3 = System.Numerics.Vector3;

namespace Veonex.Physics;

internal readonly struct CollisionShapeData
{
    public TypedIndex ShapeIndex { get; }
    public DVector3 LocalCenterOffset { get; }
    public BodyInertia Inertia { get; }

    public CollisionShapeData(
        TypedIndex shapeIndex,
        DVector3 localCenterOffset,
        BodyInertia inertia)
    {
        ShapeIndex = shapeIndex;
        LocalCenterOffset = localCenterOffset;
        Inertia = inertia;
    }
}

/// <summary>
/// Converts Veonex mesh data to Bepu collision shapes.
/// This class owns shape construction; Physics owns the returned shape lifetime.
/// </summary>
internal static class CollisionMesh
{
    public static CollisionShapeData Create(
        Simulation simulation,
        BufferPool bufferPool,
        SourceMesh sourceMesh,
        Transform transform,
        Collider collider,
        bool dynamic,
        double mass)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(bufferPool);
        ArgumentNullException.ThrowIfNull(sourceMesh);
        ArgumentNullException.ThrowIfNull(transform);
        ArgumentNullException.ThrowIfNull(collider);

        MeshData data = sourceMesh.Data;
        ValidatePositions(data, sourceMesh.Name);

        DVector3 effectiveScale = new(
            transform.Scale.X * collider.SizePercent.X / 100.0,
            transform.Scale.Y * collider.SizePercent.Y / 100.0,
            transform.Scale.Z * collider.SizePercent.Z / 100.0);

        ValidateEffectiveScale(effectiveScale, sourceMesh.Name);

        DVector3 scaledOffset = new(
            transform.Scale.X * collider.Offset.X,
            transform.Scale.Y * collider.Offset.Y,
            transform.Scale.Z * collider.Offset.Z);

        if (!IsFinite(scaledOffset))
        {
            throw new InvalidOperationException(
                $"Collider offset on mesh \"{sourceMesh.Name}\" is outside the supported numeric range.");
        }

        if (collider.SimpleGeometry)
        {
            return CreateSimpleGeometry(
                simulation,
                data,
                sourceMesh.Name,
                effectiveScale,
                scaledOffset,
                dynamic,
                mass);
        }

        ValidateIndices(data, sourceMesh.Name);

        // Simplification affects only physics geometry. The original SourceMesh stays untouched
        // and continues to be used by the renderer.
        SimplifiedCollisionMesh simplified = CollisionMeshSimplifier.Simplify(
            data,
            collider.CollisionQuality);

        return CreateTriangleMesh(
            simulation,
            bufferPool,
            simplified.Positions,
            simplified.Indices,
            sourceMesh.Name,
            effectiveScale,
            scaledOffset,
            dynamic,
            mass);
    }

    private static CollisionShapeData CreateSimpleGeometry(
        Simulation simulation,
        MeshData data,
        string meshName,
        DVector3 scale,
        DVector3 scaledOffset,
        bool dynamic,
        double mass)
    {
        GetBounds(data.Positions, out DVector3 min, out DVector3 max);

        DVector3 sourceSize = max - min;

        var dimensions = new DVector3(
            sourceSize.X * Math.Abs(scale.X),
            sourceSize.Y * Math.Abs(scale.Y),
            sourceSize.Z * Math.Abs(scale.Z));

        float width = ToPositiveFloat(dimensions.X, $"{meshName} collider width");
        float height = ToPositiveFloat(dimensions.Y, $"{meshName} collider height");
        float depth = ToPositiveFloat(dimensions.Z, $"{meshName} collider depth");

        DVector3 center = new(
            (min.X + (max.X - min.X) * 0.5) * scale.X + scaledOffset.X,
            (min.Y + (max.Y - min.Y) * 0.5) * scale.Y + scaledOffset.Y,
            (min.Z + (max.Z - min.Z) * 0.5) * scale.Z + scaledOffset.Z);

        Box box = new(width, height, depth);
        BodyInertia inertia = dynamic
            ? box.ComputeInertia(ToPositiveFloat(mass, nameof(PhysicsBody.Mass)))
            : default;

        TypedIndex shapeIndex = simulation.Shapes.Add(box);
        return new CollisionShapeData(shapeIndex, center, inertia);
    }

    private static CollisionShapeData CreateTriangleMesh(
        Simulation simulation,
        BufferPool bufferPool,
        DVector3[] positions,
        int[] indices,
        string meshName,
        DVector3 scale,
        DVector3 scaledOffset,
        bool dynamic,
        double mass)
    {
        int triangleCount = indices.Length / 3;
        bufferPool.Take<Triangle>(triangleCount, out var triangles);

        bool reverseWinding = scale.X * scale.Y * scale.Z < 0.0;

        try
        {
            for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                int indexOffset = triangleIndex * 3;

                int indexA = indices[indexOffset];
                int indexB = indices[indexOffset + 1];
                int indexC = indices[indexOffset + 2];

                NVector3 a = ScalePosition(positions[indexA], scale, meshName);
                NVector3 b = ScalePosition(positions[indexB], scale, meshName);
                NVector3 c = ScalePosition(positions[indexC], scale, meshName);

                // Bepu meshes are one-sided. Mirroring an odd number of axes reverses winding,
                // so restore the original face orientation after baking the scale into vertices.
                triangles[triangleIndex] = reverseWinding
                    ? new Triangle(a, c, b)
                    : new Triangle(a, b, c);
            }
        }
        catch
        {
            bufferPool.Return(ref triangles);
            throw;
        }

        BepuMesh nativeMesh = new(triangles, NVector3.One, bufferPool);
        bool shapeAdded = false;

        try
        {
            BodyInertia inertia = default;
            DVector3 localCenterOffset = scaledOffset;

            if (dynamic)
            {
                // Mesh remains the actual triangle geometry. Open inertia is used so the
                // mesh does not have to be a perfectly closed manifold to be simulated.
                inertia = nativeMesh.ComputeOpenInertia(
                    ToPositiveFloat(mass, nameof(PhysicsBody.Mass)),
                    out NVector3 center);

                localCenterOffset += new DVector3(center.X, center.Y, center.Z);
            }

            TypedIndex shapeIndex = simulation.Shapes.Add(nativeMesh);
            shapeAdded = true;

            return new CollisionShapeData(
                shapeIndex,
                localCenterOffset,
                inertia);
        }
        finally
        {
            if (!shapeAdded)
                nativeMesh.Dispose(bufferPool);
        }
    }

    private static void ValidateIndices(MeshData data, string meshName)
    {
        if (data.Indices.Length < 3 || data.Indices.Length % 3 != 0)
        {
            throw new InvalidOperationException(
                $"Mesh \"{meshName}\" must have an index count divisible by three to be used as a triangle collider.");
        }

        for (int i = 0; i < data.Indices.Length; i++)
        {
            if (data.Indices[i] >= data.Positions.Length)
            {
                throw new InvalidOperationException(
                    $"Mesh \"{meshName}\" contains an index outside its Positions array (index {data.Indices[i]} at offset {i}).");
            }
        }
    }

    private static void ValidatePositions(MeshData data, string meshName)
    {
        if (data.Positions.Length == 0)
        {
            throw new InvalidOperationException(
                $"Mesh \"{meshName}\" has no positions and cannot be used as a collider.");
        }

        for (int i = 0; i < data.Positions.Length; i++)
        {
            if (!IsFinite(data.Positions[i]))
            {
                throw new InvalidOperationException(
                    $"Mesh \"{meshName}\" contains a vertex with non-finite coordinates at index {i}.");
            }
        }
    }

    private static void ValidateEffectiveScale(DVector3 scale, string meshName)
    {
        if (!IsFinite(scale) ||
            scale.X == 0.0 ||
            scale.Y == 0.0 ||
            scale.Z == 0.0 ||
            !float.IsFinite((float)scale.X) ||
            !float.IsFinite((float)scale.Y) ||
            !float.IsFinite((float)scale.Z))
        {
            throw new InvalidOperationException(
                $"Collider on mesh \"{meshName}\" has an invalid effective scale. Check Transform.Scale and Collider.SizePercent.");
        }
    }

    private static void GetBounds(
        DVector3[] positions,
        out DVector3 min,
        out DVector3 max)
    {
        min = positions[0];
        max = positions[0];

        for (int i = 1; i < positions.Length; i++)
        {
            min = DVector3.Min(min, positions[i]);
            max = DVector3.Max(max, positions[i]);
        }
    }

    private static NVector3 ScalePosition(
        DVector3 position,
        DVector3 scale,
        string meshName)
    {
        double x = position.X * scale.X;
        double y = position.Y * scale.Y;
        double z = position.Z * scale.Z;

        float fx = (float)x;
        float fy = (float)y;
        float fz = (float)z;

        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
            !float.IsFinite(fx) || !float.IsFinite(fy) || !float.IsFinite(fz))
        {
            throw new InvalidOperationException(
                $"Mesh \"{meshName}\" contains collider coordinates outside the supported float range.");
        }

        return new NVector3(fx, fy, fz);
    }

    private static float ToPositiveFloat(double value, string parameterName)
    {
        float result = (float)value;

        if (!double.IsFinite(value) ||
            !float.IsFinite(result) ||
            result <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Value must be positive, finite, and representable as a float.");
        }

        return result;
    }

    private static bool IsFinite(DVector3 value)
        => double.IsFinite(value.X) &&
           double.IsFinite(value.Y) &&
           double.IsFinite(value.Z);
}
