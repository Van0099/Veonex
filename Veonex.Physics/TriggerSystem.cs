namespace Veonex.Physics;

/// <summary>
/// Tracks trigger overlaps over fixed simulation steps and raises OnTriggered only
/// when an overlap begins, not on every frame spent overlapping.
/// </summary>
internal sealed class TriggerSystem
{
    private Dictionary<TriggerPairKey, TriggerPair> _previous = [];
    private Dictionary<TriggerPairKey, TriggerPair> _current = [];

    public void BeginStep()
    {
        _current.Clear();
    }

    public void RecordOverlap(Collider first, Collider second)
    {
        if (!first.IsTrigger && !second.IsTrigger)
            return;

        Guid firstId = first.Entity.Id;
        Guid secondId = second.Entity.Id;

        if (firstId == secondId)
            return;

        TriggerPairKey key;
        TriggerPair pair;

        if (firstId.CompareTo(secondId) < 0)
        {
            key = new TriggerPairKey(firstId, secondId);
            pair = new TriggerPair(first, second);
        }
        else
        {
            key = new TriggerPairKey(secondId, firstId);
            pair = new TriggerPair(second, first);
        }

        _current.TryAdd(key, pair);
    }

    public void EndStep()
    {
        foreach ((TriggerPairKey key, TriggerPair pair) in _current)
        {
            if (_previous.ContainsKey(key))
                continue;

            if (pair.First.IsTrigger)
                pair.First.InvokeTriggered(pair.Second);

            if (pair.Second.IsTrigger)
                pair.Second.InvokeTriggered(pair.First);
        }

        // Swap dictionaries to avoid allocating a new pair collection every step.
        Dictionary<TriggerPairKey, TriggerPair> oldPrevious = _previous;
        _previous = _current;
        _current = oldPrevious;
        _current.Clear();
    }

    public void RemoveEntity(Guid entityId)
    {
        RemoveMatching(_previous, entityId);
        RemoveMatching(_current, entityId);
    }

    private static void RemoveMatching(
        Dictionary<TriggerPairKey, TriggerPair> pairs,
        Guid entityId)
    {
        foreach (TriggerPairKey key in pairs.Keys.ToArray())
        {
            if (key.First == entityId || key.Second == entityId)
                pairs.Remove(key);
        }
    }

    private readonly record struct TriggerPairKey(Guid First, Guid Second);
    private readonly record struct TriggerPair(Collider First, Collider Second);
}
