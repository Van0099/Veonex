using System.Collections.ObjectModel;

namespace Veonex.Core;

public sealed class Scene
{
    private readonly List<Entity> _entities = [];
    private readonly Dictionary<string, Entity> _entityMap = new(StringComparer.Ordinal);

    private int _entityCounter;


    public ReadOnlyCollection<Entity> Entities => _entities.AsReadOnly();


    public Entity Add(string? name = null)
    {
        name = CreateUniqueName(name);

        var entity = new Entity(this, name);

        _entities.Add(entity);
        _entityMap.Add(name, entity);

        return entity;
    }


    public bool Remove(Entity entity)
    {
        if (!_entities.Remove(entity))
            return false;

        _entityMap.Remove(entity.Name);

        IdManager.Unregister(entity.Id);

        return true;
    }


    public Entity? Find(string name)
    {
        return _entityMap.GetValueOrDefault(name);
    }


    public bool Contains(string name)
    {
        return _entityMap.ContainsKey(name);
    }


    internal void Rename(Entity entity, string newName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        if (entity.Name == newName)
            return;

        if (_entityMap.ContainsKey(newName))
            throw new InvalidOperationException(
                $"Entity \"{newName}\" already exists.");

        _entityMap.Remove(entity.Name);

        entity.SetName(newName);

        _entityMap.Add(newName, entity);
    }


    private string CreateUniqueName(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            if (_entityMap.ContainsKey(name))
                throw new InvalidOperationException(
                    $"Entity \"{name}\" already exists.");

            return name;
        }

        string generated;

        do
        {
            generated = _entityCounter == 0
                ? "Entity"
                : $"Entity_{_entityCounter}";

            _entityCounter++;
        }
        while (_entityMap.ContainsKey(generated));

        return generated;
    }
}