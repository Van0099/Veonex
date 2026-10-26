namespace Veonex.UI.Core;

public sealed class UIDocument
{
	public string Name { get; set; }

	public VisualElement Root { get; }

	public UIDocument(string name = "UIDocument")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		Name = name;

		Root = new VisualElement
		{
			Name = "Root"
		};
	}

	public void Add(VisualElement element)
	{
		Root.Add(element);
	}

	public bool Remove(VisualElement element)
	{
		return Root.Remove(element);
	}

	public void Clear()
	{
		Root.Clear();
	}

	internal void Update(double deltaTime)
	{
		Root.UpdateTree(deltaTime);
	}
}