using Veonex.UI.Layout;

namespace Veonex.UI.Core;

public sealed class UIDocument
{
	public string Name { get; set; }

	public Element Root { get; }

	public UIDocument(string name = "UIDocument")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		Name = name;

		Root = new Element
		{
			Name = "Root"
		};
	}

	public void Add(Element element)
	{
		Root.Add(element);
	}

	public bool Remove(Element element)
	{
		return Root.Remove(element);
	}

	public void Clear()
	{
		Root.Clear();
	}

	public void ResolveLayout(int width, int height)
	{
		if (width < 0)
			throw new ArgumentOutOfRangeException(nameof(width));

		if (height < 0)
			throw new ArgumentOutOfRangeException(nameof(height));

		UILayoutResolver.Resolve(
			Root,
			new UIRect(0, 0, width, height));
	}

	internal void Update(double deltaTime)
	{
		Root.UpdateTree(deltaTime);
	}
}