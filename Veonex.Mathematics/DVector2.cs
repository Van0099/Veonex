using System.Globalization;
using System.Runtime.CompilerServices;

namespace Veonex.Mathematics;

public readonly struct DVector2 : IEquatable<DVector2>
{
    public double X { get; }
    public double Y { get; }

    public DVector2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static DVector2 Zero => new(0.0, 0.0);
    public static DVector2 One => new(1.0, 1.0);
    public static DVector2 UnitX => new(1.0, 0.0);
    public static DVector2 UnitY => new(0.0, 1.0);

    public double Length => Math.Sqrt(LengthSquared);
    public double LengthSquared => X * X + Y * Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector2 Normalized()
    {
        var length = Length;
        if (length <= 0.0)
            return Zero;

        return this / length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Dot(in DVector2 a, in DVector2 b) => a.X * b.X + a.Y * b.Y;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 Lerp(in DVector2 a, in DVector2 b, double t)
        => new(DMath.Lerp(a.X, b.X, t), DMath.Lerp(a.Y, b.Y, t));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Distance(in DVector2 a, in DVector2 b) => (a - b).Length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double DistanceSquared(in DVector2 a, in DVector2 b) => (a - b).LengthSquared;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 Min(in DVector2 a, in DVector2 b)
        => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 Max(in DVector2 a, in DVector2 b)
        => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 Abs(in DVector2 v)
        => new(Math.Abs(v.X), Math.Abs(v.Y));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 Clamp(in DVector2 value, in DVector2 min, in DVector2 max)
        => new(DMath.Clamp(value.X, min.X, max.X), DMath.Clamp(value.Y, min.Y, max.Y));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 operator +(in DVector2 left, in DVector2 right)
        => new(left.X + right.X, left.Y + right.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 operator -(in DVector2 left, in DVector2 right)
        => new(left.X - right.X, left.Y - right.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 operator -(in DVector2 value)
        => new(-value.X, -value.Y);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 operator *(in DVector2 left, double scalar)
        => new(left.X * scalar, left.Y * scalar);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 operator *(double scalar, in DVector2 right)
        => right * scalar;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DVector2 operator /(in DVector2 left, double scalar)
        => new(left.X / scalar, left.Y / scalar);

    public bool Equals(DVector2 other) => X.Equals(other.X) && Y.Equals(other.Y);
    public override bool Equals(object? obj) => obj is DVector2 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y);

    public static bool operator ==(DVector2 left, DVector2 right) => left.Equals(right);
    public static bool operator !=(DVector2 left, DVector2 right) => !left.Equals(right);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"({X}, {Y})");
}
