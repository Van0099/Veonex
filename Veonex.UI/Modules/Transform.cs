using Veonex.Mathematics;

namespace Veonex.UI;

public sealed class RectTransform : UIModule
{
	private DVector2 _position;
	private DVector2 _size;

	public DVector2 Position
	{
		get => _position;
		set
		{
			if (!double.IsFinite(value.X) ||
				!double.IsFinite(value.Y))
			{
				throw new ArgumentOutOfRangeException(nameof(value));
			}

			_position = value;
		}
	}

	public DVector2 Size
	{
		get => _size;
		set
		{
			if (!double.IsFinite(value.X) ||
				!double.IsFinite(value.Y) ||
				value.X < 0.0 ||
				value.Y < 0.0)
			{
				throw new ArgumentOutOfRangeException(nameof(value));
			}

			_size = value;
		}
	}

	public RectD ResolvedRect { get; private set; }

	public DVector2 ResolvedPosition => ResolvedRect.Position;

	public DVector2 ResolvedSize => ResolvedRect.Size;

	public RectTransform()
	{
	}

	public RectTransform(DVector2 size)
	{
		Size = size;
	}

	public RectTransform(DVector2 position, DVector2 size)
	{
		Position = position;
		Size = size;
	}

	internal void SetResolvedRect(RectD rect)
	{
		ResolvedRect = rect;
	}
}