namespace Veonex.Core;

public sealed class Mesh
{
    public Guid Id { get; }

    public string Name { get; set; }

    public MeshData Data { get; }


    public Mesh(
        string name,
        MeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Id = Guid.NewGuid();

        Name = string.IsNullOrWhiteSpace(name)
            ? $"Mesh_{Id}"
            : name;

        Data = data;
    }


    public override string ToString()
        => $"{Name} ({Id})";
}