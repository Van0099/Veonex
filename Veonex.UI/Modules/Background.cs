using Veonex.Mathematics;

namespace Veonex.UI;

public sealed class Background : UIModule
{
	private DVector4 _color = new(0.0, 0.0, 0.0, 0.0);
	private string? _image;

	public DVector4 Color
	{
		get => _color;
		set => _color = value;
	}

	public string? Image
	{
		get => _image;
		set => _image = value;
	}
}