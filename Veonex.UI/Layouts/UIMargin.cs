namespace Veonex.UI.Layout;

public readonly record struct UIMargin(
	float Left,
	float Top,
	float Right,
	float Bottom)
{
	public static UIMargin Zero => default;

	public static UIMargin All(float value)
	{
		return new UIMargin(
			value,
			value,
			value,
			value);
	}

	public static UIMargin HorizontalVertical(
		float horizontal,
		float vertical)
	{
		return new UIMargin(
			horizontal,
			vertical,
			horizontal,
			vertical);
	}
}