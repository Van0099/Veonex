using Veonex.Mathematics;

namespace Veonex.Core;

public sealed class Material
{
	private double _opacity = 1.0;
	private double _emissionStrength = 0.0;

	private DVector2 _tiling = new(1.0, 1.0);
	private DVector2 _offset = new(0.0, 0.0);

	public Guid Id { get; }
	public string Name { get; set; }
	public string Shader { get; set; }
	public string? Albedo { get; set; }
	public DVector4 AlbedoColor { get; set; }

	public DVector4 EmissionColor { get; set; } =
		new(1.0, 1.0, 1.0, 1.0);

	public double EmissionStrength
	{
		get => _emissionStrength;
		set => _emissionStrength = double.IsFinite(value)
			? Math.Max(0.0, value)
			: 0.0;
	}

	public double Opacity
	{
		get => _opacity;
		set => _opacity = double.IsFinite(value)
			? Math.Clamp(value, 0.0, 1.0)
			: 1.0;
	}

	public bool UseAlphaBlending { get; set; }

	public bool TextureTiling { get; set; }

	public DVector2 Tiling
	{
		get => _tiling;
		set => _tiling = new DVector2(
			double.IsFinite(value.X) ? value.X : 1.0,
			double.IsFinite(value.Y) ? value.Y : 1.0);
	}

	public DVector2 Offset
	{
		get => _offset;
		set => _offset = new DVector2(
			double.IsFinite(value.X) ? value.X : 0.0,
			double.IsFinite(value.Y) ? value.Y : 0.0);
	}

	public Material(
		string name,
		string shader,
		DVector4 albedoColor,
		string? albedo = null,
		double opacity = 1.0,
		bool useAlphaBlending = false,
		bool textureTiling = false,
		DVector2? tiling = null,
		DVector2? offset = null)
	{
		Id = Guid.NewGuid();

		Name = string.IsNullOrWhiteSpace(name)
			? $"Material_{Id}"
			: name;

		Shader = string.IsNullOrWhiteSpace(shader)
			? throw new ArgumentException(
				"Shader cannot be empty.",
				nameof(shader))
			: shader;

		Albedo = albedo;
		AlbedoColor = albedoColor;
		Opacity = opacity;
		UseAlphaBlending = useAlphaBlending;

		TextureTiling = textureTiling;
		Tiling = tiling ?? new DVector2(1.0, 1.0);
		Offset = offset ?? new DVector2(0.0, 0.0);
	}

	public override string ToString() =>
		$"{Name} ({Id})";
}