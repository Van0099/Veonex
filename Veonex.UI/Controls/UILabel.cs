using System.Numerics;
using Veonex.UI.Core;

namespace Veonex.UI.Controls;

public sealed class UILabel : Element
{
	public string Text { get; set; } = string.Empty;

	public string FontPath { get; set; } = string.Empty;

	public int FontSize { get; set; } = 24;

	public Vector4 TextColor { get; set; } = Vector4.One;

	public UILabel()
	{
	}

	public UILabel(
		string text,
		string fontPath,
		int fontSize = 24)
	{
		ArgumentNullException.ThrowIfNull(text);
		ArgumentException.ThrowIfNullOrWhiteSpace(fontPath);

		Text = text;
		FontPath = fontPath;
		FontSize = fontSize;
	}
}