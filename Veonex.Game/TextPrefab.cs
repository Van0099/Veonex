using System;
using Veonex.Mathematics;

namespace Veonex.UI;

public static class TextPrefab
{
	public static Element Create(
		string name,
		string content,
		string fontPath,
		double fontSize,
		DVector2 position,
		DVector2 size,
		DVector4? color = null,
		TextAlignment alignment = TextAlignment.Left,
		Layout? layout = null)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(content);
		ArgumentException.ThrowIfNullOrWhiteSpace(fontPath);

		Element element = new(
			name,
			new Transform(position, size),
			new Text
			{
				Content = content,
				FontPath = fontPath,
				FontSize = fontSize,
				Color = color ?? new DVector4(1.0, 1.0, 1.0, 1.0),
				Alignment = alignment
			});

		if (layout is not null)
			element.Add(layout);

		return element;
	}
}
