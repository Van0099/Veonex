using System.Globalization;
using System.Runtime.CompilerServices;

namespace Veonex.Mathematics;

public readonly struct DQuaternion : IEquatable<DQuaternion>
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public double W { get; }

    public DQuaternion(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public static DQuaternion Identity => new(0.0, 0.0, 0.0, 1.0);

    public double Length => Math.Sqrt(LengthSquared);
    public double LengthSquared => X * X + Y * Y + Z * Z + W * W;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DQuaternion Normalized()
    {
        var length = Length;
        if (length <= 0.0)
            return Identity;

        return this / length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DQuaternion Conjugated() => new(-X, -Y, -Z, W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DQuaternion Inverted()
    {
        var ls = LengthSquared;
        if (ls <= 0.0)
            return Identity;

        return Conjugated() / ls;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion CreateFromAxisAngle(in DVector3 axis, double angleRadians)
    {
        var half = angleRadians * 0.5;
        var s = Math.Sin(half);
        var c = Math.Cos(half);
        var n = axis.Normalized();
        return new DQuaternion(n.X * s, n.Y * s, n.Z * s, c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion CreateFromYawPitchRoll(double yaw, double pitch, double roll)
    {
        var halfYaw = yaw * 0.5;
        var halfPitch = pitch * 0.5;
        var halfRoll = roll * 0.5;

        var sy = Math.Sin(halfYaw);
        var cy = Math.Cos(halfYaw);
        var sp = Math.Sin(halfPitch);
        var cp = Math.Cos(halfPitch);
        var sr = Math.Sin(halfRoll);
        var cr = Math.Cos(halfRoll);

        return new DQuaternion(
            cy * sp * cr + sy * cp * sr,
            sy * cp * cr - cy * sp * sr,
            cy * cp * sr - sy * sp * cr,
            cy * cp * cr + sy * sp * sr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion CreateFromEulerDegrees(in DVector3 degrees)
    {
        const double DegToRad = Math.PI / 180.0;

        return CreateFromEuler(
            degrees.X * DegToRad,
            degrees.Y * DegToRad,
            degrees.Z * DegToRad);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion CreateFromEuler(double xRadians, double yRadians, double zRadians)
        => CreateFromYawPitchRoll(yRadians, xRadians, zRadians);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 ToEulerDegrees()
    {
        var q = Normalized();

        // X (Pitch)
        var sinX = 2.0 * (q.W * q.X + q.Y * q.Z);
        var cosX = 1.0 - 2.0 * (q.X * q.X + q.Y * q.Y);

        var x = Math.Atan2(sinX, cosX);


        // Y (Yaw)
        var sinY = 2.0 * (q.W * q.Y - q.Z * q.X);

        double y;

        if (Math.Abs(sinY) >= 1.0)
            y = Math.CopySign(Math.PI / 2.0, sinY);
        else
            y = Math.Asin(sinY);


        // Z (Roll)
        var sinZ = 2.0 * (q.W * q.Z + q.X * q.Y);
        var cosZ = 1.0 - 2.0 * (q.Y * q.Y + q.Z * q.Z);

        var z = Math.Atan2(sinZ, cosZ);


        const double RadToDeg = 180.0 / Math.PI;

        return new DVector3(
            x * RadToDeg,
            y * RadToDeg,
            z * RadToDeg);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion CreateFromRotationMatrix(in DMatrix4x4 matrix)
    {
        var trace = matrix.M11 + matrix.M22 + matrix.M33;

        if (trace > 0.0)
        {
            var s = Math.Sqrt(trace + 1.0) * 2.0;
            return new DQuaternion(
                (matrix.M23 - matrix.M32) / s,
                (matrix.M31 - matrix.M13) / s,
                (matrix.M12 - matrix.M21) / s,
                0.25 * s);
        }

        if (matrix.M11 > matrix.M22 && matrix.M11 > matrix.M33)
        {
            var s = Math.Sqrt(1.0 + matrix.M11 - matrix.M22 - matrix.M33) * 2.0;
            return new DQuaternion(
                0.25 * s,
                (matrix.M12 + matrix.M21) / s,
                (matrix.M13 + matrix.M31) / s,
                (matrix.M23 - matrix.M32) / s);
        }

        if (matrix.M22 > matrix.M33)
        {
            var s = Math.Sqrt(1.0 + matrix.M22 - matrix.M11 - matrix.M33) * 2.0;
            return new DQuaternion(
                (matrix.M12 + matrix.M21) / s,
                0.25 * s,
                (matrix.M23 + matrix.M32) / s,
                (matrix.M31 - matrix.M13) / s);
        }

        var t = Math.Sqrt(1.0 + matrix.M33 - matrix.M11 - matrix.M22) * 2.0;
        return new DQuaternion(
            (matrix.M13 + matrix.M31) / t,
            (matrix.M23 + matrix.M32) / t,
            0.25 * t,
            (matrix.M12 - matrix.M21) / t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion Lerp(in DQuaternion a, in DQuaternion b, double t)
    => new DQuaternion(
        DMath.Lerp(a.X, b.X, t),
        DMath.Lerp(a.Y, b.Y, t),
        DMath.Lerp(a.Z, b.Z, t),
        DMath.Lerp(a.W, b.W, t)).Normalized();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion Slerp(in DQuaternion a, in DQuaternion b, double t)
    {
        var cosTheta = Dot(a, b);
        var end = b;

        if (cosTheta < 0.0)
        {
            end = -b;
            cosTheta = -cosTheta;
        }

        if (cosTheta > 0.9995)
            return Lerp(a, end, t);

        var theta = Math.Acos(DMath.Clamp(cosTheta, -1.0, 1.0));
        var sinTheta = Math.Sin(theta);

        var w1 = Math.Sin((1.0 - t) * theta) / sinTheta;
        var w2 = Math.Sin(t * theta) / sinTheta;

        return (a * w1 + end * w2).Normalized();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(in DQuaternion a, in DQuaternion b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion operator +(in DQuaternion left, in DQuaternion right)
        => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion operator -(in DQuaternion left, in DQuaternion right)
        => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion operator -(in DQuaternion value)
        => new(-value.X, -value.Y, -value.Z, -value.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion operator *(in DQuaternion left, double scalar)
        => new(left.X * scalar, left.Y * scalar, left.Z * scalar, left.W * scalar);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion operator *(double scalar, in DQuaternion right)
        => right * scalar;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion operator /(in DQuaternion left, double scalar)
        => new(left.X / scalar, left.Y / scalar, left.Z / scalar, left.W / scalar);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DQuaternion operator *(in DQuaternion left, in DQuaternion right)
        => new(
            left.W * right.X + left.X * right.W + left.Y * right.Z - left.Z * right.Y,
            left.W * right.Y - left.X * right.Z + left.Y * right.W + left.Z * right.X,
            left.W * right.Z + left.X * right.Y - left.Y * right.X + left.Z * right.W,
            left.W * right.W - left.X * right.X - left.Y * right.Y - left.Z * right.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 operator *(in DQuaternion rotation, in DVector3 point)
        => Transform(rotation, point);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Transform(in DQuaternion rotation, in DVector3 point)
    {
        var q = rotation.Normalized();
        var u = new DVector3(q.X, q.Y, q.Z);
        var s = q.W;

        var cross1 = DVector3.Cross(u, point);
        var cross2 = DVector3.Cross(u, cross1);

        return point + 2.0 * (s * cross1 + cross2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DMatrix4x4 ToMatrix4x4() => DMatrix4x4.CreateFromQuaternion(this);

    public bool Equals(DQuaternion other)
        => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    public override bool Equals(object? obj) => obj is DQuaternion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    public static bool operator ==(DQuaternion left, DQuaternion right) => left.Equals(right);
    public static bool operator !=(DQuaternion left, DQuaternion right) => !left.Equals(right);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Z}, {W})");
}
