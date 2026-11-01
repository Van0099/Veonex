using Veonex.Mathematics;

namespace Veonex.UI;

public readonly record struct RectD
{
	public DVector2 Position { get; }

	public DVector2 Size { get; }

	public double X => Position.X;
	public double Y => Position.Y;

	public double Width => Size.X;
	public double Height => Size.Y;

	public double Left => X;
	public double Top => Y;
	public double Right => X + Width;
	public double Bottom => Y + Height;

	public RectD(double x, double y, double width, double height)
	{
		if (!double.IsFinite(x))
			throw new ArgumentOutOfRangeException(nameof(x));

		if (!double.IsFinite(y))
			throw new ArgumentOutOfRangeException(nameof(y));

		if (!double.IsFinite(width) || width < 0.0)
			throw new ArgumentOutOfRangeException(nameof(width));

		if (!double.IsFinite(height) || height < 0.0)
			throw new ArgumentOutOfRangeException(nameof(height));

		Position = new DVector2(x, y);
		Size = new DVector2(width, height);
	}

	public RectD(DVector2 position, DVector2 size)
		: this(position.X, position.Y, size.X, size.Y)
	{
	}
}