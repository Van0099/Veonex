using Veonex.Mathematics;

namespace Veonex.UI;

internal static class LayoutResolver
{
	public static void Resolve(
		Element root,
		double width,
		double height)
	{
		ArgumentNullException.ThrowIfNull(root);

		if (!double.IsFinite(width) || width < 0.0)
			throw new ArgumentOutOfRangeException(nameof(width));

		if (!double.IsFinite(height) || height < 0.0)
			throw new ArgumentOutOfRangeException(nameof(height));

		RectD viewport = new(0.0, 0.0, width, height);

		if (root.TryGet<Transform>(out Transform? rootTransform) &&
			rootTransform is not null)
		{
			rootTransform.SetResolvedRect(viewport);
		}

		ResolveChildren(root, viewport);
	}

	private static void ResolveChildren(
		Element parent,
		RectD parentRect)
	{
		foreach (Element child in parent.Children)
		{
			RectD childRect = parentRect;

			if (child.Has<Transform>())
			{
				Transform transform = child.Get<Transform>();

				Layout? layout = child.Has<Layout>()
					? child.Get<Layout>()
					: null;

				childRect = ResolveRect(
					parentRect,
					transform,
					layout);

				transform.SetResolvedRect(childRect);
			}
			else if (child.Has<Layout>())
			{
				throw new InvalidOperationException(
					$"Element '{child.Name}' has Layout but no Transform.");
			}

			ResolveChildren(child, childRect);
		}
	}

	private static RectD ResolveRect(
		RectD parent,
		Transform transform,
		Layout? layout)
	{
		DVector2 position = transform.Position;
		DVector2 size = transform.Size;

		if (layout is null)
		{
			return new RectD(
				parent.X + position.X,
				parent.Y + position.Y,
				size.X,
				size.Y);
		}

		Thickness margin = layout.Margin;

		(double x, double width) = ResolveHorizontal(
			parent.X,
			parent.Width,
			position.X,
			size.X,
			margin.Left,
			margin.Right,
			layout.HorizontalAlignment);

		(double y, double height) = ResolveVertical(
			parent.Y,
			parent.Height,
			position.Y,
			size.Y,
			margin.Top,
			margin.Bottom,
			layout.VerticalAlignment);

		return new RectD(x, y, width, height);
	}

	private static (double Position, double Size) ResolveHorizontal(
		double parentPosition,
		double parentSize,
		double offset,
		double desiredSize,
		double marginLeft,
		double marginRight,
		HorizontalAlignment alignment)
	{
		double availableSize = Math.Max(
			0.0,
			parentSize - marginLeft - marginRight);

		return alignment switch
		{
			HorizontalAlignment.Left =>
				(
					parentPosition + marginLeft + offset,
					desiredSize
				),

			HorizontalAlignment.Center =>
				(
					parentPosition +
					marginLeft +
					(availableSize - desiredSize) * 0.5 +
					offset,
					desiredSize
				),

			HorizontalAlignment.Right =>
				(
					parentPosition +
					parentSize -
					marginRight -
					desiredSize +
					offset,
					desiredSize
				),

			HorizontalAlignment.Stretch =>
				(
					parentPosition + marginLeft + offset,
					availableSize
				),

			_ => throw new ArgumentOutOfRangeException(nameof(alignment))
		};
	}

	private static (double Position, double Size) ResolveVertical(
		double parentPosition,
		double parentSize,
		double offset,
		double desiredSize,
		double marginTop,
		double marginBottom,
		VerticalAlignment alignment)
	{
		double availableSize = Math.Max(
			0.0,
			parentSize - marginTop - marginBottom);

		return alignment switch
		{
			VerticalAlignment.Top =>
				(
					parentPosition + marginTop + offset,
					desiredSize
				),

			VerticalAlignment.Center =>
				(
					parentPosition +
					marginTop +
					(availableSize - desiredSize) * 0.5 +
					offset,
					desiredSize
				),

			VerticalAlignment.Bottom =>
				(
					parentPosition +
					parentSize -
					marginBottom -
					desiredSize +
					offset,
					desiredSize
				),

			VerticalAlignment.Stretch =>
				(
					parentPosition + marginTop + offset,
					availableSize
				),

			_ => throw new ArgumentOutOfRangeException(nameof(alignment))
		};
	}
}