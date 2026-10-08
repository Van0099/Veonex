using SDL;
using System.Diagnostics;
using Veonex.Core;
using Veonex.Game;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Render;

if (!SDL3.SDL_Init(
		SDL_InitFlags.SDL_INIT_VIDEO))
{
	throw new InvalidOperationException(
		$"Failed to initialize SDL3: {SDL3.SDL_GetError()}");
}

try
{
	// ============================================================
	// Window
	// ============================================================

	WindowParameters parameters = new()
	{
		Width = 1280,
		Height = 720,
		Title = "Veonex",
		VSync = false,
		Resizable = true,
		Fullscreen = false
	};

	using RenderBackend renderer =
		new(parameters);

	InputManager input =
		new();


	// ============================================================
	// Scene
	// ============================================================

	Scene scene =
		new();


	// ============================================================
	// Camera
	// ============================================================

	Entity cameraEntity =
		scene.Add("Camera");

	Transform cameraTransform =
		cameraEntity.Add<Transform>();

	Camera camera =
		cameraEntity.Add<Camera>();

	cameraTransform.Position =
		new DVector3(
			0,
			0,
			8);

	camera.AspectRatio =
		(double)parameters.Width /
		parameters.Height;


	// ============================================================
	// Camera controller
	// ============================================================

	CameraController cameraController =
		new(
			camera,
			input,
			renderer,
			DVector3.Zero,
			moveSpeed: 5.0,
			mouseSensitivity: 0.15);


	// ============================================================
	// Material
	// ============================================================

	Material material =
		new(
			"mat",
			"Basic",
			new DVector4(
				1,
				1,
				1,
				1),
			"prototype.png");


	// ============================================================
	// Cube mesh
	// ============================================================

	Mesh cubeMesh =
		CubeMeshTemplate.Create();


	// ============================================================
	// Cube 1
	// ============================================================

	Entity cube1 =
		scene.Add("Cube_1");

	Transform cube1Transform =
		cube1.Add<Transform>();

	cube1Transform.Position =
		new DVector3(
			-2,
			0,
			0);

	MeshRenderer cube1Renderer =
			cube1.Add<MeshRenderer>();

	cube1Renderer.Mesh =
		cubeMesh;

	cube1Renderer.Material =
		material;


	// ============================================================
	// Cube 2
	// ============================================================

	Entity cube2 =
		scene.Add("Cube_2");

	Transform cube2Transform =
		cube2.Add<Transform>();

	cube2Transform.Position =
		new DVector3(
			2,
			0,
			0);

	MeshRenderer cube2Renderer =
		cube2.Add<MeshRenderer>();

	cube2Renderer.Mesh =
		cubeMesh;

	cube2Renderer.Material =
		material;


	// ============================================================
	// Timing
	// ============================================================

	const double maxDeltaTime = 0.05;

	Stopwatch stopwatch =
		Stopwatch.StartNew();

	double previousTime =
		stopwatch.Elapsed.TotalSeconds;


	// ============================================================
	// Main loop
	// ============================================================

	while (!input.QuitRequested)
	{
		double currentTime =
			stopwatch.Elapsed.TotalSeconds;

		double deltaTime =
			currentTime -
			previousTime;

		previousTime =
			currentTime;

		deltaTime =
			Math.Min(
				deltaTime,
				maxDeltaTime);


		// --------------------------------------------------------
		// Input
		// --------------------------------------------------------

		input.Update();

		if (input.QuitRequested)
			break;


		// --------------------------------------------------------
		// Resize
		// --------------------------------------------------------

		if (input.WindowSizeChanged)
		{
			renderer.ResizeRenderTarget(
				input.WindowWidth,
				input.WindowHeight);
		}


		// --------------------------------------------------------
		// Fullscreen
		// --------------------------------------------------------

		if (input.IsKeyPressed(
			SDL_Scancode.SDL_SCANCODE_F11))
		{
			renderer.ToggleFullscreen();
		}


		// --------------------------------------------------------
		// Camera
		// --------------------------------------------------------

		cameraController.Update(
			deltaTime);


		// --------------------------------------------------------
		// Render
		// --------------------------------------------------------

		renderer.RenderFrame(
			scene,
			camera);
	}
}
finally
{
	SDL3.SDL_Quit();
}