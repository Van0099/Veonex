using Veonex.UI.Layout;

namespace Veonex.UI.Core;

public sealed class UISystem
{
	public void Update(
		UIDocument document,
		double deltaTime)
	{
		ValidateDeltaTime(deltaTime);

		ArgumentNullException.ThrowIfNull(document);

		document.Update(deltaTime);
	}

	public void Update(
		UIDocument document,
		double deltaTime,
		UIRect viewport)
	{
		ValidateDeltaTime(deltaTime);

		ArgumentNullException.ThrowIfNull(document);

		document.Update(deltaTime);
		document.ResolveLayout(
			(int)viewport.Width,
			(int)viewport.Height);
	}

	private static void ValidateDeltaTime(double deltaTime)
	{
		if (!double.IsFinite(deltaTime) || deltaTime < 0.0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(deltaTime));
		}
	}
}