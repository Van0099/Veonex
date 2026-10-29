using System.Numerics;
using Veonex.UI.Layout;
using Veonex.UI.Styles;

namespace Veonex.UI.Core;

public class Element
{
	private readonly List<Element> _children = [];

	public string Name { get; set; } = string.Empty;

	public Element? Parent { get; private set; }

	public IReadOnlyList<Element> Children => _children;

	public Vector2 Position { get; set; } = Vector2.Zero;

	public Vector2 Size { get; set; } = Vector2.Zero;

	public UIMargin Margin { get; set; } = UIMargin.Zero;

	public UIMargin Padding { get; set; } = UIMargin.Zero;

	public UIStyle Style { get; set; } = new();

	public UIHorizontalAlignment HorizontalAlignment { get; set; }
		= UIHorizontalAlignment.Left;

	public UIVerticalAlignment VerticalAlignment { get; set; }
		= UIVerticalAlignment.Top;

	public UIChildrenLayout ChildrenLayout { get; set; }
		= UIChildrenLayout.None;

	public float ChildSpacing { get; set; }

	public bool Visible { get; set; } = true;

	public bool Enabled { get; set; } = true;

	public UIRect LayoutRect { get; private set; } = UIRect.Empty;

	public void Add(Element child)
	{
		ArgumentNullException.ThrowIfNull(child);

		for (Element? current = this;
			 current is not null;
			 current = current.Parent)
		{
			if (ReferenceEquals(current, child))
			{
				throw new InvalidOperationException(
					"An element cannot be added to itself or its descendants.");
			}
		}

		if (ReferenceEquals(child.Parent, this))
			return;

		child.Parent?._children.Remove(child);

		child.Parent = this;
		_children.Add(child);
	}

	public bool Remove(Element child)
	{
		ArgumentNullException.ThrowIfNull(child);

		if (!_children.Remove(child))
			return false;

		child.Parent = null;
		return true;
	}

	public void Clear()
	{
		foreach (Element child in _children)
			child.Parent = null;

		_children.Clear();
	}

	protected virtual void OnUpdate(double deltaTime)
	{
	}

	internal void SetLayoutRect(UIRect rect)
	{
		LayoutRect = rect;
	}

	internal void UpdateTree(double deltaTime)
	{
		OnUpdate(deltaTime);

		for (int i = 0; i < _children.Count; i++)
			_children[i].UpdateTree(deltaTime);
	}
}