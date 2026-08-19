using System.Numerics;

namespace Veonex.Mathematics;

public static class Conversions
{
    public static Vector2 ToSystemVector2(this DVector2 value)
        => new((float)value.X, (float)value.Y);

    public static Vector3 ToSystemVector3(this DVector3 value)
        => new((float)value.X, (float)value.Y, (float)value.Z);

    public static Vector4 ToSystemVector4(this DVector4 value)
        => new((float)value.X, (float)value.Y, (float)value.Z, (float)value.W);

    public static Quaternion ToSystemQuaternion(this DQuaternion value)
        => new((float)value.X, (float)value.Y, (float)value.Z, (float)value.W);

    public static Matrix4x4 ToSystemMatrix4x4(this DMatrix4x4 value)
        => new(
            (float)value.M11, (float)value.M12, (float)value.M13, (float)value.M14,
            (float)value.M21, (float)value.M22, (float)value.M23, (float)value.M24,
            (float)value.M31, (float)value.M32, (float)value.M33, (float)value.M34,
            (float)value.M41, (float)value.M42, (float)value.M43, (float)value.M44);

    public static DVector2 ToDVector2(this Vector2 value)
        => new(value.X, value.Y);

    public static DVector3 ToDVector3(this Vector3 value)
        => new(value.X, value.Y, value.Z);

    public static DVector4 ToDVector4(this Vector4 value)
        => new(value.X, value.Y, value.Z, value.W);

    public static DQuaternion ToDQuaternion(this Quaternion value)
        => new(value.X, value.Y, value.Z, value.W);

    public static DMatrix4x4 ToDMatrix4x4(this Matrix4x4 value)
        => new(
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44);
}
