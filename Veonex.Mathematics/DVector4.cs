using System.Globalization;
using System.Runtime.CompilerServices;

namespace Veonex.Mathematics;

public readonly struct DVector4 : IEquatable<DVector4>
{
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
    public double W { get; }

    public DVector4(double x, double y, double z, double w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public static DVector4 Zero => new(0.0, 0.0, 0.0, 0.0);
    public static DVector4 One => new(1.0, 1.0, 1.0, 1.0);
    public static DVector4 UnitX => new(1.0, 0.0, 0.0, 0.0);
    public static DVector4 UnitY => new(0.0, 1.0, 0.0, 0.0);
    public static DVector4 UnitZ => new(0.0, 0.0, 1.0, 0.0);
    public static DVector4 UnitW => new(0.0, 0.0, 0.0, 1.0);

    public double Length => Math.Sqrt(LengthSquared);
    public double LengthSquared => X * X + Y * Y + Z * Z + W * W;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector4 Normalized()
    {
        var length = Length;
        if (length <= 0.0)
            return Zero;

        return this / length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(in DVector4 a, in DVector4 b)
        => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector4 Lerp(in DVector4 a, in DVector4 b, double t)
        => new(
            DMath.Lerp(a.X, b.X, t),
            DMath.Lerp(a.Y, b.Y, t),
            DMath.Lerp(a.Z, b.Z, t),
            DMath.Lerp(a.W, b.W, t));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector4 FromVector3(in DVector3 value, double w = 1.0)
        => new(value.X, value.Y, value.Z, w);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector4 operator +(in DVector4 left, in DVector4 right)
        => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z, left.W + right.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector4 operator -(in DVector4 left, in DVector4 right)
        => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z, left.W - right.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector4 operator -(in DVector4 value)
        => new(-value.X, -value.Y, -value.Z, -value.W);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector4 operator *(in DVector4 left, double scalar)
        => new(left.X * scalar, left.Y * scalar, left.Z * scalar, left.W * scalar);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector4 operator *(double scalar, in DVector4 right)
        => right * scalar;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector4 operator /(in DVector4 left, double scalar)
        => new(left.X / scalar, left.Y / scalar, left.Z / scalar, left.W / scalar);

    public bool Equals(DVector4 other)
        => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);

    public override bool Equals(object? obj) => obj is DVector4 other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);

    public static bool operator ==(DVector4 left, DVector4 right) => left.Equals(right);
    public static bool operator !=(DVector4 left, DVector4 right) => !left.Equals(right);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y}, {Z}, {W})");
}
