using SDL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Veonex.Assets;
using Veonex.Core;
using Veonex.Game;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Render;

// ============================================================
// Configuration
// ============================================================

const int maxHeight = 8;

const double blockSize = 2.0;
const double noiseScale = 0.08;

const int terrainSeed = 67;

const double maxDeltaTime = 0.05;

const double fpsUpdateInterval = 1.0;
const double lowFpsUpdateInterval = 5.0;

WindowParameters parameters = new()
{
	Width = 1920,
	Height = 1080,
	Title = "Veonex - Procedural Terrain",
	VSync = false,
	Resizable = true,
	Fullscreen = true
};

// ============================================================
// Runtime FPS measurements
// ============================================================

Stopwatch stopwatch = new();

long totalFrames = 0;

double totalFrameTimeMs = 0.0;
double currentFps = 0.0;
double averageFps = 0.0;
double low1Fps = 0.0;
double low01Fps = 0.0;
double frameTimeMs = 0.0;

double fpsUpdateTimer = 0.0;
double lowFpsUpdateTimer = 0.0;

long framesSinceUpdate = 0;

List<double> recentFrameTimes = new(4096);

// ============================================================
// SDL
// ============================================================

if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_VIDEO))
{
	throw new InvalidOperationException(
		$"Failed to initialize SDL3: {SDL3.SDL_GetError()}");
}

try
{
	// ========================================================
	// Window and renderer
	// ========================================================

	using RenderBackend renderer = new(parameters);

	InputManager input = new();

	// ========================================================
	// Scene
	// ========================================================

	Scene scene = new();

	// ========================================================
	// Camera
	// ========================================================

	Entity cameraEntity = scene.Add("Camera");

	Transform cameraTransform =
		cameraEntity.Add<Transform>();

	Camera camera =
		cameraEntity.Add<Camera>();

	cameraTransform.Position = new DVector3(
		0,
		18,
		30);

	camera.AspectRatio =
		(double)parameters.Width / parameters.Height;

	CameraController cameraController = new(
		camera,
		input,
		renderer,
		DVector3.Zero,
		moveSpeed: 12.0,
		mouseSensitivity: 0.15);

	// ========================================================
	// Grass material
	// ========================================================

	string grassTexturePath = Path.GetFullPath(
		Path.Combine("textures", "grass.png"));

	if (!File.Exists(grassTexturePath))
	{
		throw new FileNotFoundException(
			"Terrain texture was not found. Expected textures/grass.png.",
			grassTexturePath);
	}

	Material grassMaterial = new(
		"Grass",
		"Basic",
		new DVector4(0.48, 0.77, 0.23, 1.0),
		grassTexturePath);


	Console.WriteLine($"Loaded terrain texture: {grassTexturePath}");

	// ========================================================
	// Infinite chunk terrain
	// ========================================================

	using TerrainChunkManager terrainManager = new(
		scene,
		renderer,
		grassMaterial,
		chunkSize: 16,
		maxHeight: maxHeight,
		blockSize: blockSize,
		noiseScale: noiseScale,
		seed: terrainSeed,
		loadRadius: 9,
		buildsPerFrame: 1,
		animationDuration: 0.45);

	terrainManager.Initialize(cameraTransform.Position);

	Console.WriteLine(
		$"Initialized terrain chunks: {terrainManager.LoadedChunkCount}");

	// ========================================================
	// Load and center model.* (Assimp-supported format)
	// ========================================================

	string[] supportedModelExtensions =
	{
		".obj", ".fbx", ".gltf", ".glb", ".dae", ".3ds",
		".ply", ".stl", ".blend", ".x", ".ase", ".ifc"
	};

	string? modelPath = Directory
		.GetFiles(Directory.GetCurrentDirectory(), "model.*")
		.FirstOrDefault(path => supportedModelExtensions.Contains(
			Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));

	if (modelPath is null)
	{
		throw new FileNotFoundException(
			"Model file not found. Put a supported model named model.<format> " +
			"in the working directory (for example, model.obj or model.glb).");
	}

	IReadOnlyList<Mesh> modelMeshes = ModelLoader.Load(modelPath);

	if (modelMeshes.Count == 0 ||
		modelMeshes.All(mesh => mesh.Data.Positions.Length == 0))
	{
		throw new InvalidOperationException(
			$"Model contains no vertices: {modelPath}");
	}

	double minX = double.PositiveInfinity;
	double minY = double.PositiveInfinity;
	double minZ = double.PositiveInfinity;
	double maxX = double.NegativeInfinity;
	double maxY = double.NegativeInfinity;
	double maxZ = double.NegativeInfinity;

	foreach (Mesh mesh in modelMeshes)
	{
		foreach (DVector3 vertex in mesh.Data.Positions)
		{
			minX = Math.Min(minX, vertex.X);
			minY = Math.Min(minY, vertex.Y);
			minZ = Math.Min(minZ, vertex.Z);
			maxX = Math.Max(maxX, vertex.X);
			maxY = Math.Max(maxY, vertex.Y);
			maxZ = Math.Max(maxZ, vertex.Z);
		}
	}

	double sizeX = maxX - minX;
	double sizeY = maxY - minY;
	double sizeZ = maxZ - minZ;
	double largestDimension = Math.Max(sizeX, Math.Max(sizeY, sizeZ));

	if (largestDimension <= 0.0)
	{
		throw new InvalidOperationException(
			$"Model has invalid dimensions: {modelPath}");
	}

	// Keep the model's proportions and fit it inside a 4x4x4 box.
	double modelScale = 4.0 / largestDimension;
	double centerX = (minX + maxX) * 0.5;
	double centerY = (minY + maxY) * 0.5;
	double centerZ = (minZ + maxZ) * 0.5;

	double modelCenterY =
		terrainManager.GetSurfaceHeight(0.0, 0.0) + 2.0;

	for (int i = 0; i < modelMeshes.Count; i++)
	{
		Entity modelEntity = scene.Add($"Model_{i}");
		Transform modelTransform = modelEntity.Add<Transform>();
		modelTransform.Scale = new DVector3(modelScale, modelScale, modelScale);
		modelTransform.Position = new DVector3(
			-centerX * modelScale,
			modelCenterY - centerY * modelScale,
			-centerZ * modelScale);

		MeshRenderer modelRenderer = modelEntity.Add<MeshRenderer>();
		modelRenderer.Mesh = modelMeshes[i];
		modelRenderer.Material = grassMaterial;
	}

	Console.WriteLine(
		$"Loaded model: {Path.GetFileName(modelPath)} | " +
		$"Meshes: {modelMeshes.Count} | " +
		$"Size: {sizeX * modelScale:F2} x {sizeY * modelScale:F2} x {sizeZ * modelScale:F2}");

	// ========================================================
	// Start measurements
	// ========================================================

	stopwatch.Start();

	double previousTime =
		stopwatch.Elapsed.TotalSeconds;

	bool hasPreviousSample = false;

	// ========================================================
	// Main loop
	// ========================================================

	while (!input.QuitRequested)
	{
		double currentTime =
			stopwatch.Elapsed.TotalSeconds;

		// Actual elapsed time between loop iterations.
		double realDeltaTime =
			currentTime - previousTime;

		previousTime = currentTime;

		// Clamp simulation delta, but do not clamp statistics.
		double deltaTime = Math.Min(
			realDeltaTime,
			maxDeltaTime);

		// ----------------------------------------------------
		// FPS statistics
		// ----------------------------------------------------

		if (hasPreviousSample && realDeltaTime > 0.0)
		{
			frameTimeMs = realDeltaTime * 1000.0;

			recentFrameTimes.Add(frameTimeMs);

			totalFrames++;
			framesSinceUpdate++;

			totalFrameTimeMs += frameTimeMs;

			fpsUpdateTimer += realDeltaTime;
			lowFpsUpdateTimer += realDeltaTime;

			// Average session FPS based on measured frame time.
			double measuredSeconds =
				totalFrameTimeMs / 1000.0;

			averageFps = measuredSeconds > 0.0
				? totalFrames / measuredSeconds
				: 0.0;

			// Update current FPS approximately once per second.
			if (fpsUpdateTimer >= fpsUpdateInterval)
			{
				currentFps =
					framesSinceUpdate / fpsUpdateTimer;

				framesSinceUpdate = 0;
				fpsUpdateTimer = 0.0;

				Console.WriteLine(
					$"FPS: {currentFps,7:F1} | " +
					$"AVG: {averageFps,7:F1} | " +
					$"1% Low: {low1Fps,7:F1} | " +
					$"0.1% Low: {low01Fps,7:F1} | " +
					$"Frame: {frameTimeMs,7:F2} ms");

				try
				{
					Console.Title =
						$"Veonex | FPS: {currentFps:F1} | " +
						$"AVG: {averageFps:F1} | " +
						$"0.1% Low: {low01Fps:F1}";
				}
				catch (IOException)
				{
					// Console title is optional.
				}
				catch (PlatformNotSupportedException)
				{
					// Console title is optional.
				}
				catch (InvalidOperationException)
				{
					// Console title is optional.
				}
			}

			// Update rolling 0.1% Low approximately every five seconds.
			if (lowFpsUpdateTimer >= lowFpsUpdateInterval)
			{
				if (recentFrameTimes.Count > 0)
				{
					double[] recentSorted =
						recentFrameTimes.ToArray();

					Array.Sort(recentSorted);

					low01Fps = CalculateLowFpsFromSorted(
						recentSorted,
						0.001);
				}

				recentFrameTimes.Clear();
				lowFpsUpdateTimer = 0.0;
			}
		}
		else
		{
			hasPreviousSample = true;
		}

		// ----------------------------------------------------
		// Input
		// ----------------------------------------------------

		input.Update();

		if (input.QuitRequested)
			break;

		// ----------------------------------------------------
		// Resize
		// ----------------------------------------------------

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

		// ----------------------------------------------------
		// Fullscreen
		// ----------------------------------------------------

		if (input.IsKeyPressed(
			SDL_Scancode.SDL_SCANCODE_F11))
		{
			renderer.ToggleFullscreen();
		}

		// ----------------------------------------------------
		// Camera
		// ----------------------------------------------------

		cameraController.Update(deltaTime);
		terrainManager.Update(cameraTransform.Position, deltaTime);

		// ----------------------------------------------------
		// Render
		// ----------------------------------------------------

		renderer.RenderFrame(scene, camera);
	}
}
finally
{
	if (stopwatch.IsRunning)
		stopwatch.Stop();

	SDL3.SDL_Quit();
}

// ============================================================
// Runtime FPS helper
// ============================================================

static double CalculateLowFpsFromSorted(
	double[] sortedFrameTimes,
	double fraction)
{
	if (sortedFrameTimes.Length == 0 || fraction <= 0.0)
		return 0.0;

	int slowFrameCount = Math.Max(
		1,
		(int)Math.Ceiling(
			sortedFrameTimes.Length * fraction));

	slowFrameCount = Math.Min(
		slowFrameCount,
		sortedFrameTimes.Length);

	double slowFrameTimeSum = 0.0;

	int firstSlowFrame =
		sortedFrameTimes.Length - slowFrameCount;

	for (int i = firstSlowFrame;
		 i < sortedFrameTimes.Length;
		 i++)
	{
		slowFrameTimeSum += sortedFrameTimes[i];
	}

	double averageSlowFrameTime =
		slowFrameTimeSum / slowFrameCount;

	return averageSlowFrameTime > 0.0
		? 1000.0 / averageSlowFrameTime
		: 0.0;
}
