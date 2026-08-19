using System.Globalization;
using System.Runtime.CompilerServices;

namespace Veonex.Mathematics;

public readonly struct DVector3 : IEquatable<DVector3>
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }

    public DVector3(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static DVector3 Zero => new(0.0, 0.0, 0.0);
    public static DVector3 One => new(1.0, 1.0, 1.0);
    public static DVector3 UnitX => new(1.0, 0.0, 0.0);
    public static DVector3 UnitY => new(0.0, 1.0, 0.0);
    public static DVector3 UnitZ => new(0.0, 0.0, 1.0);
    public static DVector3 Up => UnitY;
    public static DVector3 Down => new(0.0, -1.0, 0.0);
    public static DVector3 Right => UnitX;
    public static DVector3 Left => new(-1.0, 0.0, 0.0);
    public static DVector3 Forward => new(0.0, 0.0, 1.0);
    public static DVector3 Backward => new(0.0, 0.0, -1.0);

    public double Length => Math.Sqrt(LengthSquared);
    public double LengthSquared => X * X + Y * Y + Z * Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 Normalized()
    {
        var length = Length;
        if (length <= 0.0)
            return Zero;

        return this / length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 WithX(double x) => new(x, Y, Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 WithY(double y) => new(X, y, Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 WithZ(double z) => new(X, Y, z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(in DVector3 a, in DVector3 b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Cross(in DVector3 a, in DVector3 b)
        => new(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Lerp(in DVector3 a, in DVector3 b, double t)
        => new(
            DMath.Lerp(a.X, b.X, t),
            DMath.Lerp(a.Y, b.Y, t),
            DMath.Lerp(a.Z, b.Z, t));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Clamp(in DVector3 value, in DVector3 min, in DVector3 max)
        => new(
            DMath.Clamp(value.X, min.X, max.X),
            DMath.Clamp(value.Y, min.Y, max.Y),
            DMath.Clamp(value.Z, min.Z, max.Z));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Min(in DVector3 a, in DVector3 b)
        => new(
            Math.Min(a.X, b.X),
            Math.Min(a.Y, b.Y),
            Math.Min(a.Z, b.Z));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Max(in DVector3 a, in DVector3 b)
        => new(
            Math.Max(a.X, b.X),
            Math.Max(a.Y, b.Y),
            Math.Max(a.Z, b.Z));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Abs(in DVector3 v)
        => new(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(in DVector3 a, in DVector3 b) => (a - b).Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double DistanceSquared(in DVector3 a, in DVector3 b) => (a - b).LengthSquared;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Reflect(in DVector3 vector, in DVector3 normal)
        => vector - 2.0 * Dot(vector, normal) * normal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Project(in DVector3 vector, in DVector3 normal)
    {
        var n = normal.Normalized();
        return Dot(vector, n) * n;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 Reject(in DVector3 vector, in DVector3 normal)
        => vector - Project(vector, normal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double AngleBetween(in DVector3 a, in DVector3 b)
    {
        var denom = a.Length * b.Length;
        if (denom <= 0.0)
            return 0.0;

        var cos = DMath.Clamp(Dot(a, b) / denom, -1.0, 1.0);
        return Math.Acos(cos);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 operator +(in DVector3 left, in DVector3 right)
        => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 operator -(in DVector3 left, in DVector3 right)
        => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 operator -(in DVector3 value)
        => new(-value.X, -value.Y, -value.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 operator *(in DVector3 left, double scalar)
        => new(left.X * scalar, left.Y * scalar, left.Z * scalar);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 operator *(double scalar, in DVector3 right)
        => right * scalar;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector3 operator /(in DVector3 left, double scalar)
        => new(left.X / scalar, left.Y / scalar, left.Z / scalar);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(DVector3 left, DVector3 right) => left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(DVector3 left, DVector3 right) => !left.Equals(right);

    public bool Equals(DVector3 other)
        => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    public override bool Equals(object? obj) => obj is DVector3 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Z})");
}
