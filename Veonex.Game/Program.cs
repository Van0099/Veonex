using SDL;
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

	camera.AspectRatio =
		(double)parameters.Width /
		parameters.Height;


	// Camera position

	cameraTransform.Position =
		new DVector3(
			0,
			0,
			8);


	// Camera looks towards the origin

	DVector3 target =
		DVector3.Zero;

	DVector3 direction =
		target -
		cameraTransform.Position;

	direction =
		direction.Normalized();

	double horizontalLength =
		Math.Sqrt(
			direction.X * direction.X +
			direction.Z * direction.Z);

	double yaw =
		Math.Atan2(
			direction.X,
			direction.Z);

	double pitch =
		Math.Atan2(
			-direction.Y,
			horizontalLength);

	cameraTransform.Rotation =
		new DVector3(
			pitch * 180.0 / Math.PI,
			yaw * 180.0 / Math.PI,
			0.0);


	// ============================================================
	// Material
	// ============================================================

	var mat =
		new Material(
			"mat",
			"Basic",
			new DVector4(1, 1, 1, 1),
			"prototype.png");


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
		mat;


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
		mat;


	// ============================================================
	// Main loop
	// ============================================================

	while (!input.QuitRequested)
	{
		input.Update();

		if (input.QuitRequested)
			break;

		if (input.WindowSizeChanged)
		{
			renderer.ResizeRenderTarget(
				input.WindowWidth,
				input.WindowHeight);
		}

		if (input.IsKeyPressed(
			SDL_Scancode.SDL_SCANCODE_F11))
		{
			renderer.ToggleFullscreen();
		}

		renderer.RenderFrame(
			scene,
			camera);
	}
}
finally
{
	SDL3.SDL_Quit();
}