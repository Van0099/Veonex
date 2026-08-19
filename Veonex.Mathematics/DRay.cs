using System.Globalization;
using System.Runtime.CompilerServices;

namespace Veonex.Mathematics;

public readonly struct DRay
{
    public DVector3 Origin { get; }
    public DVector3 Direction { get; }

    public DRay(DVector3 origin, DVector3 direction)
    {
        Origin = origin;
        Direction = direction.Normalized();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DVector3 GetPoint(double distance) => Origin + Direction * distance;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(DPlane plane, out double distance)
    {
        var denom = DVector3.Dot(plane.Normal, Direction);
        if (Math.Abs(denom) < 1e-12)
        {
            distance = 0.0;
            return false;
        }

        distance = -(DVector3.Dot(plane.Normal, Origin) + plane.D) / denom;
        return distance >= 0.0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Intersects(DAabb box, out double distance)
    {
        distance = 0.0;

        double tmin = double.NegativeInfinity;
        double tmax = double.PositiveInfinity;

        static bool UpdateSlab(double origin, double direction, double min, double max, ref double tmin, ref double tmax)
        {
            if (Math.Abs(direction) < 1e-12)
                return origin >= min && origin <= max;

            var inv = 1.0 / direction;
            var t1 = (min - origin) * inv;
            var t2 = (max - origin) * inv;

            if (t1 > t2)
                (t1, t2) = (t2, t1);

            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            return tmin <= tmax;
        }

        if (!UpdateSlab(Origin.X, Direction.X, box.Min.X, box.Max.X, ref tmin, ref tmax))
            return false;
        if (!UpdateSlab(Origin.Y, Direction.Y, box.Min.Y, box.Max.Y, ref tmin, ref tmax))
            return false;
        if (!UpdateSlab(Origin.Z, Direction.Z, box.Min.Z, box.Max.Z, ref tmin, ref tmax))
            return false;

        distance = tmin >= 0.0 ? tmin : tmax;
        return distance >= 0.0;
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"(Origin={Origin}, Direction={Direction})");
}
