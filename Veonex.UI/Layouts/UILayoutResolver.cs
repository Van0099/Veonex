using Veonex.UI.Core;

namespace Veonex.UI.Layout;

public static class UILayoutResolver
{
	public static void Resolve(Element root, UIRect viewport)
	{
		ArgumentNullException.ThrowIfNull(root);

		root.SetLayoutRect(viewport);

		ResolveChildren(root);
	}

	private static void ResolveChildren(Element parent)
	{
		UIRect content = GetContentRect(
			parent.LayoutRect,
			parent.Padding);

		switch (parent.ChildrenLayout)
		{
			case UIChildrenLayout.None:
				foreach (Element child in parent.Children)
				{
					child.SetLayoutRect(
						ResolveAnchored(child, content));

					ResolveChildren(child);
				}

				break;

			case UIChildrenLayout.Horizontal:
				ResolveHorizontal(parent, content);
				break;

			case UIChildrenLayout.Vertical:
				ResolveVertical(parent, content);
				break;

			default:
				throw new ArgumentOutOfRangeException(
					nameof(parent.ChildrenLayout));
		}
	}

	private static UIRect ResolveAnchored(
		Element element,
		UIRect parent)
	{
		UIMargin margin = element.Margin;

		float width = element.HorizontalAlignment ==
			UIHorizontalAlignment.Stretch
			? AvailableWidth(parent, margin)
			: NormalizeSize(element.Size.X);

		float height = element.VerticalAlignment ==
			UIVerticalAlignment.Stretch
			? AvailableHeight(parent, margin)
			: NormalizeSize(element.Size.Y);

		float x = GetHorizontalPosition(
			element.HorizontalAlignment,
			parent,
			width,
			margin,
			element.Position.X);

		float y = GetVerticalPosition(
			element.VerticalAlignment,
			parent,
			height,
			margin,
			element.Position.Y);

		return new UIRect(x, y, width, height);
	}

	private static void ResolveHorizontal(
		Element parent,
		UIRect content)
	{
		float cursor = 0.0f;
		float spacing = NormalizeSpacing(parent.ChildSpacing);

		foreach (Element child in parent.Children)
		{
			UIMargin margin = child.Margin;

			float width = NormalizeSize(child.Size.X);

			float height = child.VerticalAlignment ==
				UIVerticalAlignment.Stretch
				? AvailableHeight(content, margin)
				: NormalizeSize(child.Size.Y);

			float x =
				content.X +
				cursor +
				margin.Left +
				Safe(child.Position.X);

			float y = GetVerticalPosition(
				child.VerticalAlignment,
				content,
				height,
				margin,
				child.Position.Y);

			child.SetLayoutRect(
				new UIRect(x, y, width, height));

			ResolveChildren(child);

			cursor += margin.Left
				+ width
				+ margin.Right
				+ spacing;
		}
	}

	private static void ResolveVertical(
		Element parent,
		UIRect content)
	{
		float cursor = 0.0f;
		float spacing = NormalizeSpacing(parent.ChildSpacing);

		foreach (Element child in parent.Children)
		{
			UIMargin margin = child.Margin;

			float width = child.HorizontalAlignment ==
				UIHorizontalAlignment.Stretch
				? AvailableWidth(content, margin)
				: NormalizeSize(child.Size.X);

			float height = NormalizeSize(child.Size.Y);

			float x = GetHorizontalPosition(
				child.HorizontalAlignment,
				content,
				width,
				margin,
				child.Position.X);

			float y =
				content.Y +
				cursor +
				margin.Top +
				Safe(child.Position.Y);

			child.SetLayoutRect(
				new UIRect(x, y, width, height));

			ResolveChildren(child);

			cursor += margin.Top
				+ height
				+ margin.Bottom
				+ spacing;
		}
	}

	private static float GetHorizontalPosition(
		UIHorizontalAlignment alignment,
		UIRect parent,
		float width,
		UIMargin margin,
		float offset)
	{
		offset = Safe(offset);

		return alignment switch
		{
			UIHorizontalAlignment.Left =>
				parent.Left + margin.Left + offset,

			UIHorizontalAlignment.Center =>
				parent.Left +
				(parent.Width - width
					+ margin.Left - margin.Right) * 0.5f
				+ offset,

			UIHorizontalAlignment.Right =>
				parent.Right - margin.Right - width + offset,

			UIHorizontalAlignment.Stretch =>
				parent.Left + margin.Left + offset,

			_ => throw new ArgumentOutOfRangeException(
				nameof(alignment))
		};
	}

	private static float GetVerticalPosition(
		UIVerticalAlignment alignment,
		UIRect parent,
		float height,
		UIMargin margin,
		float offset)
	{
		offset = Safe(offset);

		return alignment switch
		{
			UIVerticalAlignment.Top =>
				parent.Top + margin.Top + offset,

			UIVerticalAlignment.Center =>
				parent.Top +
				(parent.Height - height
					+ margin.Top - margin.Bottom) * 0.5f
				+ offset,

			UIVerticalAlignment.Bottom =>
				parent.Bottom - margin.Bottom - height + offset,

			UIVerticalAlignment.Stretch =>
				parent.Top + margin.Top + offset,

			_ => throw new ArgumentOutOfRangeException(
				nameof(alignment))
		};
	}

	private static UIRect GetContentRect(
		UIRect rect,
		UIMargin padding)
	{
		float left = NonNegative(padding.Left);
		float top = NonNegative(padding.Top);
		float right = NonNegative(padding.Right);
		float bottom = NonNegative(padding.Bottom);

		return new UIRect(
			rect.X + left,
			rect.Y + top,
			MathF.Max(0.0f, rect.Width - left - right),
			MathF.Max(0.0f, rect.Height - top - bottom));
	}

	private static float AvailableWidth(
		UIRect parent,
		UIMargin margin)
	{
		return MathF.Max(
			0.0f,
			parent.Width - Safe(margin.Left) - Safe(margin.Right));
	}

	private static float AvailableHeight(
		UIRect parent,
		UIMargin margin)
	{
		return MathF.Max(
			0.0f,
			parent.Height - Safe(margin.Top) - Safe(margin.Bottom));
	}

	private static float NormalizeSize(float value)
	{
		return float.IsFinite(value)
			? MathF.Max(0.0f, value)
			: 0.0f;
	}

	private static float NormalizeSpacing(float value)
	{
		return NormalizeSize(value);
	}

	private static float NonNegative(float value)
	{
		return NormalizeSize(value);
	}

	private static float Safe(float value)
	{
		return float.IsFinite(value) ? value : 0.0f;
	}
}