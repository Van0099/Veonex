using Veonex.Mathematics;

namespace Veonex.Core;

public sealed class Material
{
	public Guid Id { get; }
	public string Name { get; set; }
	public string Shader { get; set; }
	public string? Albedo { get; set; }
	public DVector4 AlbedoColor { get; set; }

	public Material(
		string name,
		string shader,
		DVector4 albedoColor,
		string? albedo = null)
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
	}

	public override string ToString() =>
		$"{Name} ({Id})";
}