using System.Globalization;
using System.Runtime.CompilerServices;

namespace Veonex.Mathematics;

public readonly struct DPlane : IEquatable<DPlane>
{
    public DVector3 Normal { get; }
    public double D { get; }

    public DPlane(DVector3 normal, double d)
    {
        Normal = normal.Normalized();
        D = d;
    }

    public DPlane(DVector3 normal, DVector3 point)
    {
        Normal = normal.Normalized();
        D = -DVector3.Dot(Normal, point);
    }

    public double SignedDistanceTo(in DVector3 point) => DVector3.Dot(Normal, point) + D;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 ProjectPoint(in DVector3 point)
        => point - SignedDistanceTo(point) * Normal;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 ReflectPoint(in DVector3 point)
        => point - 2.0 * SignedDistanceTo(point) * Normal;

    public bool Equals(DPlane other) => Normal == other.Normal && D.Equals(other.D);
    public override bool Equals(object? obj) => obj is DPlane other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Normal, D);

    public static bool operator ==(DPlane left, DPlane right) => left.Equals(right);
    public static bool operator !=(DPlane left, DPlane right) => !left.Equals(right);

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"(N={Normal}, D={D})");
}
