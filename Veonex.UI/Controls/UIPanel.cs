using System.Numerics;
using Veonex.UI.Core;
using Veonex.UI.Layout;

namespace Veonex.UI.Controls;

public sealed class UIPanel : VisualElement
{
	public UIPanel()
	{
	}

	public UIPanel(
		UIRect rect,
		Vector4 backgroundColor)
	{
		Rect = rect;
		BackgroundColor = backgroundColor;
	}
}