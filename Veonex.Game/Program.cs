
using SDL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Veonex.Core;
using Veonex.Game;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Render;

// ============================================================
// Configuration
// ============================================================

const int terrainWidth = 24;
const int terrainDepth = 24;
const int maxHeight = 8;

const double blockSize = 2.0;
const double noiseScale = 0.08;

const int terrainSeed = 42;
const int textureSeed = 128;

const double maxDeltaTime = 0.05;

const double fpsUpdateInterval = 1.0;
const double lowFpsUpdateInterval = 5.0;

const double frameTime60Fps = 1000.0 / 60.0;
const double frameTime30Fps = 1000.0 / 30.0;

WindowParameters parameters = new()
{
	Width = 1280,
	Height = 720,
	Title = "Veonex - Procedural Terrain",
	VSync = false,
	Resizable = true,
	Fullscreen = true
};

// ============================================================
// Statistics state
// Declared outside try so finally can save it.
// ============================================================

Stopwatch stopwatch = new();

bool statsStarted = false;

long totalFrames = 0;
long framesOver60Fps = 0;
long framesOver30Fps = 0;

int generatedBlocks = 0;
int loadedTextureCount = 0;

double totalFrameTimeMs = 0.0;
double currentFps = 0.0;
double averageFps = 0.0;
double low1Fps = 0.0;
double low01Fps = 0.0;
double frameTimeMs = 0.0;

double fpsUpdateTimer = 0.0;
double lowFpsUpdateTimer = 0.0;

long framesSinceUpdate = 0;

List<double> sessionFrameTimes = new(4096);
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
	// Textures
	// ========================================================

	string textureDirectory = "textures";

	if (!Directory.Exists(textureDirectory))
	{
		throw new DirectoryNotFoundException(
			$"Texture directory not found: {textureDirectory}");
	}

	string[] texturePaths = Directory
		.GetFiles(textureDirectory)
		.Where(path =>
		{
			string extension =
				Path.GetExtension(path).ToLowerInvariant();

			return extension is
				".png" or
				".jpg" or
				".jpeg" or
				".bmp" or
				".tga";
		})
		.OrderBy(path => path, StringComparer.Ordinal)
		.ToArray();

	if (texturePaths.Length == 0)
	{
		throw new InvalidOperationException(
			$"No supported textures found in: {textureDirectory}");
	}

	Random textureRandom = new(textureSeed);

	Material[] materials = texturePaths
		.Select(path => new Material(
			Path.GetFileNameWithoutExtension(path),
			"Basic",
			new DVector4(1, 1, 1, 1),
			path))
		.ToArray();

	loadedTextureCount = materials.Length;

	Console.WriteLine(
		$"Loaded {loadedTextureCount} terrain texture paths.");

	// ========================================================
	// Cube mesh
	// ========================================================

	Mesh cubeMesh = CubeMeshTemplate.Create();

	// ========================================================
	// Generate terrain
	// ========================================================

	PerlinNoise.Initialize(terrainSeed);

	double[,] heightMap = PerlinNoise.GenerateFbmMap2D(
		terrainWidth,
		terrainDepth,
		scale: noiseScale,
		offset: DVector2.Zero,
		octaves: 4,
		lacunarity: 2.0,
		gain: 0.5);

	for (int z = 0; z < terrainDepth; z++)
	{
		for (int x = 0; x < terrainWidth; x++)
		{
			double noise = heightMap[x, z];

			int columnHeight = 1 + (int)Math.Round(
				noise * (maxHeight - 1));

			columnHeight = Math.Clamp(
				columnHeight,
				1,
				maxHeight);

			double worldX =
				(x - (terrainWidth - 1) / 2.0) * blockSize;

			double worldZ =
				(z - (terrainDepth - 1) / 2.0) * blockSize;

			// Fill each column from the bottom upwards.
			for (int y = 0; y < columnHeight; y++)
			{
				Entity block = scene.Add(
					$"Terrain_{x}_{y}_{z}");

				Transform transform =
					block.Add<Transform>();

				transform.Position = new DVector3(
					worldX,
					y * blockSize,
					worldZ);

				MeshRenderer meshRenderer =
					block.Add<MeshRenderer>();

				meshRenderer.Mesh = cubeMesh;

				Material material = materials[
					textureRandom.Next(materials.Length)];

				meshRenderer.Material = material;

				generatedBlocks++;
			}
		}
	}

	Console.WriteLine(
		$"Generated terrain: {terrainWidth}x{terrainDepth}");

	Console.WriteLine(
		$"Total blocks: {generatedBlocks}");

	// ========================================================
	// Start measurements
	// ========================================================

	stopwatch.Start();
	statsStarted = true;

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

			sessionFrameTimes.Add(frameTimeMs);
			recentFrameTimes.Add(frameTimeMs);

			totalFrames++;
			framesSinceUpdate++;

			totalFrameTimeMs += frameTimeMs;

			fpsUpdateTimer += realDeltaTime;
			lowFpsUpdateTimer += realDeltaTime;

			if (frameTimeMs > frameTime60Fps)
				framesOver60Fps++;

			if (frameTimeMs > frameTime30Fps)
				framesOver30Fps++;

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

		// ----------------------------------------------------
		// Render
		// ----------------------------------------------------

		renderer.RenderFrame(scene, camera);
	}
}
finally
{
	// ========================================================
	// Save statistics on exit
	// ========================================================

	if (stopwatch.IsRunning)
		stopwatch.Stop();

	if (statsStarted)
	{
		try
		{
			double sessionDuration =
				stopwatch.Elapsed.TotalSeconds;

			double averageFrameTime =
				totalFrames > 0
					? totalFrameTimeMs / totalFrames
					: 0.0;

			double sessionAverageFps =
				totalFrameTimeMs > 0.0
					? totalFrames / (totalFrameTimeMs / 1000.0)
					: 0.0;

			double[] sortedFrameTimes =
				sessionFrameTimes.ToArray();

			Array.Sort(sortedFrameTimes);

			double minFrameTime =
				sortedFrameTimes.Length > 0
					? sortedFrameTimes[0]
					: 0.0;

			double maxFrameTime =
				sortedFrameTimes.Length > 0
					? sortedFrameTimes[^1]
					: 0.0;

			double medianFrameTime =
				GetPercentile(sortedFrameTimes, 0.50);

			double p95FrameTime =
				GetPercentile(sortedFrameTimes, 0.95);

			double p99FrameTime =
				GetPercentile(sortedFrameTimes, 0.99);

			double sessionLow1Fps =
				CalculateLowFpsFromSorted(
					sortedFrameTimes,
					0.01);

			double sessionLow01Fps =
				CalculateLowFpsFromSorted(
					sortedFrameTimes,
					0.001);

			double over60Percent = totalFrames > 0
				? framesOver60Fps * 100.0 / totalFrames
				: 0.0;

			double over30Percent = totalFrames > 0
				? framesOver30Fps * 100.0 / totalFrames
				: 0.0;

			string statsPath =
				Path.GetFullPath("stats.txt");

			using (StreamWriter writer = new(
				statsPath,
				false,
				new UTF8Encoding(false)))
			{
				writer.WriteLine("========================================");
				writer.WriteLine("          VEONEX SESSION STATS");
				writer.WriteLine("========================================");
				writer.WriteLine();

				writer.WriteLine(
					$"Report generated: {DateTimeOffset.Now:O}");

				writer.WriteLine(
					$"Session duration: {FormatDuration(sessionDuration)}");

				writer.WriteLine(
					$"Session duration (seconds): {sessionDuration:F3}");

				writer.WriteLine(
					$"Working directory: {Environment.CurrentDirectory}");

				writer.WriteLine();

				writer.WriteLine("----- ENVIRONMENT -----");
				writer.WriteLine($"OS: {Environment.OSVersion}");
				writer.WriteLine($".NET: {Environment.Version}");
				writer.WriteLine(
					$"Process architecture: {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");

				writer.WriteLine(
					$"Processor count: {Environment.ProcessorCount}");

				writer.WriteLine();

				writer.WriteLine("----- WINDOW -----");
				writer.WriteLine(
					$"Configured resolution: {parameters.Width}x{parameters.Height}");

				writer.WriteLine($"Title: {parameters.Title}");
				writer.WriteLine($"Fullscreen: {parameters.Fullscreen}");
				writer.WriteLine($"VSync: {parameters.VSync}");
				writer.WriteLine($"Resizable: {parameters.Resizable}");

				writer.WriteLine();

				writer.WriteLine("----- TERRAIN -----");
				writer.WriteLine(
					$"Dimensions: {terrainWidth}x{terrainDepth}");

				writer.WriteLine($"Maximum column height: {maxHeight}");
				writer.WriteLine($"Block size: {blockSize:F2}");
				writer.WriteLine($"Noise scale: {noiseScale:F4}");
				writer.WriteLine($"Terrain seed: {terrainSeed}");
				writer.WriteLine($"Texture seed: {textureSeed}");
				writer.WriteLine($"Texture paths found: {loadedTextureCount}");
				writer.WriteLine($"Generated blocks: {generatedBlocks}");

				writer.WriteLine();

				writer.WriteLine("----- FPS -----");
				writer.WriteLine($"Measured frames: {totalFrames}");
				writer.WriteLine($"Last reported FPS: {currentFps:F2}");
				writer.WriteLine(
					$"Average FPS (entire session): {sessionAverageFps:F2}");

				writer.WriteLine(
					$"1% Low FPS (entire session): {sessionLow1Fps:F2}");

				writer.WriteLine(
					$"0.1% Low FPS (entire session): {sessionLow01Fps:F2}");

				writer.WriteLine();

				writer.WriteLine("----- FRAME TIME -----");
				writer.WriteLine(
					$"Average frame time: {averageFrameTime:F3} ms");

				writer.WriteLine($"Minimum frame time: {minFrameTime:F3} ms");
				writer.WriteLine($"Maximum frame time: {maxFrameTime:F3} ms");
				writer.WriteLine($"Median frame time (P50): {medianFrameTime:F3} ms");
				writer.WriteLine($"P95 frame time: {p95FrameTime:F3} ms");
				writer.WriteLine($"P99 frame time: {p99FrameTime:F3} ms");

				writer.WriteLine();

				writer.WriteLine("----- SLOW FRAMES -----");

				writer.WriteLine(
					$"Frames slower than 16.67 ms (~below 60 FPS): {framesOver60Fps}");

				writer.WriteLine(
					$"Percentage slower than 16.67 ms: {over60Percent:F2}%");

				writer.WriteLine(
					$"Frames slower than 33.33 ms (~below 30 FPS): {framesOver30Fps}");

				writer.WriteLine(
					$"Percentage slower than 33.33 ms: {over30Percent:F2}%");

				writer.WriteLine();

				writer.WriteLine("----- NOTES -----");
				writer.WriteLine(
					"FPS is calculated from measured time between loop iterations.");

				writer.WriteLine(
					"Simulation delta time is clamped to 50 ms, but statistics use unclamped time.");

				writer.WriteLine(
					"1% Low and 0.1% Low are calculated as the reciprocal of the average frame time among the slowest 1% and 0.1% of measured frames.");

				writer.WriteLine(
					"Frame times are collected throughout the session; the final low-FPS values use the complete sample.");

				writer.WriteLine();
				writer.WriteLine("========================================");
				writer.WriteLine("                 END");
				writer.WriteLine("========================================");
			}

			Console.WriteLine();
			Console.WriteLine($"Statistics saved to: {statsPath}");
		}
		catch (Exception exception)
		{
			Console.Error.WriteLine(
				$"Failed to save stats.txt: {exception}");
		}
	}

	SDL3.SDL_Quit();
}

// ============================================================
// Statistics helpers
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

static double GetPercentile(
	double[] sortedValues,
	double percentile)
{
	if (sortedValues.Length == 0)
		return 0.0;

	percentile = Math.Clamp(percentile, 0.0, 1.0);

	int index = (int)Math.Ceiling(
		percentile * sortedValues.Length) - 1;

	index = Math.Clamp(
		index,
		0,
		sortedValues.Length - 1);

	return sortedValues[index];
}

static string FormatDuration(double seconds)
{
	TimeSpan duration = TimeSpan.FromSeconds(
		Math.Max(0.0, seconds));

	return $"{(int)duration.TotalHours:D2}:" +
		   $"{duration.Minutes:D2}:" +
		   $"{duration.Seconds:D2}";
}