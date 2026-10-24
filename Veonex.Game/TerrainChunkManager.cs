using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Veonex.Core;
using Veonex.Mathematics;
using Veonex.Render;

namespace Veonex.Game;

public sealed class TerrainChunkManager : IDisposable
{
	private const int Octaves = 4;
	private const int SubdivisionsPerCell = 2;
	private const double Lacunarity = 2.0;
	private const double Persistence = 0.5;
	private const double MinAnimatedScale = 0.015;
	private const double AnimationDrop = 1.4;

	private readonly Scene _scene;
	private readonly RenderBackend _renderer;
	private readonly Material _material;
	private readonly int _chunkSize;
	private readonly int _maxHeight;
	private readonly double _blockSize;
	private readonly double _noiseScale;
	private readonly int _seed;
	private readonly int _loadRadius;
	private readonly int _buildsPerFrame;
	private readonly int _maxConcurrentBuilds;
	private readonly int _maxUploadsPerFrame;
	private readonly double _animationDuration;
	private readonly double _chunkWorldSize;

	private readonly Dictionary<ChunkCoordinate, Chunk> _chunks = [];
	private readonly Dictionary<ChunkCoordinate, PendingBuild> _pendingBuilds = [];
	private readonly HashSet<ChunkCoordinate> _failedChunks = [];

	private bool _initialized;
	private bool _disposed;

	public int LoadedChunkCount => _chunks.Count;
	public int PendingChunkCount => _pendingBuilds.Count;

	public event Action<Exception>? GenerationFailed;

	private readonly record struct ChunkCoordinate(int X, int Z);

	private sealed class Chunk
	{
		public Entity Entity { get; }
		public Mesh Mesh { get; }
		public Transform Transform { get; }
		public ChunkCoordinate Coordinate { get; }
		public double Progress { get; set; }
		public bool TargetVisible { get; set; } = true;

		public Chunk(
			Entity entity,
			Mesh mesh,
			Transform transform,
			ChunkCoordinate coordinate)
		{
			Entity = entity;
			Mesh = mesh;
			Transform = transform;
			Coordinate = coordinate;
		}
	}

	private sealed class PendingBuild
	{
		public Task<MeshData> Task { get; }
		public CancellationTokenSource Cancellation { get; }

		public PendingBuild(
			Task<MeshData> task,
			CancellationTokenSource cancellation)
		{
			Task = task;
			Cancellation = cancellation;
		}
	}

	public TerrainChunkManager(
		Scene scene,
		RenderBackend renderer,
		Material material,
		int chunkSize = 16,
		int maxHeight = 8,
		double blockSize = 2.0,
		double noiseScale = 0.08,
		int seed = 67,
		int loadRadius = 3,
		int buildsPerFrame = 4,
		double animationDuration = 0.45,
		int maxConcurrentBuilds = 2,
		int maxUploadsPerFrame = 2)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(renderer);
		ArgumentNullException.ThrowIfNull(material);

		if (chunkSize < 2)
			throw new ArgumentOutOfRangeException(nameof(chunkSize));
		if (maxHeight < 1)
			throw new ArgumentOutOfRangeException(nameof(maxHeight));
		if (blockSize <= 0.0)
			throw new ArgumentOutOfRangeException(nameof(blockSize));
		if (noiseScale <= 0.0)
			throw new ArgumentOutOfRangeException(nameof(noiseScale));
		if (loadRadius < 1)
			throw new ArgumentOutOfRangeException(nameof(loadRadius));
		if (buildsPerFrame < 1)
			throw new ArgumentOutOfRangeException(nameof(buildsPerFrame));
		if (animationDuration <= 0.0)
			throw new ArgumentOutOfRangeException(nameof(animationDuration));
		if (maxConcurrentBuilds < 1)
			throw new ArgumentOutOfRangeException(nameof(maxConcurrentBuilds));
		if (maxUploadsPerFrame < 1)
			throw new ArgumentOutOfRangeException(nameof(maxUploadsPerFrame));

		_scene = scene;
		_renderer = renderer;
		_material = material;
		_chunkSize = chunkSize;
		_maxHeight = maxHeight;
		_blockSize = blockSize;
		_noiseScale = noiseScale;
		_seed = seed;
		_loadRadius = loadRadius;
		_buildsPerFrame = buildsPerFrame;
		_maxConcurrentBuilds = maxConcurrentBuilds;
		_maxUploadsPerFrame = maxUploadsPerFrame;
		_animationDuration = animationDuration;
		_chunkWorldSize = _chunkSize * _blockSize;
	}

	public void Initialize(DVector3 cameraPosition)
	{
		ThrowIfDisposed();

		if (_initialized)
			return;

		_initialized = true;
	}

	public void Update(DVector3 cameraPosition, double deltaTime)
	{
		ThrowIfDisposed();

		if (!_initialized)
			Initialize(cameraPosition);

		deltaTime = Math.Clamp(deltaTime, 0.0, 0.1);

		ChunkCoordinate center = GetChunkCoordinate(cameraPosition);
		HashSet<ChunkCoordinate> desired = BuildDesired(center);

		foreach (Chunk chunk in _chunks.Values)
			chunk.TargetVisible = desired.Contains(chunk.Coordinate);

		CancelObsoleteBuilds(desired);
		ProcessCompletedBuilds(desired);
		ScheduleMissingChunks(desired, center);

		List<Chunk> chunksToRemove = [];

		double progressStep = deltaTime / _animationDuration;

		foreach (Chunk chunk in _chunks.Values)
		{
			if (chunk.TargetVisible)
				chunk.Progress = Math.Min(1.0, chunk.Progress + progressStep);
			else
				chunk.Progress = Math.Max(0.0, chunk.Progress - progressStep);

			double t = chunk.Progress;
			double eased = t * t * (3.0 - 2.0 * t);
			double scale = MinAnimatedScale + (1.0 - MinAnimatedScale) * eased;

			chunk.Transform.Scale = new DVector3(scale, scale, scale);

			chunk.Transform.Position = new DVector3(
				chunk.Coordinate.X * _chunkWorldSize,
				-AnimationDrop * (1.0 - eased),
				chunk.Coordinate.Z * _chunkWorldSize);

			if (!chunk.TargetVisible && chunk.Progress <= 0.0)
				chunksToRemove.Add(chunk);
		}

		foreach (Chunk chunk in chunksToRemove)
		{
			_scene.Remove(chunk.Entity);
			_chunks.Remove(chunk.Coordinate);
		}
	}

	public double GetSurfaceHeight(double worldX, double worldZ)
	{
		ThrowIfDisposed();

		double cellX = worldX / _blockSize;
		double cellZ = worldZ / _blockSize;

		return GetTerrainHeight(cellX, cellZ) * _blockSize;
	}

	private void CancelObsoleteBuilds(HashSet<ChunkCoordinate> desired)
	{
		foreach (KeyValuePair<ChunkCoordinate, PendingBuild> pair
			in _pendingBuilds.ToArray())
		{
			if (desired.Contains(pair.Key))
				continue;

			PendingBuild pending = pair.Value;

			if (!pending.Cancellation.IsCancellationRequested)
				pending.Cancellation.Cancel();

			if (pending.Task.IsCompleted)
				RemovePendingBuild(pair.Key, pending);
		}

		foreach (ChunkCoordinate coordinate in _failedChunks.ToArray())
		{
			if (!desired.Contains(coordinate))
				_failedChunks.Remove(coordinate);
		}
	}

	private void ProcessCompletedBuilds(HashSet<ChunkCoordinate> desired)
	{
		int uploads = 0;

		foreach (KeyValuePair<ChunkCoordinate, PendingBuild> pair
			in _pendingBuilds.ToArray())
		{
			ChunkCoordinate coordinate = pair.Key;
			PendingBuild pending = pair.Value;
			Task<MeshData> task = pending.Task;

			if (!task.IsCompleted)
				continue;

			if (!desired.Contains(coordinate))
			{
				RemovePendingBuild(coordinate, pending);
				continue;
			}

			if (task.IsCanceled)
			{
				RemovePendingBuild(coordinate, pending);
				continue;
			}

			if (task.IsFaulted)
			{
				Exception exception = task.Exception?.GetBaseException()
					?? new InvalidOperationException("Chunk generation failed.");

				RemovePendingBuild(coordinate, pending);
				_failedChunks.Add(coordinate);
				GenerationFailed?.Invoke(exception);
				continue;
			}

			if (uploads >= _maxUploadsPerFrame)
				continue;

			MeshData data = task.GetAwaiter().GetResult();

			RemovePendingBuild(coordinate, pending);

			if (_disposed ||
				!desired.Contains(coordinate) ||
				_chunks.ContainsKey(coordinate))
			{
				continue;
			}

			CreateChunk(coordinate, data);
			uploads++;
		}
	}

	private void ScheduleMissingChunks(
		HashSet<ChunkCoordinate> desired,
		ChunkCoordinate center)
	{
		int availableSlots = _maxConcurrentBuilds - _pendingBuilds.Count;

		if (availableSlots <= 0)
			return;

		int countToSchedule = Math.Min(
			availableSlots,
			_buildsPerFrame);

		List<ChunkCoordinate> missing = desired
			.Where(coordinate =>
				!_chunks.ContainsKey(coordinate) &&
				!_pendingBuilds.ContainsKey(coordinate) &&
				!_failedChunks.Contains(coordinate))
			.OrderBy(coordinate => DistanceSquared(coordinate, center))
			.Take(countToSchedule)
			.ToList();

		foreach (ChunkCoordinate coordinate in missing)
			StartBuild(coordinate);
	}

	private void StartBuild(ChunkCoordinate coordinate)
	{
		CancellationTokenSource cancellation = new();

		try
		{
			Task<MeshData> task = Task.Run(
				() => BuildChunkMeshData(coordinate, cancellation.Token),
				cancellation.Token);

			_pendingBuilds.Add(
				coordinate,
				new PendingBuild(task, cancellation));
		}
		catch
		{
			cancellation.Dispose();
			throw;
		}
	}

	private void RemovePendingBuild(
		ChunkCoordinate coordinate,
		PendingBuild pending)
	{
		if (!_pendingBuilds.Remove(coordinate))
			return;

		if (pending.Task.IsFaulted)
			_ = pending.Task.Exception;

		pending.Cancellation.Dispose();
	}

	private HashSet<ChunkCoordinate> BuildDesired(ChunkCoordinate center)
	{
		HashSet<ChunkCoordinate> desired = [];

		for (int z = -_loadRadius; z <= _loadRadius; z++)
		{
			for (int x = -_loadRadius; x <= _loadRadius; x++)
				desired.Add(new ChunkCoordinate(center.X + x, center.Z + z));
		}

		return desired;
	}

	private ChunkCoordinate GetChunkCoordinate(DVector3 worldPosition)
	{
		int x = (int)Math.Floor(worldPosition.X / _chunkWorldSize + 0.5);
		int z = (int)Math.Floor(worldPosition.Z / _chunkWorldSize + 0.5);

		return new ChunkCoordinate(x, z);
	}

	private static long DistanceSquared(ChunkCoordinate a, ChunkCoordinate b)
	{
		long dx = (long)a.X - b.X;
		long dz = (long)a.Z - b.Z;

		return dx * dx + dz * dz;
	}

	private void CreateChunk(ChunkCoordinate coordinate, MeshData data)
	{
		Mesh mesh = new(
			$"TerrainChunk_{coordinate.X}_{coordinate.Z}",
			data);

		Entity entity = _scene.Add(
			$"TerrainChunk_{coordinate.X}_{coordinate.Z}");

		Transform transform = entity.Add<Transform>();

		transform.Position = new DVector3(
			coordinate.X * _chunkWorldSize,
			-AnimationDrop,
			coordinate.Z * _chunkWorldSize);

		transform.Scale = new DVector3(
			MinAnimatedScale,
			MinAnimatedScale,
			MinAnimatedScale);

		MeshRenderer meshRenderer = entity.Add<MeshRenderer>();
		meshRenderer.Mesh = mesh;
		meshRenderer.Material = _material;
		meshRenderer.Visible = true;

		_chunks.Add(
			coordinate,
			new Chunk(entity, mesh, transform, coordinate));
	}

	private MeshData BuildChunkMeshData(
		ChunkCoordinate coordinate,
		CancellationToken cancellationToken)
	{
		int resolution = _chunkSize * SubdivisionsPerCell;
		int rowSize = resolution + 1;

		double sampleStep = 1.0 / SubdivisionsPerCell;

		double firstCellX =
			coordinate.X * _chunkSize - _chunkSize / 2.0;

		double firstCellZ =
			coordinate.Z * _chunkSize - _chunkSize / 2.0;

		double[,] heights = new double[resolution + 3, resolution + 3];

		for (int z = -1; z <= resolution + 1; z++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			for (int x = -1; x <= resolution + 1; x++)
			{
				double sampleX = firstCellX + x * sampleStep;
				double sampleZ = firstCellZ + z * sampleStep;

				heights[x + 1, z + 1] =
					GetTerrainHeight(sampleX, sampleZ);
			}
		}

		List<DVector3> positions = new(rowSize * rowSize);
		List<DVector3> normals = new(rowSize * rowSize);
		List<DVector2> uvs = new(rowSize * rowSize);
		List<uint> indices = new(resolution * resolution * 6);

		for (int z = 0; z <= resolution; z++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			for (int x = 0; x <= resolution; x++)
			{
				double localX =
					(x * sampleStep - _chunkSize / 2.0) * _blockSize;

				double localZ =
					(z * sampleStep - _chunkSize / 2.0) * _blockSize;

				double height = heights[x + 1, z + 1];

				double slopeX =
					(heights[x + 2, z + 1] - heights[x, z + 1])
					* SubdivisionsPerCell * 0.5;

				double slopeZ =
					(heights[x + 1, z + 2] - heights[x + 1, z])
					* SubdivisionsPerCell * 0.5;

				DVector3 normal = Normalize(
					new DVector3(-slopeX, 1.0, -slopeZ));

				positions.Add(new DVector3(
					localX,
					height * _blockSize,
					localZ));

				normals.Add(normal);

				uvs.Add(new DVector2(
					x * sampleStep,
					z * sampleStep));
			}
		}

		for (int z = 0; z < resolution; z++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			for (int x = 0; x < resolution; x++)
			{
				uint a = (uint)(z * rowSize + x);
				uint b = (uint)((z + 1) * rowSize + x);
				uint c = b + 1;
				uint d = a + 1;

				indices.Add(a);
				indices.Add(c);
				indices.Add(b);

				indices.Add(a);
				indices.Add(d);
				indices.Add(c);
			}
		}

		cancellationToken.ThrowIfCancellationRequested();

		return new MeshData(
			positions.ToArray(),
			indices.ToArray(),
			normals.ToArray(),
			uvs.ToArray());
	}

	private static DVector3 Normalize(DVector3 value)
	{
		double length = Math.Sqrt(
			value.X * value.X +
			value.Y * value.Y +
			value.Z * value.Z);

		if (length <= 1e-12)
			return new DVector3(0.0, 1.0, 0.0);

		return new DVector3(
			value.X / length,
			value.Y / length,
			value.Z / length);
	}

	private double GetTerrainHeight(double cellX, double cellZ)
	{
		double total = 0.0;
		double totalAmplitude = 0.0;
		double amplitude = 1.0;
		double frequency = _noiseScale;

		for (int octave = 0; octave < Octaves; octave++)
		{
			total += ValueNoise(
				cellX * frequency,
				cellZ * frequency,
				unchecked(_seed + octave * 1013)) * amplitude;

			totalAmplitude += amplitude;
			amplitude *= Persistence;
			frequency *= Lacunarity;
		}

		double noise = totalAmplitude > 0.0
			? total / totalAmplitude
			: 0.5;

		return 1.0 + noise * (_maxHeight - 1);
	}

	private static double ValueNoise(double x, double z, int seed)
	{
		double floorX = Math.Floor(x);
		double floorZ = Math.Floor(z);

		int x0 = unchecked((int)floorX);
		int z0 = unchecked((int)floorZ);
		int x1 = unchecked(x0 + 1);
		int z1 = unchecked(z0 + 1);

		double tx = Fade(x - floorX);
		double tz = Fade(z - floorZ);

		double a = Lerp(
			HashNoise(x0, z0, seed),
			HashNoise(x1, z0, seed),
			tx);

		double b = Lerp(
			HashNoise(x0, z1, seed),
			HashNoise(x1, z1, seed),
			tx);

		return Lerp(a, b, tz);
	}

	private static double HashNoise(int x, int z, int seed)
	{
		unchecked
		{
			uint value = (uint)x * 374761393u;
			value += (uint)z * 668265263u;
			value += (uint)seed * 2246822519u;
			value = (value ^ (value >> 13)) * 1274126177u;
			value ^= value >> 16;

			return (value & 0x00FFFFFFu) / (double)0x00FFFFFFu;
		}
	}

	private static double Fade(double t)
	{
		return t * t * (3.0 - 2.0 * t);
	}

	private static double Lerp(double a, double b, double t)
	{
		return a + (b - a) * t;
	}

	private void ThrowIfDisposed()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		foreach (PendingBuild pending in _pendingBuilds.Values)
		{
			pending.Cancellation.Cancel();

			if (pending.Task.IsCompleted)
			{
				if (pending.Task.IsFaulted)
					_ = pending.Task.Exception;

				pending.Cancellation.Dispose();
			}
			else
			{
				_ = pending.Task.ContinueWith(
					task =>
					{
						_ = task.Exception;
						pending.Cancellation.Dispose();
					},
					CancellationToken.None,
					TaskContinuationOptions.ExecuteSynchronously,
					TaskScheduler.Default);
			}
		}

		_pendingBuilds.Clear();
		_failedChunks.Clear();

		foreach (Chunk chunk in _chunks.Values)
			_scene.Remove(chunk.Entity);

		_chunks.Clear();
	}
}