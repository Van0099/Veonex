namespace Veonex.Mathematics;

public sealed class DTransform
{
    public DVector3 Position { get; set; }
    public DQuaternion Rotation { get; set; } = DQuaternion.Identity;
    public DVector3 Scale { get; set; } = DVector3.One;

    public DTransform()
    {
    }

    public DTransform(DVector3 position, DQuaternion rotation, DVector3 scale)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
    }

    public DMatrix4x4 ToMatrix()
        => DMatrix4x4.CreateScale(Scale) * DMatrix4x4.CreateFromQuaternion(Rotation) * DMatrix4x4.CreateTranslation(Position);

    public DMatrix4x4 ToMatrixRelativeTo(in DVector3 origin)
        => DMatrix4x4.CreateScale(Scale) * DMatrix4x4.CreateFromQuaternion(Rotation) * DMatrix4x4.CreateTranslation(Position - origin);

    public override string ToString() => $"Position={Position}, Rotation={Rotation}, Scale={Scale}";
}
