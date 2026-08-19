using System.Collections.Concurrent;

namespace Veonex.Core;

public static class IdManager
{
    private static readonly ConcurrentDictionary<Guid, object> _objects = new();


    public static Guid Register(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        Guid id;

        do
        {
            id = Guid.NewGuid();
        }
        while (!_objects.TryAdd(id, obj));

        return id;
    }


    public static void Unregister(Guid id)
    {
        _objects.TryRemove(id, out _);
    }


    public static bool Contains(Guid id)
    {
        return _objects.ContainsKey(id);
    }


    public static object? Get(Guid id)
    {
        _objects.TryGetValue(id, out var obj);

        return obj;
    }


    public static T? Get<T>(Guid id)
        where T : class
    {
        return Get(id) as T;
    }
}