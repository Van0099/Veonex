namespace Veonex.UI.Layout;

public readonly record struct UIRect(
	float X,
	float Y,
	float Width,
	float Height)
{
	public static UIRect Empty => default;

	public float Left => X;
	public float Top => Y;
	public float Right => X + Width;
	public float Bottom => Y + Height;

	public bool Contains(float x, float y)
	{
		return Width > 0.0f &&
			   Height > 0.0f &&
			   x >= Left &&
			   x < Right &&
			   y >= Top &&
			   y < Bottom;
	}
}