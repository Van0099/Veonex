using SDL;
using Veonex.Assets;
using Veonex.Core;
using Veonex.Core.Logging;
using Veonex.Game;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Physics;
using Veonex.Render;
using Veonex.UI;

/// <summary>
/// test sandbox project
/// </summary>
WindowParameters parameters = new()
{
	Width = 1920,
	Height = 1080,
	Title = "Veonex Game",
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

	const string fontPath = "assets/fonts/Roboto-Regular.ttf";

	UIDocument uiDocument = new()
	{
		Name = "HUD"
	};

	Element counter = TextPrefab.Create(
		name: "Counter",
		content: "60 FPS",
		fontPath: fontPath,
		fontSize: 12.0,
		position: new DVector2(6.0, 6.0),
		size: new DVector2(296.0, 24.0));

	Element renderStatsLabel = TextPrefab.Create(
		name: "RenderStats",
		content: string.Empty,
		fontPath: fontPath,
		fontSize: 12.0,
		position: new DVector2(6.0, 36.0),
		size: new DVector2(900.0, 48.0));

	Element positionLabel = TextPrefab.Create(
		name: "PositionStats",
		content: string.Empty,
		fontPath: fontPath,
		fontSize: 12.0,
		position: new DVector2(6.0, 72.0),
		size: new DVector2(196.0, 48.0));

	Element consoleLabel = TextPrefab.Create(
		name: "Console",
		content: "Veonex Console",
		fontPath: fontPath,
		fontSize: 12.0,
		position: DVector2.Zero,
		size: new DVector2(600.0, 300.0),
		layout: new Layout
		{
			HorizontalAlignment = HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Bottom,
			Margin = new Thickness(6.0)
		});

	uiDocument.Root.AddChild(counter);
	uiDocument.Root.AddChild(renderStatsLabel);
	uiDocument.Root.AddChild(positionLabel);
	uiDocument.Root.AddChild(consoleLabel);

	Scene scene = new();

	Entity lightEntity = scene.Add("DirectionalLight");

	Transform lightTransform = lightEntity.Add<Transform>();
	lightTransform.Rotation = new DVector3(45.0, 30.0, 0.0);

	Light light = lightEntity.Add<Light>();
	light.Type = LightType.Directional;
	light.Color = new DVector3(1.0, 1.0, 1.0);
	light.Intensity = 1.0;

	physics.Gravity = new DVector3(0.0, -9.81, 0.0);

	Mesh cubeMesh = CubeMeshTemplate.Create();
	var loadedMeshes = ModelLoader.Load("assets/models/object.glb")
		.Concat(ModelLoader.Load("assets/models/suzanne.glb"))
		.ToList();
	Mesh modelMesh = loadedMeshes[0];

	const string texBasic = "assets/textures/prototype.png";

	Material basicMat = new("Basic", "Basic", 
		new DVector4(1, 1, 1, 1), 
		texBasic, 
		textureTiling: true, 
		tiling: new DVector2(10.0, 10.0));

	Material[] texturedMats = [basicMat];

	Material[] brickMaterials =
	[
		new("BrickRed",    "Basic", new DVector4(1.0, 0.2, 0.2, 1.0), "assets/textures/brick.png"),
		new("BrickGreen",  "Basic", new DVector4(0.2, 1.0, 0.2, 1.0), "assets/textures/brick.png"),
		new("BrickBlue",   "Basic", new DVector4(0.2, 0.2, 1.0, 1.0), "assets/textures/brick.png"),
		new("BrickYellow", "Basic", new DVector4(1.0, 1.0, 0.2, 1.0), "assets/textures/brick.png")
	];

	// Floor
	Entity floor = scene.Add("Floor");
	Transform floorTransform = floor.Add<Transform>();
	floorTransform.Position = DVector3.Zero;
	floorTransform.Scale = new DVector3(1000, 1, 1000);

	MeshRenderer floorRenderer = floor.Add<MeshRenderer>();
	floorRenderer.Mesh = cubeMesh;
	floorRenderer.Material = basicMat;

	Collider floorCollider = floor.Add<Collider>();
	floorCollider.SimpleGeometry = true;

	const double floorSize = 50;
	const double wallHeight = 5;
	const double wallThickness = 1;

	double offset = floorSize / 2 + wallThickness / 2;
	double wallCenterY = wallHeight / 2 + 0.5;

	(string name, DVector3 pos, DVector3 scale)[] walls =
	[
		("WallNorth", new DVector3(0, wallCenterY,  offset), new DVector3(floorSize + wallThickness * 2, wallHeight, wallThickness)),
		("WallSouth", new DVector3(0, wallCenterY, -offset), new DVector3(floorSize + wallThickness * 2, wallHeight, wallThickness)),
		("WallEast",  new DVector3( offset, wallCenterY, 0), new DVector3(wallThickness, wallHeight, floorSize)),
		("WallWest",  new DVector3(-offset, wallCenterY, 0), new DVector3(wallThickness, wallHeight, floorSize)),
	];

	foreach (var (name, pos, scale) in walls)
	{
		Entity wall = scene.Add(name);

		Transform wallTransform = wall.Add<Transform>();
		wallTransform.Position = pos;
		wallTransform.Scale = scale;

		MeshRenderer wallRenderer = wall.Add<MeshRenderer>();
		wallRenderer.Mesh = cubeMesh;
		wallRenderer.Material = basicMat;

		Collider wallCollider = wall.Add<Collider>();
		wallCollider.SimpleGeometry = true;
		wallCollider.SizePercent = new DVector3(0.1, 0.1, 0.1);
	}

	Entity rampEntity = scene.Add("Ramp");

	Transform rampTransform = rampEntity.Add<Transform>();
	rampTransform.Position = new DVector3(0.0, 1.0, 0.0);
	rampTransform.Scale = new DVector3(2.0, 2.0, 2.0);

	MeshRenderer rampRenderer = rampEntity.Add<MeshRenderer>();
	rampRenderer.Mesh = modelMesh;
	rampRenderer.Material = basicMat;

	Collider rampCollider = rampEntity.Add<Collider>();

	const int boxesPerCorner = 5;
	const int randomBoxCount = 25;
	const double cornerDistance = 30.0;
	const double cornerHeight = 10.0;
	const double spawnRadius = 20.0;
	const double boxSize = 1.0;
	const double stackGap = 0.02;

	var rng = new Random();

	Material PickMaterial() => brickMaterials[rng.Next(brickMaterials.Length)];

	double diag = cornerDistance / Math.Sqrt(2.0);

	DVector3[] corners =
	[
		new( diag, 0,  diag),
	new(-diag, 0,  diag),
	new( diag, 0, -diag),
	new(-diag, 0, -diag),
];

	for (int ci = 0; ci < corners.Length; ci++)
	{
		DVector3 basePos = corners[ci];

		for (int i = 0; i < boxesPerCorner; i++)
		{
			double y = cornerHeight + boxSize / 2.0 + i * (boxSize + stackGap);
			SpawnBox($"CornerBox_{ci}_{i}", new DVector3(basePos.X, y, basePos.Z), PickMaterial());
		}
	}

	for (int i = 0; i < randomBoxCount; i++)
	{
		double angle = rng.NextDouble() * Math.PI * 2.0;
		double r = spawnRadius * Math.Sqrt(rng.NextDouble());

		double x = Math.Cos(angle) * r;
		double z = Math.Sin(angle) * r;
		double y = 0.5 + boxSize / 2.0;

		SpawnBox($"RandomBox_{i}", new DVector3(x, y, z), PickMaterial());
	}

	// Player
	Entity playerEntity = scene.Add("Player");

	Transform playerTransform = playerEntity.Add<Transform>();
	playerTransform.Position = new DVector3(0.0, 2, 0);
	playerTransform.Scale = new DVector3(0.7, 0.9, 0.7);

	Mesh capsuleMesh = CapsuleMeshTemplate.Create(
		radius: 0.5,
		height: 2.0,
		segments: 24,
		ringsPerHemisphere: 8,
		cylinderSegments: 1);

	Material playerMaterial = new(
		"WhiteMat",
		"Basic",
		new DVector4(1, 1, 1, 1));

	MeshRenderer playerRenderer = playerEntity.Add<MeshRenderer>();
	playerRenderer.Mesh = capsuleMesh;
	playerRenderer.Material = playerMaterial;
	playerRenderer.Visible = true;

	Collider playerCollider = playerEntity.Add<Collider>();
	playerCollider.SimpleGeometry = true;

	PhysicsBody playerBody = playerEntity.Add<PhysicsBody>();
	playerBody.Mass = 75.0;
	playerBody.Drag = 0.05;
	playerBody.AngularDrag = 5.0;
	playerBody.UseGravity = true;
	playerBody.FreezeRotation = true;

	// Camera
	Entity cameraEntity = scene.Add("Camera");
	cameraEntity.Add<Transform>();

	Camera camera = cameraEntity.Add<Camera>();
	camera.AspectRatio = (double)parameters.Width / parameters.Height;

	physics.Update(scene, 0.0);

	PhysicsPlayerController playerController = new(
		playerEntity,
		camera,
		input,
		renderer,
		physics,
		lookTarget: new DVector3(0.0, 2.0, -100.0),
		moveSpeed: 6.0,
		jumpSpeed: 7.0,
		mouseSensitivity: 0.15,
		eyeHeight: 0.62);

	System.Diagnostics.Stopwatch stopwatch =
		System.Diagnostics.Stopwatch.StartNew();

	double previousTime = stopwatch.Elapsed.TotalSeconds;

	const double maxDeltaTime = 0.05;

	double fpsTimer = 0.0;
	int fpsFrames = 0;

	List<string> consoleLines = [];
	const int MaxConsoleLines = 32;

	void SpawnBox(string name, DVector3 position, Material material)
	{
		Entity box = scene.Add(name);

		Transform t = box.Add<Transform>();
		t.Position = position;
		t.Scale = new DVector3(boxSize, boxSize, boxSize);

		MeshRenderer r = box.Add<MeshRenderer>();
		r.Mesh = loadedMeshes[1];
		r.Material = material;
		r.Visible = true;

		Collider c = box.Add<Collider>();
		c.SimpleGeometry = false;

		PhysicsBody body = box.Add<PhysicsBody>();
		body.Mass = 10.0;
		body.Drag = 0.05;
		body.AngularDrag = 8.0;
		body.UseGravity = true;
		body.FreezeRotation = false;
	}

	while (!input.QuitRequested)
	{
		double currentTime = stopwatch.Elapsed.TotalSeconds;
		double realDeltaTime = currentTime - previousTime;
		previousTime = currentTime;

		double deltaTime = Math.Min(realDeltaTime, maxDeltaTime);

		fpsTimer += realDeltaTime;
		fpsFrames++;

		if (fpsTimer >= 1.0)
		{
			int currentFps = (int)Math.Round(fpsFrames / fpsTimer);

			counter.Get<Text>().Content = $"{currentFps} FPS";

			fpsTimer = 0.0;
			fpsFrames = 0;
		}

		foreach (LogEntry entry in Logger.DrainPending())
		{
			if (entry.Category == "RenderStats")
			{
				renderStatsLabel.Get<Text>().Content = entry.Message;
				continue;
			}

			string line =
				$"[{entry.Timestamp:HH:mm:ss}] " +
				$"[{entry.Level}] " +
				$"[{entry.Category}] " +
				entry.Message;

			if (entry.Exception is not null)
				line += $"\n{entry.Exception}";

			consoleLines.Add(line);

			if (consoleLines.Count > MaxConsoleLines)
				consoleLines.RemoveAt(0);
		}

		positionLabel.Get<Text>().Content = playerTransform.Position.ToString();

		consoleLabel.Get<Text>().Content = consoleLines.Count == 0
			? "Veonex Console"
			: string.Join("\n", consoleLines);

		input.Update();

		if (input.QuitRequested)
			break;

		uiDocument.Update(deltaTime);

		playerController.UpdateInput(deltaTime);

		if (input.WindowSizeChanged)
		{
			renderer.ResizeRenderTarget(
				input.WindowWidth,
				input.WindowHeight);

			if (input.WindowHeight > 0)
			{
				camera.AspectRatio =
					(double)input.WindowWidth / input.WindowHeight;
			}
		}

		if (input.IsKeyPressed(SDL_Scancode.SDL_SCANCODE_F11))
			renderer.ToggleFullscreen();

		scene.Update(deltaTime);
		physics.Update(scene, deltaTime);
		playerController.UpdateCamera();

		renderer.RenderFrame(
			scene,
			camera,
			currentTime,
			uiDocument);
	}

	stopwatch.Stop();
}
finally
{
	SDL3.SDL_Quit();
}