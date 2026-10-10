using Veonex.Mathematics;

namespace Veonex.Core;
public sealed class Material
{
	private double _opacity = 1.0;

	public Guid Id { get; }
	public string Name { get; set; }
	public string Shader { get; set; }
	public string? Albedo { get; set; }
	public DVector4 AlbedoColor { get; set; }

	public double Opacity
	{
		get => _opacity;
		set => _opacity = double.IsFinite(value)
			? Math.Clamp(value, 0.0, 1.0)
			: 1.0;
	}

	public bool UseAlphaBlending { get; set; }

	public Material(
		string name,
		string shader,
		DVector4 albedoColor,
		string? albedo = null,
		double opacity = 1.0,
		bool useAlphaBlending = false)
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
	}

	public override string ToString() =>
		$"{Name} ({Id})";
}