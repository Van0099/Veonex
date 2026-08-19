namespace Veonex.Core;

public sealed class Entity
{
    private readonly Dictionary<Type, Module> _modules = [];
    public Guid Id { get; }

    internal Entity(Scene scene, string name)
    {
        Scene = scene;
        Name = name;

        Id = IdManager.Register(this);
    }


    public Scene Scene { get; }


    public string Name { get; private set; }


    internal void SetName(string name)
    {
        Name = name;
    }


    public void Rename(string name)
    {
        Scene.Rename(this, name);
    }


    public T Add<T>()
        where T : Module, new()
    {
        var type = typeof(T);

        if (_modules.ContainsKey(type))
            throw new InvalidOperationException(
                $"Entity already contains module {type.Name}.");

        var module = new T
        {
            Entity = this
        };

        _modules.Add(type, module);

        module.OnAdded();

        return module;
    }


    public bool Has<T>()
        where T : Module
    {
        return _modules.ContainsKey(typeof(T));
    }


    public T Get<T>()
        where T : Module
    {
        if (_modules.TryGetValue(typeof(T), out var module))
            return (T)module;

        throw new InvalidOperationException(
            $"Entity does not contain module {typeof(T).Name}.");
    }


    internal IEnumerable<Module> Modules => _modules.Values;
}