using Veonex.Mathematics;

namespace Veonex.Core;

public enum LightType
{
	Directional
}

public sealed class Light : Module
{
	private double _intensity = 1.0;

	public LightType Type { get; set; } = LightType.Directional;

	public DVector3 Color { get; set; } = new(1.0, 1.0, 1.0);

	public double Intensity
	{
		get => _intensity;
		set => _intensity = double.IsFinite(value)
			? Math.Max(0.0, value)
			: 0.0;
	}

	public bool Enabled { get; set; } = true;

	public DVector3 Direction =>
		Entity.Get<Transform>().Quaternion *
		new DVector3(0.0, 0.0, 1.0);

	public override void OnAdded()
	{
		if (!Entity.Has<Transform>())
		{
			throw new InvalidOperationException(
				"Light module requires Transform module.");
		}
	}
}