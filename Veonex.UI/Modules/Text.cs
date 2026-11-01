using System;
using Veonex.Mathematics;

namespace Veonex.UI;

public enum TextAlignment
{
	Left,
	Center,
	Right
}

public sealed class Text : UIModule
{
	private string _content = string.Empty;
	private string _fontPath = string.Empty;
	private double _fontSize = 16.0;
	private DVector4 _color = new(1.0, 1.0, 1.0, 1.0);
	private TextAlignment _alignment = TextAlignment.Left;

	public string Content
	{
		get => _content;
		set => _content = value ?? throw new ArgumentNullException(nameof(value));
	}

	public string FontPath
	{
		get => _fontPath;
		set => _fontPath = value ?? throw new ArgumentNullException(nameof(value));
	}

	public double FontSize
	{
		get => _fontSize;
		set
		{
			if (!double.IsFinite(value) || value <= 0.0)
				throw new ArgumentOutOfRangeException(nameof(value));

			_fontSize = value;
		}
	}

	public DVector4 Color
	{
		get => _color;
		set => _color = value;
	}

	public TextAlignment Alignment
	{
		get => _alignment;
		set => _alignment = value;
	}
}