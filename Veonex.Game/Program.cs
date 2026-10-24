using SDL;
using Veonex.Core;
using Veonex.Game;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Render;
using Veonex.Physics;

/// <summary>
/// A temporary sandbox created for physics testing.
/// The original sandbox will be restored once all tests are completed.
/// </summary>

WindowParameters parameters = new()
{
	Width = 1920,
	Height = 1080,
	Title = "Veonex - Physics test",
	VSync = false,
	Resizable = true,
	Fullscreen = true
};

if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
{
	throw new InvalidOperationException(
		$"Failed to initialize SDL3: {SDL3.SDL_GetError()}");
}

try
{
	using RenderBackend renderer = new(parameters);
	using var physics = new Physics();

	InputManager input = new();

	Scene scene = new();

	Entity cameraEntity = scene.Add("Camera");

	Transform cameraTransform =
		cameraEntity.Add<Transform>();

	Camera camera =
		cameraEntity.Add<Camera>();

	cameraTransform.Position = new DVector3(0, 2, 8);

	camera.AspectRatio =
		(double)parameters.Width / parameters.Height;

	CameraController cameraController = new(
		camera,
		input,
		renderer,
		DVector3.Zero,
		moveSpeed: 12.0,
		mouseSensitivity: 0.15);

	Material cubeMaterial = new(
		"CubeMaterial",
		"Basic",
		new DVector4(0.48, 0.77, 0.23, 1.0));

	Entity cubeEntity = scene.Add("Cube");

	Transform cubeTransform =
		cubeEntity.Add<Transform>();

	cubeTransform.Position = DVector3.Zero;
	cubeTransform.Scale = new DVector3(1, 1, 1);

	MeshRenderer cubeRenderer =
		cubeEntity.Add<MeshRenderer>();

	cubeRenderer.Mesh = CubeMeshTemplate.Create();
	cubeRenderer.Material = cubeMaterial;

	double previousTime =
		0.0;

	System.Diagnostics.Stopwatch stopwatch =
		System.Diagnostics.Stopwatch.StartNew();

	previousTime = stopwatch.Elapsed.TotalSeconds;

	const double maxDeltaTime = 0.05;

	while (!input.QuitRequested)
	{
		double currentTime =
			stopwatch.Elapsed.TotalSeconds;

		double realDeltaTime =
			currentTime - previousTime;

		previousTime = currentTime;

		double deltaTime =
			Math.Min(realDeltaTime, maxDeltaTime);

		input.Update();
		physics.Update(deltaTime);

		if (input.QuitRequested)
			break;

		if (input.WindowSizeChanged)
		{
			renderer.ResizeRenderTarget(
				input.WindowWidth,
				input.WindowHeight);

			if (input.WindowHeight > 0)
			{
				camera.AspectRatio =
					(double)input.WindowWidth /
					input.WindowHeight;
			}
		}

		if (input.IsKeyPressed(
			SDL_Scancode.SDL_SCANCODE_F11))
		{
			renderer.ToggleFullscreen();
		}

		cameraController.Update(deltaTime);

		renderer.RenderFrame(scene, camera);
	}

	stopwatch.Stop();
}
finally
{
	SDL3.SDL_Quit();
}