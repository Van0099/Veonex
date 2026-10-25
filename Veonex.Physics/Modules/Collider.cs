using Veonex.Core;
using Veonex.Mathematics;

namespace Veonex.Physics;

/// <summary>
/// Defines the collision geometry used by the physics simulation.
/// The geometry is generated from the MeshRenderer's MeshData.
/// </summary>
public sealed class Collider : Module
{
    private DVector3 _sizePercent = new(100.0, 100.0, 100.0);
    private DVector3 _offset = DVector3.Zero;
    private double _collisionQuality = 1.0;

    /// <summary>
    /// When enabled, collision geometry is an oriented box fitted to the mesh bounds.
    /// When disabled, the mesh's indexed triangles are used.
    /// </summary>
    public bool SimpleGeometry { get; set; }

    /// <summary>
    /// Target triangle ratio used to simplify complex collision geometry.
    /// 1.0 keeps the original indexed mesh; 0.5 targets roughly half its triangles.
    /// This setting is ignored when SimpleGeometry is enabled.
    /// Valid range: greater than 0 and at most 1.
    /// </summary>
    public double CollisionQuality
    {
        get => _collisionQuality;
        set
        {
            if (!double.IsFinite(value) || value <= 0.0 || value > 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(CollisionQuality),
                    "Collision quality must be finite, greater than zero, and at most 1.0.");
            }

            _collisionQuality = value;
        }
    }

    /// <summary>
    /// Per-axis scale of collision geometry expressed as percentages.
    /// 100 means the original mesh size, 50 means half size, and 200 means double size.
    /// </summary>
    public DVector3 SizePercent
    {
        get => _sizePercent;
        set
        {
            ValidatePositiveVector(value, nameof(SizePercent));
            _sizePercent = value;
        }
    }

    /// <summary>
    /// Local offset of the collider. The offset is affected by Transform.Scale,
    /// but is not affected by SizePercent.
    /// </summary>
    public DVector3 Offset
    {
        get => _offset;
        set
        {
            ValidateFiniteVector(value, nameof(Offset));
            _offset = value;
        }
    }

    /// <summary>
    /// If enabled, this collider reports overlap events instead of blocking movement.
    /// </summary>
    public bool IsTrigger { get; set; }

    /// <summary>
    /// Raised on this collider when it is a trigger and begins overlapping another collider.
    /// The argument is the other collider.
    /// </summary>
    public event Action<Collider>? OnTriggered;

    internal void InvokeTriggered(Collider other)
    {
        OnTriggered?.Invoke(other);
    }

    private static void ValidatePositiveVector(DVector3 value, string parameterName)
    {
        if (!double.IsFinite(value.X) ||
            !double.IsFinite(value.Y) ||
            !double.IsFinite(value.Z) ||
            value.X <= 0.0 ||
            value.Y <= 0.0 ||
            value.Z <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Values must be finite and greater than zero.");
        }
    }

    private static void ValidateFiniteVector(DVector3 value, string parameterName)
    {
        if (!double.IsFinite(value.X) ||
            !double.IsFinite(value.Y) ||
            !double.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Values must be finite.");
        }
    }
}
