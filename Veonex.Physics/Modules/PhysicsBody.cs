using Veonex.Core;

namespace Veonex.Physics;

/// <summary>
/// Makes an entity movable by the physics simulation.
/// An entity with a Collider but without PhysicsBody is stationary.
/// </summary>
public sealed class PhysicsBody : Module
{
    private double _mass = 1.0;
    private double _drag = 0.05;
    private double _angularDrag = 0.05;

    public double Mass
    {
        get => _mass;
        set
        {
            if (!double.IsFinite(value) || value <= 0.0 || value > float.MaxValue)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Mass must be finite, greater than zero, and representable as a float.");
            }

            _mass = value;
        }
    }

    /// <summary>
    /// Linear velocity damping. Zero means no drag.
    /// </summary>
    public double Drag
    {
        get => _drag;
        set => _drag = ValidateNonNegative(value, nameof(Drag));
    }

    /// <summary>
    /// Angular velocity damping. Zero means no angular drag.
    /// </summary>
    public double AngularDrag
    {
        get => _angularDrag;
        set => _angularDrag = ValidateNonNegative(value, nameof(AngularDrag));
    }

    public bool UseGravity { get; set; } = true;

    private static double ValidateNonNegative(double value, string propertyName)
    {
        if (!double.IsFinite(value) || value < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                propertyName,
                "Value must be finite and non-negative.");
        }

        return value;
    }
}
