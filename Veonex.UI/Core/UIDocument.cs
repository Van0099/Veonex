namespace Veonex.UI.Core;

public sealed class UIDocument
{
	public Guid Id { get; } = Guid.NewGuid();

	public string Name { get; set; } = "UIDocument";

	public Element Root { get; }

	public UIDocument()
	{
		Root = new Element("Root");
	}

	public UIDocument(Element root)
	{
		ArgumentNullException.ThrowIfNull(root);

		if (root.Parent is not null)
		{
			throw new InvalidOperationException(
				"The root Element cannot have a parent.");
		}

		Root = root;
	}

	public void Update(double deltaTime)
	{
		if (!double.IsFinite(deltaTime) || deltaTime < 0.0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(deltaTime));
		}

		Root.UpdateTree(deltaTime);
	}
}