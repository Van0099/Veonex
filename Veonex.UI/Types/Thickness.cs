namespace Veonex.UI;

public readonly record struct Thickness
{
	public double Left { get; }

	public double Top { get; }

	public double Right { get; }

	public double Bottom { get; }

	public double Horizontal => Left + Right;

	public double Vertical => Top + Bottom;

	public Thickness(double uniform)
		: this(uniform, uniform, uniform, uniform)
	{
	}

	public Thickness(double horizontal, double vertical)
		: this(horizontal, vertical, horizontal, vertical)
	{
	}

	public Thickness(
		double left,
		double top,
		double right,
		double bottom)
	{
		if (!double.IsFinite(left))
			throw new ArgumentOutOfRangeException(nameof(left));

		if (!double.IsFinite(top))
			throw new ArgumentOutOfRangeException(nameof(top));

		if (!double.IsFinite(right))
			throw new ArgumentOutOfRangeException(nameof(right));

		if (!double.IsFinite(bottom))
			throw new ArgumentOutOfRangeException(nameof(bottom));

		Left = left;
		Top = top;
		Right = right;
		Bottom = bottom;
	}
}