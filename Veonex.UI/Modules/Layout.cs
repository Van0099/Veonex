namespace Veonex.UI;

public enum HorizontalAlignment
{
	Left,
	Center,
	Right,
	Stretch
}

public enum VerticalAlignment
{
	Top,
	Center,
	Bottom,
	Stretch
}

public sealed class Layout : UIModule
{
	public Thickness Margin { get; set; }

	public HorizontalAlignment HorizontalAlignment { get; set; }
		= HorizontalAlignment.Left;

	public VerticalAlignment VerticalAlignment { get; set; }
		= VerticalAlignment.Top;
}