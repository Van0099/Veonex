using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;

namespace Veonex.Physics;

/// <summary>
/// Maps Bepu collidable handles back to Veonex colliders and stores grounding contacts.
/// Bepu performs broad-phase and narrow-phase geometry detection itself.
/// </summary>
internal sealed class CollisionDetector
{
    private readonly Dictionary<CollidableKey, Collider> _colliders = [];
    private readonly HashSet<int> _groundedBodyHandles = [];

    public void RegisterBody(BodyHandle handle, Collider collider)
    {
        _colliders[new CollidableKey(CollidableMobility.Dynamic, handle.Value)] = collider;
    }

    public void RegisterStatic(StaticHandle handle, Collider collider)
    {
        _colliders[new CollidableKey(CollidableMobility.Static, handle.Value)] = collider;
    }

    public void UnregisterBody(BodyHandle handle)
    {
        _colliders.Remove(new CollidableKey(CollidableMobility.Dynamic, handle.Value));
        _groundedBodyHandles.Remove(handle.Value);
    }

    public void UnregisterStatic(StaticHandle handle)
    {
        _colliders.Remove(new CollidableKey(CollidableMobility.Static, handle.Value));
    }

    public void ClearGrounded()
    {
        _groundedBodyHandles.Clear();
    }

    public bool IsGrounded(BodyHandle handle)
        => _groundedBodyHandles.Contains(handle.Value);

    internal bool TryGetCollider(CollidableReference reference, out Collider collider)
        => _colliders.TryGetValue(CreateKey(reference), out collider!);

    internal void MarkGrounded(CollidableReference reference)
    {
        if (reference.Mobility != CollidableMobility.Static)
            _groundedBodyHandles.Add(reference.BodyHandle.Value);
    }

    private static CollidableKey CreateKey(CollidableReference reference)
    {
        int handle = reference.Mobility == CollidableMobility.Static
            ? reference.StaticHandle.Value
            : reference.BodyHandle.Value;

        return new CollidableKey(reference.Mobility, handle);
    }

    private readonly record struct CollidableKey(CollidableMobility Mobility, int Handle);
}
