using Veonex.Mathematics;

namespace Veonex.Core;

public sealed class Transform : Module
{
    private DVector3 _position = DVector3.Zero;
    private DQuaternion _rotation = DQuaternion.Identity;
    private DVector3 _scale = DVector3.One;

    private DMatrix4x4 _localMatrix;


    public Transform()
    {
        UpdateMatrix();
    }


    public DVector3 Position
    {
        get => _position;
        set
        {
            _position = value;
            UpdateMatrix();
        }
    }


    public DVector3 Rotation
    {
        get => _rotation.ToEulerDegrees();

        set
        {
            _rotation = DQuaternion.CreateFromEulerDegrees(value);
            UpdateMatrix();
        }
    }


    public DQuaternion Quaternion
    {
        get => _rotation;

        set
        {
            _rotation = value.Normalized();
            UpdateMatrix();
        }
    }


    public DVector3 Scale
    {
        get => _scale;

        set
        {
            _scale = value;
            UpdateMatrix();
        }
    }


    public DMatrix4x4 LocalMatrix => _localMatrix;


    private void UpdateMatrix()
    {
        _localMatrix = DMatrix4x4.CreateWorld(
            _position,
            _rotation,
            _scale);
    }
}