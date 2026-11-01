namespace Veonex.UI;

public abstract class UIModule
{
	public Element Element { get; internal set; } = null!;

	public virtual void OnAdded() { }

	public virtual void Update(double deltaTime) { }

	public virtual void OnRemoved() { }
}