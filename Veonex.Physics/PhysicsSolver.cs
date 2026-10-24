using BepuPhysics;
using BepuPhysics.Constraints;
using BepuUtilities.Memory;
using Veonex.Mathematics;

namespace Veonex.Physics;

/// <summary>
/// Owns the Bepu simulation and performs a single fixed-time physics step.
/// </summary>
internal sealed class PhysicsSolver : IDisposable
{
    private readonly Simulation _simulation;
    private readonly CollisionDetector _collisionDetector;
    private readonly TriggerSystem _triggerSystem;
    private bool _disposed;

    public Simulation Simulation => _simulation;

    public PhysicsSolver(
        BufferPool bufferPool,
        CollisionDetector collisionDetector,
        TriggerSystem triggerSystem)
    {
        _collisionDetector = collisionDetector;
        _triggerSystem = triggerSystem;

        _simulation = Simulation.Create(
            bufferPool,
            new PhysicsNarrowPhaseCallbacks(collisionDetector, triggerSystem),
            new PhysicsPoseIntegratorCallbacks(),
            new SolveDescription(8, 1));
    }

    public void Step(
        IEnumerable<PhysicsObject> objects,
        DVector3 gravity,
        double fixedDeltaTime)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _collisionDetector.ClearGrounded();
        _triggerSystem.BeginStep();

        PhysicsIntegrator.ApplyForces(
            _simulation,
            objects,
            gravity,
            (float)fixedDeltaTime);

        _simulation.Timestep((float)fixedDeltaTime);

        _triggerSystem.EndStep();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _simulation.Dispose();
    }
}
