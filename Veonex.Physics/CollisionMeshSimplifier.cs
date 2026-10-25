using Veonex.Core;
using Veonex.Mathematics;

namespace Veonex.Physics;

/// <summary>
/// Simplified indexed geometry used only by the physics collider.
/// It never modifies the source mesh used by rendering.
/// </summary>
internal readonly record struct SimplifiedCollisionMesh(
    DVector3[] Positions,
    int[] Indices);

/// <summary>
/// Reduces triangle count using quadric-error edge collapses.
/// Coincident vertices are welded first so meshes with split render vertices
/// (for example, because of normals or UV seams) can still be simplified.
/// </summary>
internal static class CollisionMeshSimplifier
{
    public static SimplifiedCollisionMesh Simplify(MeshData data, double quality)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!double.IsFinite(quality) || quality <= 0.0 || quality > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quality),
                "Collision quality must be finite, greater than zero, and at most 1.0.");
        }

        if (data.Indices.Length < 3 || data.Indices.Length % 3 != 0)
        {
            throw new InvalidOperationException(
                "The mesh must contain an indexed triangle list to simplify its collider.");
        }

        int[] sourceIndices = new int[data.Indices.Length];
        for (int i = 0; i < sourceIndices.Length; i++)
        {
            int index = checked((int)data.Indices[i]);
            if (index < 0 || index >= data.Positions.Length)
            {
                throw new InvalidOperationException(
                    $"Mesh index {index} at offset {i} is outside the Positions array.");
            }

            sourceIndices[i] = index;
        }

        // A quality of 1.0 deliberately keeps the source geometry unchanged.
        if (quality == 1.0)
            return new SimplifiedCollisionMesh(data.Positions, sourceIndices);

        int sourceTriangleCount = sourceIndices.Length / 3;
        int targetTriangleCount = Math.Clamp(
            (int)Math.Round(sourceTriangleCount * quality, MidpointRounding.AwayFromZero),
            1,
            sourceTriangleCount);

        WeldedMesh welded = WeldVertices(data.Positions, sourceIndices);

        if (welded.Faces.Count == 0)
        {
            // Do not replace a malformed/fully-degenerate mesh with an empty collider.
            return new SimplifiedCollisionMesh(data.Positions, sourceIndices);
        }

        if (welded.Faces.Count <= targetTriangleCount)
            return BuildResult(welded.Positions, welded.Faces);

        Quadric[] quadrics = BuildQuadrics(welded.Positions, welded.Faces);
        List<Face> faces = welded.Faces;

        while (faces.Count > targetTriangleCount)
        {
            List<Candidate> candidates = BuildCandidates(
                welded.Positions,
                faces,
                quadrics);

            if (candidates.Count == 0)
                break;

            candidates.Sort(static (left, right) =>
            {
                int comparison = left.Cost.CompareTo(right.Cost);
                if (comparison != 0)
                    return comparison;

                comparison = left.Keep.CompareTo(right.Keep);
                return comparison != 0
                    ? comparison
                    : left.Remove.CompareTo(right.Remove);
            });

            bool collapsed = false;

            foreach (Candidate candidate in candidates)
            {
                if (!CanCollapse(
                        welded.Positions,
                        faces,
                        candidate.Keep,
                        candidate.Remove,
                        candidate.Position))
                {
                    continue;
                }

                welded.Positions[candidate.Keep] = candidate.Position;
                quadrics[candidate.Keep] =
                    quadrics[candidate.Keep] + quadrics[candidate.Remove];

                faces = ApplyCollapse(
                    faces,
                    candidate.Keep,
                    candidate.Remove);

                collapsed = true;
                break;
            }

            // Some topologies cannot be reduced further without flipping a triangle.
            if (!collapsed)
                break;
        }

        if (faces.Count == 0)
            return new SimplifiedCollisionMesh(data.Positions, sourceIndices);

        return BuildResult(welded.Positions, faces);
    }

    private static WeldedMesh WeldVertices(
        DVector3[] sourcePositions,
        int[] sourceIndices)
    {
        DVector3 min = sourcePositions[0];
        DVector3 max = sourcePositions[0];

        for (int i = 1; i < sourcePositions.Length; i++)
        {
            DVector3 point = sourcePositions[i];
            min = DVector3.Min(min, point);
            max = DVector3.Max(max, point);
        }

        double extent = Math.Max(
            max.X - min.X,
            Math.Max(max.Y - min.Y, max.Z - min.Z));
        double tolerance = Math.Max(extent * 1e-8, 1e-15);

        var positions = new List<Vector3d>(sourcePositions.Length);
        var vertexMap = new int[sourcePositions.Length];
        var lookup = new Dictionary<VertexKey, int>(sourcePositions.Length);

        for (int i = 0; i < sourcePositions.Length; i++)
        {
            DVector3 source = sourcePositions[i];
            long x = (long)Math.Round((source.X - min.X) / tolerance);
            long y = (long)Math.Round((source.Y - min.Y) / tolerance);
            long z = (long)Math.Round((source.Z - min.Z) / tolerance);
            var key = new VertexKey(x, y, z);

            if (!lookup.TryGetValue(key, out int weldedIndex))
            {
                weldedIndex = positions.Count;
                positions.Add(new Vector3d(source.X, source.Y, source.Z));
                lookup.Add(key, weldedIndex);
            }

            vertexMap[i] = weldedIndex;
        }

        var faces = new List<Face>(sourceIndices.Length / 3);
        var uniqueFaces = new HashSet<FaceKey>();

        for (int i = 0; i < sourceIndices.Length; i += 3)
        {
            int a = vertexMap[sourceIndices[i]];
            int b = vertexMap[sourceIndices[i + 1]];
            int c = vertexMap[sourceIndices[i + 2]];

            if (a == b || b == c || c == a)
                continue;

            var key = new FaceKey(a, b, c);
            if (uniqueFaces.Add(key))
                faces.Add(new Face(a, b, c));
        }

        return new WeldedMesh(positions, faces);
    }

    private static Quadric[] BuildQuadrics(
        List<Vector3d> positions,
        List<Face> faces)
    {
        var quadrics = new Quadric[positions.Count];
        var edgeInfo = new Dictionary<ulong, EdgeInfo>(faces.Count * 2);

        foreach (Face face in faces)
        {
            Vector3d a = positions[face.A];
            Vector3d b = positions[face.B];
            Vector3d c = positions[face.C];

            Vector3d cross = Vector3d.Cross(b - a, c - a);
            double lengthSquared = cross.LengthSquared;
            if (lengthSquared <= 1e-30 || !double.IsFinite(lengthSquared))
                continue;

            Vector3d normal = cross / Math.Sqrt(lengthSquared);
            double d = -Vector3d.Dot(normal, a);
            Quadric plane = Quadric.FromPlane(normal.X, normal.Y, normal.Z, d, 1.0);

            quadrics[face.A] += plane;
            quadrics[face.B] += plane;
            quadrics[face.C] += plane;

            AddEdge(edgeInfo, face.A, face.B, normal);
            AddEdge(edgeInfo, face.B, face.C, normal);
            AddEdge(edgeInfo, face.C, face.A, normal);
        }

        // Boundary constraints discourage collapses that pull open edges inward.
        const double boundaryWeight = 100.0;
        foreach (KeyValuePair<ulong, EdgeInfo> pair in edgeInfo)
        {
            EdgeInfo info = pair.Value;
            if (info.Count != 1)
                continue;

            GetEdgeVertices(pair.Key, out int a, out int b);
            Vector3d direction = positions[b] - positions[a];
            double directionLengthSquared = direction.LengthSquared;
            if (directionLengthSquared <= 1e-30)
                continue;

            direction /= Math.Sqrt(directionLengthSquared);
            Vector3d boundaryNormal = Vector3d.Cross(direction, info.FaceNormal);
            double normalLengthSquared = boundaryNormal.LengthSquared;
            if (normalLengthSquared <= 1e-30)
                continue;

            boundaryNormal /= Math.Sqrt(normalLengthSquared);
            double d = -Vector3d.Dot(boundaryNormal, positions[a]);
            Quadric boundaryPlane = Quadric.FromPlane(
                boundaryNormal.X,
                boundaryNormal.Y,
                boundaryNormal.Z,
                d,
                boundaryWeight);

            quadrics[a] += boundaryPlane;
            quadrics[b] += boundaryPlane;
        }

        return quadrics;
    }

    private static void AddEdge(
        Dictionary<ulong, EdgeInfo> edgeInfo,
        int a,
        int b,
        Vector3d faceNormal)
    {
        ulong key = MakeEdgeKey(a, b);

        if (edgeInfo.TryGetValue(key, out EdgeInfo info))
        {
            info.Count++;
            edgeInfo[key] = info;
        }
        else
        {
            edgeInfo.Add(key, new EdgeInfo(1, faceNormal));
        }
    }

    private static List<Candidate> BuildCandidates(
        List<Vector3d> positions,
        List<Face> faces,
        Quadric[] quadrics)
    {
        var edges = new HashSet<ulong>(faces.Count * 2);
        foreach (Face face in faces)
        {
            edges.Add(MakeEdgeKey(face.A, face.B));
            edges.Add(MakeEdgeKey(face.B, face.C));
            edges.Add(MakeEdgeKey(face.C, face.A));
        }

        var candidates = new List<Candidate>(edges.Count);

        foreach (ulong edge in edges)
        {
            GetEdgeVertices(edge, out int keep, out int remove);

            Quadric combined = quadrics[keep] + quadrics[remove];
            Vector3d candidatePosition = FindBestPosition(
                combined,
                positions[keep],
                positions[remove]);

            double cost = combined.Evaluate(candidatePosition);
            if (!double.IsFinite(cost))
                continue;

            candidates.Add(new Candidate(
                keep,
                remove,
                candidatePosition,
                Math.Max(0.0, cost)));
        }

        return candidates;
    }

    private static Vector3d FindBestPosition(
        Quadric quadric,
        Vector3d a,
        Vector3d b)
    {
        Vector3d midpoint = (a + b) * 0.5;
        Vector3d bestPosition = a;
        double bestCost = SafeCost(quadric, a);

        double costB = SafeCost(quadric, b);
        if (costB < bestCost)
        {
            bestCost = costB;
            bestPosition = b;
        }

        double midpointCost = SafeCost(quadric, midpoint);
        if (midpointCost < bestCost)
        {
            bestCost = midpointCost;
            bestPosition = midpoint;
        }

        if (quadric.TryOptimize(out Vector3d optimal) && optimal.IsFinite)
        {
            double optimalCost = SafeCost(quadric, optimal);
            if (optimalCost < bestCost)
                bestPosition = optimal;
        }

        return bestPosition;
    }

    private static double SafeCost(Quadric quadric, Vector3d position)
    {
        double cost = quadric.Evaluate(position);
        return double.IsFinite(cost) ? Math.Max(0.0, cost) : double.PositiveInfinity;
    }

    private static bool CanCollapse(
        List<Vector3d> positions,
        List<Face> faces,
        int keep,
        int remove,
        Vector3d newPosition)
    {
        foreach (Face face in faces)
        {
            bool containsKeep = face.Contains(keep);
            bool containsRemove = face.Contains(remove);

            if (!containsKeep && !containsRemove)
                continue;

            // Faces containing both edge vertices disappear during the collapse.
            if (containsKeep && containsRemove)
                continue;

            Vector3d oldA = positions[face.A];
            Vector3d oldB = positions[face.B];
            Vector3d oldC = positions[face.C];
            Vector3d oldNormal = Vector3d.Cross(oldB - oldA, oldC - oldA);
            double oldAreaSquared = oldNormal.LengthSquared;

            int newAIndex = face.A == remove ? keep : face.A;
            int newBIndex = face.B == remove ? keep : face.B;
            int newCIndex = face.C == remove ? keep : face.C;

            Vector3d newA = newAIndex == keep ? newPosition : positions[newAIndex];
            Vector3d newB = newBIndex == keep ? newPosition : positions[newBIndex];
            Vector3d newC = newCIndex == keep ? newPosition : positions[newCIndex];
            Vector3d newNormal = Vector3d.Cross(newB - newA, newC - newA);
            double newAreaSquared = newNormal.LengthSquared;

            if (!double.IsFinite(newAreaSquared))
                return false;

            if (oldAreaSquared > 1e-30)
            {
                if (newAreaSquared <= oldAreaSquared * 1e-12 ||
                    Vector3d.Dot(oldNormal, newNormal) <= 0.0)
                {
                    // Avoid creating a degenerate or flipped triangle.
                    return false;
                }
            }
        }

        return true;
    }

    private static List<Face> ApplyCollapse(
        List<Face> faces,
        int keep,
        int remove)
    {
        var result = new List<Face>(faces.Count);
        var unique = new HashSet<FaceKey>();

        foreach (Face face in faces)
        {
            int a = face.A == remove ? keep : face.A;
            int b = face.B == remove ? keep : face.B;
            int c = face.C == remove ? keep : face.C;

            if (a == b || b == c || c == a)
                continue;

            var key = new FaceKey(a, b, c);
            if (unique.Add(key))
                result.Add(new Face(a, b, c));
        }

        return result;
    }

    private static SimplifiedCollisionMesh BuildResult(
        List<Vector3d> sourcePositions,
        List<Face> faces)
    {
        var remap = new int[sourcePositions.Count];
        Array.Fill(remap, -1);

        var outputPositions = new List<DVector3>();
        var outputIndices = new int[faces.Count * 3];
        int outputOffset = 0;

        foreach (Face face in faces)
        {
            outputIndices[outputOffset++] = GetOrAddVertex(face.A);
            outputIndices[outputOffset++] = GetOrAddVertex(face.B);
            outputIndices[outputOffset++] = GetOrAddVertex(face.C);
        }

        return new SimplifiedCollisionMesh(outputPositions.ToArray(), outputIndices);

        int GetOrAddVertex(int sourceIndex)
        {
            int mappedIndex = remap[sourceIndex];
            if (mappedIndex >= 0)
                return mappedIndex;

            Vector3d position = sourcePositions[sourceIndex];
            mappedIndex = outputPositions.Count;
            outputPositions.Add(new DVector3(position.X, position.Y, position.Z));
            remap[sourceIndex] = mappedIndex;
            return mappedIndex;
        }
    }

    private static ulong MakeEdgeKey(int a, int b)
    {
        uint first = (uint)Math.Min(a, b);
        uint second = (uint)Math.Max(a, b);
        return ((ulong)first << 32) | second;
    }

    private static void GetEdgeVertices(ulong key, out int a, out int b)
    {
        a = (int)(uint)(key >> 32);
        b = (int)(uint)key;
    }

    private readonly record struct VertexKey(long X, long Y, long Z);

    private readonly record struct FaceKey
    {
        public int A { get; }
        public int B { get; }
        public int C { get; }

        public FaceKey(int a, int b, int c)
        {
            A = Math.Min(a, Math.Min(b, c));
            C = Math.Max(a, Math.Max(b, c));
            B = a + b + c - A - C;
        }
    }

    private sealed class WeldedMesh
    {
        public List<Vector3d> Positions { get; }
        public List<Face> Faces { get; }

        public WeldedMesh(List<Vector3d> positions, List<Face> faces)
        {
            Positions = positions;
            Faces = faces;
        }
    }

    private struct Face
    {
        public int A;
        public int B;
        public int C;

        public Face(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }

        public bool Contains(int index) => A == index || B == index || C == index;
    }

    private struct EdgeInfo
    {
        public int Count;
        public Vector3d FaceNormal;

        public EdgeInfo(int count, Vector3d faceNormal)
        {
            Count = count;
            FaceNormal = faceNormal;
        }
    }

    private readonly record struct Candidate(
        int Keep,
        int Remove,
        Vector3d Position,
        double Cost);

    private readonly struct Quadric
    {
        private readonly double _m00;
        private readonly double _m01;
        private readonly double _m02;
        private readonly double _m03;
        private readonly double _m11;
        private readonly double _m12;
        private readonly double _m13;
        private readonly double _m22;
        private readonly double _m23;
        private readonly double _m33;

        private Quadric(
            double m00, double m01, double m02, double m03,
            double m11, double m12, double m13,
            double m22, double m23, double m33)
        {
            _m00 = m00;
            _m01 = m01;
            _m02 = m02;
            _m03 = m03;
            _m11 = m11;
            _m12 = m12;
            _m13 = m13;
            _m22 = m22;
            _m23 = m23;
            _m33 = m33;
        }

        public static Quadric FromPlane(
            double x,
            double y,
            double z,
            double d,
            double weight)
        {
            return new Quadric(
                x * x * weight,
                x * y * weight,
                x * z * weight,
                x * d * weight,
                y * y * weight,
                y * z * weight,
                y * d * weight,
                z * z * weight,
                z * d * weight,
                d * d * weight);
        }

        public double Evaluate(Vector3d p)
        {
            double x = p.X;
            double y = p.Y;
            double z = p.Z;

            return
                _m00 * x * x +
                2.0 * _m01 * x * y +
                2.0 * _m02 * x * z +
                2.0 * _m03 * x +
                _m11 * y * y +
                2.0 * _m12 * y * z +
                2.0 * _m13 * y +
                _m22 * z * z +
                2.0 * _m23 * z +
                _m33;
        }

        public bool TryOptimize(out Vector3d result)
        {
            double a = _m00;
            double b = _m01;
            double c = _m02;
            double d = _m11;
            double e = _m12;
            double f = _m22;

            double cofactor00 = d * f - e * e;
            double cofactor01 = c * e - b * f;
            double cofactor02 = b * e - c * d;
            double cofactor11 = a * f - c * c;
            double cofactor12 = b * c - a * e;
            double cofactor22 = a * d - b * b;

            double determinant =
                a * cofactor00 +
                b * cofactor01 +
                c * cofactor02;

            double matrixScale = Math.Max(
                Math.Max(Math.Abs(a), Math.Abs(b)),
                Math.Max(
                    Math.Max(Math.Abs(c), Math.Abs(d)),
                    Math.Max(Math.Abs(e), Math.Abs(f))));

            double threshold = 1e-12 * Math.Max(1.0, matrixScale * matrixScale * matrixScale);
            if (!double.IsFinite(determinant) || Math.Abs(determinant) <= threshold)
            {
                result = default;
                return false;
            }

            double rhsX = -_m03;
            double rhsY = -_m13;
            double rhsZ = -_m23;

            double x = (cofactor00 * rhsX + cofactor01 * rhsY + cofactor02 * rhsZ) / determinant;
            double y = (cofactor01 * rhsX + cofactor11 * rhsY + cofactor12 * rhsZ) / determinant;
            double z = (cofactor02 * rhsX + cofactor12 * rhsY + cofactor22 * rhsZ) / determinant;

            result = new Vector3d(x, y, z);
            return result.IsFinite;
        }

        public static Quadric operator +(Quadric left, Quadric right)
        {
            return new Quadric(
                left._m00 + right._m00,
                left._m01 + right._m01,
                left._m02 + right._m02,
                left._m03 + right._m03,
                left._m11 + right._m11,
                left._m12 + right._m12,
                left._m13 + right._m13,
                left._m22 + right._m22,
                left._m23 + right._m23,
                left._m33 + right._m33);
        }
    }

    private readonly struct Vector3d
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public Vector3d(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double LengthSquared => X * X + Y * Y + Z * Z;
        public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y) && double.IsFinite(Z);

        public static double Dot(Vector3d a, Vector3d b)
            => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vector3d Cross(Vector3d a, Vector3d b)
            => new(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);

        public static Vector3d operator +(Vector3d a, Vector3d b)
            => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vector3d operator -(Vector3d a, Vector3d b)
            => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vector3d operator *(Vector3d value, double scalar)
            => new(value.X * scalar, value.Y * scalar, value.Z * scalar);

        public static Vector3d operator /(Vector3d value, double scalar)
            => new(value.X / scalar, value.Y / scalar, value.Z / scalar);
    }
}
