using SDL;
using Veonex.Core;
using Veonex.Game;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Physics;
using Veonex.Render;
using Veonex.Assets;

/// <summary>
/// Physics sandbox: a physics-driven first-person player, a floor,
/// and 100 dynamic cubes which fall and collide.
/// </summary>
WindowParameters parameters = new()
{
	Width = 1920,
	Height = 1080,
	Title = "Veonex - Physics Sandbox",
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

	physics.Gravity = new DVector3(0.0, -9.81, 0.0);

	// All cubes use the same mesh.
	Mesh cubeMesh = CubeMeshTemplate.Create();

	Material floorMaterial = new(
		"PhysicsFloor",
		"Basic",
		new DVector4(1, 1, 1, 1.0),
		"assets/textures/prototype.png");

	Material[] cubeMaterials =
	[
		new Material(
			"CubeGreen",
			"Basic",
			new DVector4(0.48, 0.77, 0.23, 1.0),
			"assets/textures/images.png"),

		new Material(
			"CubeBlue",
			"Basic",
			new DVector4(0.23, 0.55, 0.92, 1.0),
			"assets/textures/images.png"),

		new Material(
			"CubeOrange",
			"Basic",
			new DVector4(0.95, 0.48, 0.18, 1.0),
			"assets/textures/images.png"),

		new Material(
			"CubePurple",
			"Basic",
			new DVector4(0.65, 0.35, 0.88, 1.0),
			"assets/textures/images.png"),
	];

	Entity floorEntity = scene.Add("Floor");

	Transform floorTransform =
		floorEntity.Add<Transform>();

	floorTransform.Position =
		new DVector3(0.0, -0.5, 0.0);

	floorTransform.Scale =
		new DVector3(30.0, 0.5, 30.0);

	MeshRenderer floorRenderer =
		floorEntity.Add<MeshRenderer>();

	floorRenderer.Mesh = cubeMesh;
	floorRenderer.Material = floorMaterial;

	Collider floorCollider =
		floorEntity.Add<Collider>();

	floorCollider.SimpleGeometry = true;

	Entity playerEntity = scene.Add("Player");

	Transform playerTransform =
		playerEntity.Add<Transform>();

	playerTransform.Position =
		new DVector3(0.0, 1.05, 11.0);

	playerTransform.Scale =
		new DVector3(0.35, 0.9, 0.35);

	MeshRenderer playerRenderer =
		playerEntity.Add<MeshRenderer>();

	playerRenderer.Mesh = cubeMesh;
	playerRenderer.Material = cubeMaterials[0];
	playerRenderer.Visible = false;

	Collider playerCollider =
		playerEntity.Add<Collider>();

	playerCollider.SimpleGeometry = true;

	PhysicsBody playerBody =
		playerEntity.Add<PhysicsBody>();

	playerBody.Mass = 75.0;
	playerBody.Drag = 0.05;
	playerBody.AngularDrag = 5.0;
	playerBody.UseGravity = true;
	playerBody.FreezeRotation = true;

	const int cubeCount = 100;
	const int columnsPerAxis = 5;

	const double cubeScale = 0.5;
	const double stackSpacing = 1.15;

	List<Mesh> loadedMeshes = new List<Mesh>(ModelLoader.Load("assets/models/model2.glb"));
	Mesh modelMesh = loadedMeshes[0];

	Random random = new(128);

	for (int i = 0; i < cubeCount; i++)
	{
		int layer =
			i / (columnsPerAxis * columnsPerAxis);

		int cell =
			i % (columnsPerAxis * columnsPerAxis);

		int column =
			cell % columnsPerAxis;

		int row =
			cell / columnsPerAxis;

		Entity cubeEntity =
			scene.Add($"FallingCube_{i + 1:000}");

		Transform cubeTransform =
			cubeEntity.Add<Transform>();

		cubeTransform.Scale = new DVector3(
			cubeScale,
			cubeScale,
			cubeScale);

		cubeTransform.Position = new DVector3(
			(column - (columnsPerAxis - 1) * 0.5)
				* stackSpacing,

			4.0 + layer * 1.08
				+ random.NextDouble() * 0.04,

			(row - (columnsPerAxis - 1) * 0.5)
				* stackSpacing);

		MeshRenderer cubeRenderer =
			cubeEntity.Add<MeshRenderer>();

		cubeRenderer.Mesh = modelMesh;

		cubeRenderer.Material =
			cubeMaterials[random.Next(cubeMaterials.Length)];

		Collider cubeCollider =
			cubeEntity.Add<Collider>();

		cubeCollider.SimpleGeometry = false;
		cubeCollider.CollisionQuality = 1;

		PhysicsBody cubeBody =
			cubeEntity.Add<PhysicsBody>();

		cubeBody.Mass = 50.0;
		cubeBody.Drag = 0.02;
		cubeBody.AngularDrag = 0.08;
		cubeBody.UseGravity = true;
	}

	// -------------------------------------------------------------
	// CAMERA
	//
	// Camera follows the player via PhysicsPlayerController.
	// -------------------------------------------------------------

	Entity cameraEntity = scene.Add("Camera");

	cameraEntity.Add<Transform>();

	Camera camera =
		cameraEntity.Add<Camera>();

	camera.AspectRatio =
		(double)parameters.Width / parameters.Height;

	// Register scene colliders and bodies before the first frame.
	physics.Update(scene, 0.0);

	PhysicsPlayerController playerController = new(
		playerEntity,
		camera,
		input,
		renderer,
		physics,
		lookTarget: new DVector3(0.0, 3.0, 0.0),
		moveSpeed: 6.0,
		jumpSpeed: 7.0,
		mouseSensitivity: 0.15,
		eyeHeight: 0.62);

	Console.WriteLine("Physics sandbox ready.");

	Console.WriteLine(
		"WASD: move | Space: jump | Mouse: look | " +
		"Esc: release mouse | Left click: recapture | F11: fullscreen");

	Console.WriteLine(
		$"Dynamic bodies: {physics.BodyCount} | " +
		$"Colliders: {physics.ColliderCount}");

	System.Diagnostics.Stopwatch stopwatch =
		System.Diagnostics.Stopwatch.StartNew();

	double previousTime =
		stopwatch.Elapsed.TotalSeconds;

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

		if (input.QuitRequested)
			break;

		playerController.UpdateInput(deltaTime);

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

		scene.Update(deltaTime);

		physics.Update(scene, deltaTime);

		playerController.UpdateCamera();

		renderer.RenderFrame(scene, camera, currentTime);
	}

	stopwatch.Stop();
}
finally
{
	SDL3.SDL_Quit();
}