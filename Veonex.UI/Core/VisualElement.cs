using Veonex.UI.Layout;
using System.Numerics;

namespace Veonex.UI.Core;

public class VisualElement
{
	private readonly List<VisualElement> _children = [];

	public string Name { get; set; } = string.Empty;

	public VisualElement? Parent { get; private set; }

	public IReadOnlyList<VisualElement> Children => _children;

	public UIRect Rect { get; set; } = UIRect.Empty;

	public UIMargin Margin { get; set; } = UIMargin.Zero;

	public bool Visible { get; set; } = true;

	public bool Enabled { get; set; } = true;

	public Vector4 BackgroundColor { get; set; } = Vector4.Zero;

	public void Add(VisualElement child)
	{
		ArgumentNullException.ThrowIfNull(child);

		for (VisualElement? current = this;
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
		{
			return;
		}

		child.Parent?._children.Remove(child);

		child.Parent = this;
		_children.Add(child);
	}

	public bool Remove(VisualElement child)
	{
		ArgumentNullException.ThrowIfNull(child);

		if (!_children.Remove(child))
		{
			return false;
		}

		child.Parent = null;
		return true;
	}

	public void Clear()
	{
		foreach (VisualElement child in _children)
		{
			child.Parent = null;
		}

		_children.Clear();
	}

	protected virtual void OnUpdate(double deltaTime)
	{
	}

	internal void UpdateTree(double deltaTime)
	{
		OnUpdate(deltaTime);

		for (int i = 0; i < _children.Count; i++)
		{
			_children[i].UpdateTree(deltaTime);
		}
	}
}