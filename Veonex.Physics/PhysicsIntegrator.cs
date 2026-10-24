using System.Numerics;
using BepuPhysics;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;
using Veonex.Mathematics;
using NVector3 = System.Numerics.Vector3;

namespace Veonex.Physics;

/// <summary>
/// Applies entity-specific gravity and velocity drag before each fixed Bepu step.
/// The Bepu pose-integrator callback itself is intentionally gravity-free so that
/// UseGravity can be configured independently for every PhysicsBody.
/// </summary>
internal static class PhysicsIntegrator
{
    public static void ApplyForces(
        Simulation simulation,
        IEnumerable<PhysicsObject> objects,
        DVector3 gravity,
        float deltaTime)
    {
        NVector3 gravityVector = ToVector3(gravity);

        foreach (PhysicsObject obj in objects)
        {
            if (obj.BodyModule is not PhysicsBody settings)
                continue;

            BodyReference body = simulation.Bodies[obj.BodyHandle];

            // Sleeping bodies are awakened by Bepu when a relevant collision occurs.
            if (!body.Awake)
                continue;

            NVector3 linearVelocity = body.Velocity.Linear;
            NVector3 angularVelocity = body.Velocity.Angular;

            if (settings.UseGravity)
                linearVelocity += gravityVector * deltaTime;

            float linearDamping = (float)Math.Exp(-settings.Drag * deltaTime);
            float angularDamping = (float)Math.Exp(-settings.AngularDrag * deltaTime);

            body.Velocity.Linear = linearVelocity * linearDamping;
            body.Velocity.Angular = angularVelocity * angularDamping;
        }
    }

    private static NVector3 ToVector3(DVector3 value)
        => new((float)value.X, (float)value.Y, (float)value.Z);
}

/// <summary>
/// Bepu still integrates body poses and solves constraints, while external acceleration
/// and damping are applied by PhysicsIntegrator.ApplyForces above.
/// </summary>
internal struct PhysicsPoseIntegratorCallbacks : IPoseIntegratorCallbacks
{
    public readonly AngularIntegrationMode AngularIntegrationMode
        => AngularIntegrationMode.Nonconserving;

    public readonly bool AllowSubstepsForUnconstrainedBodies => false;

    public readonly bool IntegrateVelocityForKinematics => false;

    public void Initialize(Simulation simulation)
    {
    }

    public void PrepareForIntegration(float dt)
    {
    }

    public void IntegrateVelocity(
        Vector<int> bodyIndices,
        Vector3Wide position,
        QuaternionWide orientation,
        BodyInertiaWide localInertia,
        Vector<int> integrationMask,
        int workerIndex,
        Vector<float> dt,
        ref BodyVelocityWide velocity)
    {
        // No-op: gravity, Drag, and AngularDrag are handled per PhysicsBody before the step.
    }
}
