namespace Veonex.Core;

public abstract class Module
{
    public Entity Entity { get; internal set; } = null!;

    public virtual void OnAdded()
    {
    }

    public virtual void Update(double deltaTime)
    {
    }
}