namespace Veonex.UI;

public class Element
{
	private readonly Dictionary<Type, UIModule> _modules = [];
	private readonly List<Element> _children = [];

	private readonly List<UIModule> _moduleUpdateBuffer = [];
	private readonly List<Element> _childrenUpdateBuffer = [];

	public Guid Id { get; } = Guid.NewGuid();

	public string Name { get; set; } = string.Empty;

	public Element? Parent { get; private set; }

	public IReadOnlyList<Element> Children => _children;

	public IReadOnlyCollection<UIModule> Modules => _modules.Values;

	public Element()
	{
	}

	public Element(params UIModule[] modules)
	{
		ArgumentNullException.ThrowIfNull(modules);

		foreach (UIModule module in modules)
			Add(module);
	}

	public Element(string name, params UIModule[] modules)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(modules);

		Name = name;

		foreach (UIModule module in modules)
			Add(module);
	}

	public T Add<T>()
		where T : UIModule, new()
	{
		return Add(new T());
	}

	public T Add<T>(Action<T> configure)
		where T : UIModule, new()
	{
		ArgumentNullException.ThrowIfNull(configure);

		T module = new();

		configure(module);

		return Add(module);
	}

	public T Add<T>(T module)
		where T : UIModule
	{
		ArgumentNullException.ThrowIfNull(module);

		Type type = module.GetType();

		if (_modules.ContainsKey(type))
		{
			throw new InvalidOperationException(
				$"Element already contains module {type.Name}.");
		}

		if (module.Element is not null)
		{
			throw new InvalidOperationException(
				$"Module {type.Name} already belongs to an Element.");
		}

		module.Element = this;

		_modules.Add(type, module);

		try
		{
			module.OnAdded();
		}
		catch
		{
			_modules.Remove(type);
			module.Element = null!;

			throw;
		}

		return module;
	}

	public bool Has<T>()
		where T : UIModule
	{
		return _modules.ContainsKey(typeof(T));
	}

	public T Get<T>()
		where T : UIModule
	{
		if (_modules.TryGetValue(typeof(T), out UIModule? module))
			return (T)module;

		throw new InvalidOperationException(
			$"Element does not contain module {typeof(T).Name}.");
	}

	public bool TryGet<T>(out T? module)
		where T : UIModule
	{
		if (_modules.TryGetValue(typeof(T), out UIModule? existing))
		{
			module = (T)existing;
			return true;
		}

		module = null;
		return false;
	}

	public bool Remove<T>()
		where T : UIModule
	{
		Type type = typeof(T);

		if (!_modules.Remove(type, out UIModule? module))
			return false;

		try
		{
			module.OnRemoved();
		}
		finally
		{
			module.Element = null!;
		}

		return true;
	}

	public Element AddChild(Element child)
	{
		ArgumentNullException.ThrowIfNull(child);

		if (ReferenceEquals(child, this))
		{
			throw new InvalidOperationException(
				"An Element cannot be its own child.");
		}

		if (child.Parent is not null)
		{
			throw new InvalidOperationException(
				"The Element already has a parent.");
		}

		for (Element? current = this;
			 current is not null;
			 current = current.Parent)
		{
			if (ReferenceEquals(current, child))
			{
				throw new InvalidOperationException(
					"Adding this child would create a hierarchy cycle.");
			}
		}

		_children.Add(child);
		child.Parent = this;

		return child;
	}

	public bool RemoveChild(Element child)
	{
		ArgumentNullException.ThrowIfNull(child);

		if (!_children.Remove(child))
			return false;

		child.Parent = null;

		return true;
	}

	public void ClearChildren()
	{
		foreach (Element child in _children)
			child.Parent = null;

		_children.Clear();
	}

	internal void UpdateTree(double deltaTime)
	{
		_moduleUpdateBuffer.Clear();
		_moduleUpdateBuffer.AddRange(_modules.Values);

		foreach (UIModule module in _moduleUpdateBuffer)
		{
			if (ReferenceEquals(module.Element, this) &&
				_modules.TryGetValue(
					module.GetType(),
					out UIModule? current) &&
				ReferenceEquals(current, module))
			{
				module.Update(deltaTime);
			}
		}

		_childrenUpdateBuffer.Clear();
		_childrenUpdateBuffer.AddRange(_children);

		foreach (Element child in _childrenUpdateBuffer)
		{
			if (ReferenceEquals(child.Parent, this))
				child.UpdateTree(deltaTime);
		}
	}
}