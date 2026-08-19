namespace Veonex.Core;

public sealed class MeshRenderer : Module
{
    public Mesh? Mesh { get; set; }


    public bool Visible { get; set; } = true;


    public override void OnAdded()
    {
        if (!Entity.Has<Transform>())
        {
            throw new InvalidOperationException(
                "MeshRenderer requires Transform module.");
        }
    }
}