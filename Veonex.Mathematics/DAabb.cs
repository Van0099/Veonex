using System.Globalization;

namespace Veonex.Mathematics;

public readonly struct DAabb : IEquatable<DAabb>
{
    public DVector3 Min { get; }
    public DVector3 Max { get; }

    public DAabb(DVector3 min, DVector3 max)
    {
        Min = DVector3.Min(min, max);
        Max = DVector3.Max(min, max);
    }

    public DVector3 Center => (Min + Max) * 0.5;
    public DVector3 Size => Max - Min;
    public DVector3 Extents => Size * 0.5;

    public bool Contains(in DVector3 point)
        => point.X >= Min.X && point.X <= Max.X
        && point.Y >= Min.Y && point.Y <= Max.Y
        && point.Z >= Min.Z && point.Z <= Max.Z;

    public bool Intersects(in DAabb other)
        => Min.X <= other.Max.X && Max.X >= other.Min.X
        && Min.Y <= other.Max.Y && Max.Y >= other.Min.Y
        && Min.Z <= other.Max.Z && Max.Z >= other.Min.Z;

    public DAabb Encapsulate(in DVector3 point)
        => new(DVector3.Min(Min, point), DVector3.Max(Max, point));

    public DAabb Encapsulate(in DAabb other)
        => new(DVector3.Min(Min, other.Min), DVector3.Max(Max, other.Max));

    public bool Equals(DAabb other) => Min == other.Min && Max == other.Max;
    public override bool Equals(object? obj) => obj is DAabb other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Min, Max);

    public static bool operator ==(DAabb left, DAabb right) => left.Equals(right);
    public static bool operator !=(DAabb left, DAabb right) => !left.Equals(right);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"(Min={Min}, Max={Max})");
}
