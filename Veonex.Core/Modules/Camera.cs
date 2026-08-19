using Veonex.Mathematics;

namespace Veonex.Core;

public sealed class Camera : Module
{
    private double _fieldOfView = 60.0;
    private double _nearClip = 0.01;
    private double _farClip = 1000.0;
    private double _aspectRatio = 16.0 / 9.0;

    public double FieldOfView
    {
        get => _fieldOfView;
        set => _fieldOfView = value;
    }

    public double NearClip
    {
        get => _nearClip;
        set => _nearClip = value;
    }

    public double FarClip
    {
        get => _farClip;
        set => _farClip = value;
    }

    public double AspectRatio
    {
        get => _aspectRatio;
        set => _aspectRatio = value;
    }

    public DMatrix4x4 GetViewMatrix()
    {
        var transform = Entity.Get<Transform>();

        var position = transform.Position;
        var rotation = transform.Quaternion;

        var forward = rotation * DVector3.Forward;

        return DMatrix4x4.CreateLookAt(
            position,
            position + forward,
            DVector3.Up);
    }

    public DMatrix4x4 GetProjectionMatrix()
    {
        return DMatrix4x4.CreatePerspectiveFieldOfView(
            FieldOfView * Math.PI / 180.0,
            AspectRatio,
            NearClip,
            FarClip);
    }
}