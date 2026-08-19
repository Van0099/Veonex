using System.Runtime.CompilerServices;

namespace Veonex.Mathematics;

public static class DMath
{
    public const double Pi = Math.PI;
    public const double TwoPi = Math.PI * 2.0;
    public const double HalfPi = Math.PI * 0.5;
    public const double Deg2Rad = Math.PI / 180.0;
    public const double Rad2Deg = 180.0 / Math.PI;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToRadians(double degrees) => degrees * Deg2Rad;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToDegrees(double radians) => radians * Rad2Deg;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp01(double value) => Clamp(value, 0.0, 1.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double InverseLerp(double a, double b, double value)
    {
        if (a == b)
            return 0.0;
        return (value - a) / (b - a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SmoothStep(double from, double to, double t)
    {
        t = Clamp01(t);
        t = t * t * (3.0 - 2.0 * t);
        return Lerp(from, to, t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double MoveTowards(double current, double target, double maxDelta)
    {
        var delta = target - current;
        if (Abs(delta) <= maxDelta)
            return target;
        return current + Sign(delta) * maxDelta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Repeat(double t, double length)
    {
        if (length == 0.0)
            return 0.0;
        return t - Math.Floor(t / length) * length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double DeltaAngle(double current, double target)
    {
        var delta = Repeat((target - current), TwoPi);
        if (delta > Math.PI)
            delta -= TwoPi;
        return delta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Approximately(double a, double b, double epsilon = 1e-12)
        => Abs(a - b) <= epsilon;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Abs(double value) => Math.Abs(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Min(double a, double b) => Math.Min(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Max(double a, double b) => Math.Max(a, b);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Sign(double value) => Math.Sign(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sqrt(double value) => Math.Sqrt(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Sin(double value) => Math.Sin(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Cos(double value) => Math.Cos(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Tan(double value) => Math.Tan(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Acos(double value) => Math.Acos(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Asin(double value) => Math.Asin(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Atan2(double y, double x) => Math.Atan2(y, x);
}
