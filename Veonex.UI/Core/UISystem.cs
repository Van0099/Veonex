namespace Veonex.UI.Core;

public sealed class UISystem
{
	public void Update(
		UIDocument document,
		double deltaTime)
	{
		ArgumentNullException.ThrowIfNull(document);

		if (!double.IsFinite(deltaTime) || deltaTime < 0.0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(deltaTime));
		}

		document.Update(deltaTime);
	}
}