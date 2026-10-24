using System.Numerics;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuUtilities;

namespace Veonex.Physics;

/// <summary>
/// Supplies contact material settings, grounding detection, and trigger overlap reporting to Bepu.
/// </summary>
internal struct PhysicsNarrowPhaseCallbacks : INarrowPhaseCallbacks
{
    private const float SpeculativeMargin = 0.01f;
    private readonly CollisionDetector _detector;
    private readonly TriggerSystem _triggers;

    public PhysicsNarrowPhaseCallbacks(
        CollisionDetector detector,
        TriggerSystem triggers)
    {
        _detector = detector;
        _triggers = triggers;
    }

    public void Initialize(Simulation simulation)
    {
    }

    public bool AllowContactGeneration(
        int workerIndex,
        CollidableReference a,
        CollidableReference b,
        ref float speculativeMargin)
    {
        // Two stationary colliders cannot move into one another. A trigger still detects
        // overlaps as long as at least one object has a PhysicsBody.
        return a.Mobility != CollidableMobility.Static ||
               b.Mobility != CollidableMobility.Static;
    }

    public bool AllowContactGeneration(
        int workerIndex,
        CollidablePair pair,
        int childIndexA,
        int childIndexB)
    {
        return true;
    }

    public bool ConfigureContactManifold<TManifold>(
        int workerIndex,
        CollidablePair pair,
        ref TManifold manifold,
        out PairMaterialProperties pairMaterial)
        where TManifold : unmanaged, IContactManifold<TManifold>
    {
        pairMaterial = new PairMaterialProperties
        {
            FrictionCoefficient = 1.0f,
            MaximumRecoveryVelocity = 2.0f,
            SpringSettings = new SpringSettings(30.0f, 1.0f)
        };

        if (!_detector.TryGetCollider(pair.A, out Collider colliderA) ||
            !_detector.TryGetCollider(pair.B, out Collider colliderB))
        {
            return true;
        }

        if (colliderA.IsTrigger || colliderB.IsTrigger)
        {
            _triggers.RecordOverlap(colliderA, colliderB);
            return false; // Keep overlap detection, but do not create a physical constraint.
        }

        for (int i = 0; i < manifold.Count; i++)
        {
            manifold.GetContact(
                i,
                out _,
                out Vector3 normal,
                out float depth,
                out _);

            if (depth < -SpeculativeMargin || MathF.Abs(normal.Y) < 0.55f)
                continue;

            _detector.MarkGrounded(pair.A);
            _detector.MarkGrounded(pair.B);
            break;
        }

        return true;
    }

    public bool ConfigureContactManifold(
        int workerIndex,
        CollidablePair pair,
        int childIndexA,
        int childIndexB,
        ref ConvexContactManifold manifold)
    {
        return true;
    }

    public void Dispose()
    {
    }
}
