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
using Transform = Veonex.Core.Transform;

/// <summary>
/// Mipmap stress test:
///  - giant grass.png floor (minification + mip test on natural texture)
///  - long prototype.png "grid runway" going into the distance
///  - images.png side walls forming a textured corridor
///  - procedural black/white checkerboard far away (definitive grid test)
///  - reference planes at increasing distance with all three textures
///  - physics-driven first-person player
/// </summary>
WindowParameters parameters = new()
{
	Width = 1920,
	Height = 1080,
	Title = "Veonex - Mipmap Stress Test",
	VSync = true,
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

	// ==================== Текстуры ====================
	const string texGrass = "assets/textures/grass.png";
	const string texImages = "assets/textures/images.png";
	const string texProto = "assets/textures/prototype.png";

	Material grassMat = new("GrassMat", "Basic", new DVector4(1, 1, 1, 1), texGrass);
	Material imagesMat = new("ImagesMat", "Basic", new DVector4(1, 1, 1, 1), texImages);
	Material protoMat = new("ProtoMat", "Basic", new DVector4(1, 1, 1, 1), texProto);

	// Сплошные цвета для процедурной шахматки (не используют текстуру — рендерер
	// подставит внутреннюю белую 1x1 текстуру).
	Material blackTile = new("BlackTile", "Basic", new DVector4(0.05, 0.05, 0.05, 1));
	Material whiteTile = new("WhiteTile", "Basic", new DVector4(0.95, 0.95, 0.95, 1));

	Material[] texturedMats = [grassMat, imagesMat, protoMat];

	// ==================== Гигантский травяной пол ====================
	// Текстура растянута на 500x500 — отличный тест minification + мипмапов
	// (вдали должна плавно усредняться, без мерцания).
	Entity grassFloor = scene.Add("GrassFloor");
	Transform grassFloorT = grassFloor.Add<Transform>();
	grassFloorT.Position = new DVector3(0, -0.5, 0);
	grassFloorT.Scale = new DVector3(500, 0.5, 500);
	MeshRenderer grassFloorR = grassFloor.Add<MeshRenderer>();
	grassFloorR.Mesh = cubeMesh;
	grassFloorR.Material = grassMat;
	Collider grassFloorC = grassFloor.Add<Collider>();
	grassFloorC.SimpleGeometry = true;

	// ==================== Длинная "полоса" из prototype.png ====================
	// Идёт вдаль по центру коридора. У прототип-текстур обычно есть сетка —
	// если да, это и есть "сеточка вдали" прямо на геометрии.
	Entity runway = scene.Add("GridRunway");
	Transform runwayT = runway.Add<Transform>();
	runwayT.Position = new DVector3(0, -0.23, -120);
	runwayT.Scale = new DVector3(40, 0.02, 280); // от Z = +20 до Z = -260
	MeshRenderer runwayR = runway.Add<MeshRenderer>();
	runwayR.Mesh = cubeMesh;
	runwayR.Material = protoMat;

	// ==================== Боковые стены из images.png ====================
	for (int side = -1; side <= 1; side += 2)
	{
		Entity wall = scene.Add($"Wall_{(side < 0 ? "L" : "R")}");
		Transform wallT = wall.Add<Transform>();
		wallT.Position = new DVector3(side * 20, 5, -120);
		wallT.Scale = new DVector3(0.5, 10, 280);
		MeshRenderer wallR = wall.Add<MeshRenderer>();
		wallR.Mesh = cubeMesh;
		wallR.Material = imagesMat;
		Collider wallC = wall.Add<Collider>();
		wallC.SimpleGeometry = true;
	}

	// ==================== Дальняя стена из prototype.png ====================
	Entity farWall = scene.Add("FarWall");
	Transform farWallT = farWall.Add<Transform>();
	farWallT.Position = new DVector3(0, 10, -260);
	farWallT.Scale = new DVector3(50, 25, 0.5);
	MeshRenderer farWallR = farWall.Add<MeshRenderer>();
	farWallR.Mesh = cubeMesh;
	farWallR.Material = protoMat;

	// ==================== Процедурная шахматка вдали ====================
	// Эталонный тест "сеточки вдали", не зависящий от содержимого текстуры.
	// 10 колонок x 20 рядов по 4 юнита = 40 x 80 юнитов, на Z = -100..-180.
	const int gridCols = 10;
	const int gridRows = 20;
	const double tileSize = 4.0;
	const double gridStartZ = -100.0;
	const double gridY = -0.20;

	int checkerCount = 0;
	for (int row = 0; row < gridRows; row++)
	{
		for (int col = 0; col < gridCols; col++)
		{
			bool isDark = (row + col) % 2 == 0;
			Material mat = isDark ? blackTile : whiteTile;

			Entity tile = scene.Add($"Checker_{row:00}_{col:00}");
			Transform tileT = tile.Add<Transform>();
			tileT.Position = new DVector3(
				(col - (gridCols - 1) * 0.5) * tileSize,
				gridY,
				gridStartZ - row * tileSize);
			tileT.Scale = new DVector3(tileSize, 0.02, tileSize);

			MeshRenderer tileR = tile.Add<MeshRenderer>();
			tileR.Mesh = cubeMesh;
			tileR.Material = mat;
			checkerCount++;
		}
	}

	// ==================== Опорные плоскости вдали (все три текстуры) ====================
	// 9 плоскостей, уходящих вдаль, чередуя текстуры. Размер растёт с дистанцией,
	// чтобы на экране они занимали похожую площадь — удобно сравнивать мип-переходы.
	const int refPlaneCount = 9;
	const double refStartZ = -5.0;
	const double refSpacing = 6.0;
	const double refBaseSize = 2.0;

	for (int i = 0; i < refPlaneCount; i++)
	{
		Entity p = scene.Add($"RefPlane_{i:00}");
		Transform pT = p.Add<Transform>();
		double size = refBaseSize + i * 1.2;
		pT.Position = new DVector3(
			(i % 3 - 1) * 7.0,
			size * 0.5 + 0.2,
			refStartZ - i * refSpacing);
		pT.Scale = new DVector3(size, size, 0.05);

		MeshRenderer pR = p.Add<MeshRenderer>();
		pR.Mesh = cubeMesh;
		pR.Material = texturedMats[i % 3];
	}

	// ==================== Ближние опорные плоскости (по одной на текстуру) ====================
	for (int i = 0; i < 3; i++)
	{
		Entity p = scene.Add($"CloseRef_{i}");
		Transform pT = p.Add<Transform>();
		pT.Position = new DVector3(-4.0 + i * 4.0, 1.5, 6.0);
		pT.Scale = new DVector3(2.0, 2.0, 0.05);

		MeshRenderer pR = p.Add<MeshRenderer>();
		pR.Mesh = cubeMesh;
		pR.Material = texturedMats[i];
	}

	// ==================== Декоративные колонны вдоль коридора ====================
	// Нагрузка текстурами: 20 колонн с чередующимися текстурами.
	for (int i = 0; i < 10; i++)
	{
		for (int side = -1; side <= 1; side += 2)
		{
			Entity p = scene.Add($"Pillar_{i:00}_{(side < 0 ? "L" : "R")}");
			Transform pT = p.Add<Transform>();
			pT.Position = new DVector3(side * 14, 3, -20 - i * 22);
			pT.Scale = new DVector3(1.5, 6, 1.5);

			MeshRenderer pR = p.Add<MeshRenderer>();
			pR.Mesh = cubeMesh;
			pR.Material = texturedMats[(i + (side > 0 ? 1 : 0)) % 3];
		}
	}

	// ==================== Игрок ====================
	Entity playerEntity = scene.Add("Player");

	Transform playerTransform = playerEntity.Add<Transform>();
	playerTransform.Position = new DVector3(0.0, 1.05, 11.0);
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

	// ==================== Камера ====================
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

	Console.WriteLine("Mipmap stress test ready.");

	Console.WriteLine(
		"WASD: move | Space: jump | Mouse: look | " +
		"Esc: release mouse | Left click: recapture | F11: fullscreen");

	Console.WriteLine(
		$"Textures: grass.png + images.png + prototype.png | " +
		$"Checkerboard tiles: {checkerCount} | " +
		$"Dynamic bodies: {physics.BodyCount} | " +
		$"Colliders: {physics.ColliderCount}");

	System.Diagnostics.Stopwatch stopwatch =
		System.Diagnostics.Stopwatch.StartNew();

	double previousTime = stopwatch.Elapsed.TotalSeconds;

	const double maxDeltaTime = 0.05;

	double fpsTimer = 0.0;
	int fpsFrames = 0;

	List<string> consoleLines = [];
	const int MaxConsoleLines = 32;

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