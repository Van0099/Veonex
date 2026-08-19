using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Veonex.Mathematics;

[StructLayout(LayoutKind.Sequential)]
public readonly struct DMatrix4x4 : IEquatable<DMatrix4x4>
{
    public double M11 { get; }
    public double M12 { get; }
    public double M13 { get; }
    public double M14 { get; }
    public double M21 { get; }
    public double M22 { get; }
    public double M23 { get; }
    public double M24 { get; }
    public double M31 { get; }
    public double M32 { get; }
    public double M33 { get; }
    public double M34 { get; }
    public double M41 { get; }
    public double M42 { get; }
    public double M43 { get; }
    public double M44 { get; }

    public DMatrix4x4(
        double m11, double m12, double m13, double m14,
        double m21, double m22, double m23, double m24,
        double m31, double m32, double m33, double m34,
        double m41, double m42, double m43, double m44)
    {
        M11 = m11; M12 = m12; M13 = m13; M14 = m14;
        M21 = m21; M22 = m22; M23 = m23; M24 = m24;
        M31 = m31; M32 = m32; M33 = m33; M34 = m34;
        M41 = m41; M42 = m42; M43 = m43; M44 = m44;
    }

    public static DMatrix4x4 Identity => new(
        1.0, 0.0, 0.0, 0.0,
        0.0, 1.0, 0.0, 0.0,
        0.0, 0.0, 1.0, 0.0,
        0.0, 0.0, 0.0, 1.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateScale(double scale)
        => CreateScale(scale, scale, scale);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateScale(in DVector3 scale)
        => CreateScale(scale.X, scale.Y, scale.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateScale(double x, double y, double z)
        => new(
            x, 0.0, 0.0, 0.0,
            0.0, y, 0.0, 0.0,
            0.0, 0.0, z, 0.0,
            0.0, 0.0, 0.0, 1.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateTranslation(in DVector3 translation)
        => new(
            1.0, 0.0, 0.0, 0.0,
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            translation.X, translation.Y, translation.Z, 1.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateRotationX(double radians)
    {
        var s = Math.Sin(radians);
        var c = Math.Cos(radians);

        return new(
            1.0, 0.0, 0.0, 0.0,
            0.0, c, s, 0.0,
            0.0, -s, c, 0.0,
            0.0, 0.0, 0.0, 1.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateRotationY(double radians)
    {
        var s = Math.Sin(radians);
        var c = Math.Cos(radians);

        return new(
            c, 0.0, -s, 0.0,
            0.0, 1.0, 0.0, 0.0,
            s, 0.0, c, 0.0,
            0.0, 0.0, 0.0, 1.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateRotationZ(double radians)
    {
        var s = Math.Sin(radians);
        var c = Math.Cos(radians);

        return new(
            c, s, 0.0, 0.0,
            -s, c, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            0.0, 0.0, 0.0, 1.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateFromQuaternion(in DQuaternion quaternion)
    {
        var q = quaternion.Normalized();

        var x2 = q.X + q.X;
        var y2 = q.Y + q.Y;
        var z2 = q.Z + q.Z;

        var xx = q.X * x2;
        var yy = q.Y * y2;
        var zz = q.Z * z2;
        var xy = q.X * y2;
        var xz = q.X * z2;
        var yz = q.Y * z2;
        var wx = q.W * x2;
        var wy = q.W * y2;
        var wz = q.W * z2;

        return new DMatrix4x4(
            1.0 - (yy + zz), xy + wz, xz - wy, 0.0,
            xy - wz, 1.0 - (xx + zz), yz + wx, 0.0,
            xz + wy, yz - wx, 1.0 - (xx + yy), 0.0,
            0.0, 0.0, 0.0, 1.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateWorld(in DVector3 position, in DQuaternion rotation, in DVector3 scale)
        => CreateScale(scale) * CreateFromQuaternion(rotation) * CreateTranslation(position);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateLookAt(
    in DVector3 cameraPosition,
    in DVector3 cameraTarget,
    in DVector3 cameraUpVector)
    {
        var forward = (cameraTarget - cameraPosition).Normalized();

        var right = DVector3.Cross(forward, cameraUpVector).Normalized();
        var up = DVector3.Cross(right, forward);

        return new DMatrix4x4(
            right.X, up.X, forward.X, 0.0,
            right.Y, up.Y, forward.Y, 0.0,
            right.Z, up.Z, forward.Z, 0.0,

            -DVector3.Dot(right, cameraPosition),
            -DVector3.Dot(up, cameraPosition),
            -DVector3.Dot(forward, cameraPosition),
            1.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreatePerspectiveFieldOfView(
        double fieldOfView,
        double aspectRatio,
        double nearPlaneDistance,
        double farPlaneDistance)
    {
        if (fieldOfView <= 0.0 || fieldOfView >= Math.PI)
            throw new ArgumentOutOfRangeException(nameof(fieldOfView));
        if (aspectRatio <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        if (nearPlaneDistance <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));
        if (farPlaneDistance <= nearPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));

        var yScale = 1.0 / Math.Tan(fieldOfView * 0.5);
        var xScale = yScale / aspectRatio;

        return new DMatrix4x4(
            xScale, 0.0, 0.0, 0.0,
            0.0, yScale, 0.0, 0.0,
            0.0, 0.0, farPlaneDistance / (farPlaneDistance - nearPlaneDistance), 1.0,
            0.0, 0.0, (-nearPlaneDistance * farPlaneDistance) / (farPlaneDistance - nearPlaneDistance), 0.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateOrthographic(double width, double height, double nearPlaneDistance, double farPlaneDistance)
    {
        if (width <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(height));
        if (farPlaneDistance <= nearPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));

        return new DMatrix4x4(
            2.0 / width, 0.0, 0.0, 0.0,
            0.0, 2.0 / height, 0.0, 0.0,
            0.0, 0.0, 1.0 / (farPlaneDistance - nearPlaneDistance), 0.0,
            0.0, 0.0, -nearPlaneDistance / (farPlaneDistance - nearPlaneDistance), 1.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 CreateOrthographicOffCenter(
        double left,
        double right,
        double bottom,
        double top,
        double nearPlaneDistance,
        double farPlaneDistance)
    {
        if (right <= left)
            throw new ArgumentOutOfRangeException(nameof(right));
        if (top <= bottom)
            throw new ArgumentOutOfRangeException(nameof(top));
        if (farPlaneDistance <= nearPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));

        return new DMatrix4x4(
            2.0 / (right - left), 0.0, 0.0, 0.0,
            0.0, 2.0 / (top - bottom), 0.0, 0.0,
            0.0, 0.0, 1.0 / (farPlaneDistance - nearPlaneDistance), 0.0,
            (left + right) / (left - right),
            (top + bottom) / (bottom - top),
            -nearPlaneDistance / (farPlaneDistance - nearPlaneDistance),
            1.0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 Transpose(in DMatrix4x4 matrix)
        => new(
            matrix.M11, matrix.M21, matrix.M31, matrix.M41,
            matrix.M12, matrix.M22, matrix.M32, matrix.M42,
            matrix.M13, matrix.M23, matrix.M33, matrix.M43,
            matrix.M14, matrix.M24, matrix.M34, matrix.M44);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Invert(in DMatrix4x4 matrix, out DMatrix4x4 result)
    {
        Span<double> a = stackalloc double[32];

        a[0] = matrix.M11; a[1] = matrix.M12; a[2] = matrix.M13; a[3] = matrix.M14; a[4] = 1.0; a[5] = 0.0; a[6] = 0.0; a[7] = 0.0;
        a[8] = matrix.M21; a[9] = matrix.M22; a[10] = matrix.M23; a[11] = matrix.M24; a[12] = 0.0; a[13] = 1.0; a[14] = 0.0; a[15] = 0.0;
        a[16] = matrix.M31; a[17] = matrix.M32; a[18] = matrix.M33; a[19] = matrix.M34; a[20] = 0.0; a[21] = 0.0; a[22] = 1.0; a[23] = 0.0;
        a[24] = matrix.M41; a[25] = matrix.M42; a[26] = matrix.M43; a[27] = matrix.M44; a[28] = 0.0; a[29] = 0.0; a[30] = 0.0; a[31] = 1.0;

        for (int col = 0; col < 4; col++)
        {
            int pivotRow = col;
            double pivotAbs = Math.Abs(a[pivotRow * 8 + col]);

            for (int row = col + 1; row < 4; row++)
            {
                var abs = Math.Abs(a[row * 8 + col]);
                if (abs > pivotAbs)
                {
                    pivotAbs = abs;
                    pivotRow = row;
                }
            }

            if (pivotAbs <= 1e-18)
            {
                result = default;
                return false;
            }

            if (pivotRow != col)
            {
                for (int i = 0; i < 8; i++)
                    (a[col * 8 + i], a[pivotRow * 8 + i]) = (a[pivotRow * 8 + i], a[col * 8 + i]);
            }

            var pivot = a[col * 8 + col];
            var invPivot = 1.0 / pivot;

            for (int i = 0; i < 8; i++)
                a[col * 8 + i] *= invPivot;

            for (int row = 0; row < 4; row++)
            {
                if (row == col)
                    continue;

                var factor = a[row * 8 + col];
                if (factor == 0.0)
                    continue;

                for (int i = 0; i < 8; i++)
                    a[row * 8 + i] -= factor * a[col * 8 + i];
            }
        }

        result = new DMatrix4x4(
            a[4], a[5], a[6], a[7],
            a[12], a[13], a[14], a[15],
            a[20], a[21], a[22], a[23],
            a[28], a[29], a[30], a[31]);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 TransformPoint(in DVector3 point)
    {
        var x = point.X * M11 + point.Y * M21 + point.Z * M31 + M41;
        var y = point.X * M12 + point.Y * M22 + point.Z * M32 + M42;
        var z = point.X * M13 + point.Y * M23 + point.Z * M33 + M43;
        var w = point.X * M14 + point.Y * M24 + point.Z * M34 + M44;

        if (w != 0.0 && w != 1.0)
        {
            var invW = 1.0 / w;
            return new DVector3(x * invW, y * invW, z * invW);
        }

        return new DVector3(x, y, z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 TransformVector(in DVector3 vector)
        => new(
            vector.X * M11 + vector.Y * M21 + vector.Z * M31,
            vector.X * M12 + vector.Y * M22 + vector.Z * M32,
            vector.X * M13 + vector.Y * M23 + vector.Z * M33);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 TransformNormal(in DVector3 normal)
    {
        if (!Invert(this, out var inverted))
            return TransformVector(normal);

        var transposed = Transpose(inverted);
        return transposed.TransformVector(normal);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DMatrix4x4 operator *(in DMatrix4x4 left, in DMatrix4x4 right)
    {
        return new DMatrix4x4(
            left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31 + left.M14 * right.M41,
            left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32 + left.M14 * right.M42,
            left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33 + left.M14 * right.M43,
            left.M11 * right.M14 + left.M12 * right.M24 + left.M13 * right.M34 + left.M14 * right.M44,

            left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31 + left.M24 * right.M41,
            left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32 + left.M24 * right.M42,
            left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33 + left.M24 * right.M43,
            left.M21 * right.M14 + left.M22 * right.M24 + left.M23 * right.M34 + left.M24 * right.M44,

            left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31 + left.M34 * right.M41,
            left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32 + left.M34 * right.M42,
            left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33 + left.M34 * right.M43,
            left.M31 * right.M14 + left.M32 * right.M24 + left.M33 * right.M34 + left.M34 * right.M44,

            left.M41 * right.M11 + left.M42 * right.M21 + left.M43 * right.M31 + left.M44 * right.M41,
            left.M41 * right.M12 + left.M42 * right.M22 + left.M43 * right.M32 + left.M44 * right.M42,
            left.M41 * right.M13 + left.M42 * right.M23 + left.M43 * right.M33 + left.M44 * right.M43,
            left.M41 * right.M14 + left.M42 * right.M24 + left.M43 * right.M34 + left.M44 * right.M44);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector4 operator *(in DMatrix4x4 matrix, in DVector4 vector)
        => new(
            vector.X * matrix.M11 + vector.Y * matrix.M21 + vector.Z * matrix.M31 + vector.W * matrix.M41,
            vector.X * matrix.M12 + vector.Y * matrix.M22 + vector.Z * matrix.M32 + vector.W * matrix.M42,
            vector.X * matrix.M13 + vector.Y * matrix.M23 + vector.Z * matrix.M33 + vector.W * matrix.M43,
            vector.X * matrix.M14 + vector.Y * matrix.M24 + vector.Z * matrix.M34 + vector.W * matrix.M44);

    public bool Equals(DMatrix4x4 other)
        => M11.Equals(other.M11) && M12.Equals(other.M12) && M13.Equals(other.M13) && M14.Equals(other.M14)
        && M21.Equals(other.M21) && M22.Equals(other.M22) && M23.Equals(other.M23) && M24.Equals(other.M24)
        && M31.Equals(other.M31) && M32.Equals(other.M32) && M33.Equals(other.M33) && M34.Equals(other.M34)
        && M41.Equals(other.M41) && M42.Equals(other.M42) && M43.Equals(other.M43) && M44.Equals(other.M44);

    public override bool Equals(object? obj) => obj is DMatrix4x4 other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(M11); hash.Add(M12); hash.Add(M13); hash.Add(M14);
        hash.Add(M21); hash.Add(M22); hash.Add(M23); hash.Add(M24);
        hash.Add(M31); hash.Add(M32); hash.Add(M33); hash.Add(M34);
        hash.Add(M41); hash.Add(M42); hash.Add(M43); hash.Add(M44);

        return hash.ToHashCode();
    }

    public static bool operator ==(DMatrix4x4 left, DMatrix4x4 right) => left.Equals(right);
    public static bool operator !=(DMatrix4x4 left, DMatrix4x4 right) => !left.Equals(right);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture,
            $"[{M11}, {M12}, {M13}, {M14}] [{M21}, {M22}, {M23}, {M24}] [{M31}, {M32}, {M33}, {M34}] [{M41}, {M42}, {M43}, {M44}]");
}
