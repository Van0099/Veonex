using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
	private const double InvFF = 1.0 / 0x00FFFFFF;
	private const double InvSubdivisions = 1.0 / SubdivisionsPerCell;

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
	private readonly double _halfChunkCells;
	private readonly double _heightRange;
	private readonly double _invTotalAmplitude;
	private readonly double[] _octaveFrequencies;
	private readonly double[] _octaveAmplitudes;
	private readonly uint[] _octaveSeedHashes;
	private readonly ChunkCoordinate[] _offsets;
	private readonly HashSet<ChunkCoordinate> _desired;

	private readonly Dictionary<ChunkCoordinate, Chunk> _chunks = [];
	private readonly Dictionary<ChunkCoordinate, PendingBuild> _pendingBuilds = [];
	private readonly HashSet<ChunkCoordinate> _failedChunks = [];

	private readonly List<Chunk> _chunksToRemove = [];
	private readonly List<ChunkCoordinate> _pendingRemoveList = [];
	private readonly List<ChunkCoordinate> _failedRemoveList = [];
	private readonly List<ChunkCoordinate> _missingList = [];

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
		int maxConcurrentBuilds = 8,
		int maxUploadsPerFrame = 8)
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
		_halfChunkCells = _chunkSize * 0.5;
		_heightRange = _maxHeight - 1;

		int side = loadRadius * 2 + 1;
		int capacity = side * side;

		_offsets = new ChunkCoordinate[capacity];
		int offsetIndex = 0;
		for (int z = -loadRadius; z <= loadRadius; z++)
			for (int x = -loadRadius; x <= loadRadius; x++)
				_offsets[offsetIndex++] = new ChunkCoordinate(x, z);

		_desired = new HashSet<ChunkCoordinate>(capacity);
		_chunksToRemove.Capacity = capacity;
		_missingList.Capacity = capacity;
		_pendingRemoveList.Capacity = capacity;
		_failedRemoveList.Capacity = capacity;

		_octaveFrequencies = new double[Octaves];
		_octaveAmplitudes = new double[Octaves];
		_octaveSeedHashes = new uint[Octaves];

		double amplitude = 1.0;
		double frequency = _noiseScale;
		double totalAmplitude = 0.0;

		for (int i = 0; i < Octaves; i++)
		{
			_octaveFrequencies[i] = frequency;
			_octaveAmplitudes[i] = amplitude;
			_octaveSeedHashes[i] = unchecked(
				(uint)(_seed + i * 1013) * 2246822519u);
			totalAmplitude += amplitude;
			amplitude *= Persistence;
			frequency *= Lacunarity;
		}

		_invTotalAmplitude = 1.0 / totalAmplitude;
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

		if (deltaTime < 0.0)
			deltaTime = 0.0;
		else if (deltaTime > 0.1)
			deltaTime = 0.1;

		ChunkCoordinate center = GetChunkCoordinate(cameraPosition);
		BuildDesired(center);

		CancelObsoleteBuilds();
		ProcessCompletedBuilds();
		ScheduleMissingChunks(center);

		_chunksToRemove.Clear();

		double progressStep = deltaTime / _animationDuration;

		foreach (Chunk chunk in _chunks.Values)
		{
			bool visible = _desired.Contains(chunk.Coordinate);
			chunk.TargetVisible = visible;

			double progress;
			if (visible)
			{
				progress = chunk.Progress + progressStep;
				if (progress > 1.0)
					progress = 1.0;
			}
			else
			{
				progress = chunk.Progress - progressStep;
				if (progress < 0.0)
					progress = 0.0;
			}

			chunk.Progress = progress;

			double t = progress;
			double eased = t * t * (3.0 - 2.0 * t);
			double scale = MinAnimatedScale + (1.0 - MinAnimatedScale) * eased;

			chunk.Transform.Scale = new DVector3(scale, scale, scale);

			chunk.Transform.Position = new DVector3(
				chunk.Coordinate.X * _chunkWorldSize,
				-AnimationDrop * (1.0 - eased),
				chunk.Coordinate.Z * _chunkWorldSize);

			if (!visible && progress <= 0.0)
				_chunksToRemove.Add(chunk);
		}

		for (int i = 0; i < _chunksToRemove.Count; i++)
		{
			Chunk chunk = _chunksToRemove[i];
			_scene.Remove(chunk.Entity);
			_chunks.Remove(chunk.Coordinate);
		}
	}

	public double GetSurfaceHeight(double worldX, double worldZ)
	{
		ThrowIfDisposed();
		return GetTerrainHeight(worldX / _blockSize, worldZ / _blockSize) * _blockSize;
	}

	private void CancelObsoleteBuilds()
	{
		_pendingRemoveList.Clear();

		foreach (KeyValuePair<ChunkCoordinate, PendingBuild> pair in _pendingBuilds)
		{
			if (_desired.Contains(pair.Key))
				continue;

			PendingBuild pending = pair.Value;

			if (!pending.Cancellation.IsCancellationRequested)
				pending.Cancellation.Cancel();

			if (pending.Task.IsCompleted)
				_pendingRemoveList.Add(pair.Key);
		}

		for (int i = 0; i < _pendingRemoveList.Count; i++)
		{
			ChunkCoordinate coord = _pendingRemoveList[i];
			if (_pendingBuilds.TryGetValue(coord, out PendingBuild? pending))
				RemovePendingBuild(coord, pending);
		}

		_failedRemoveList.Clear();

		foreach (ChunkCoordinate coordinate in _failedChunks)
		{
			if (!_desired.Contains(coordinate))
				_failedRemoveList.Add(coordinate);
		}

		for (int i = 0; i < _failedRemoveList.Count; i++)
			_failedChunks.Remove(_failedRemoveList[i]);
	}

	private void ProcessCompletedBuilds()
	{
		int uploads = 0;
		_pendingRemoveList.Clear();

		foreach (KeyValuePair<ChunkCoordinate, PendingBuild> pair in _pendingBuilds)
		{
			ChunkCoordinate coordinate = pair.Key;
			PendingBuild pending = pair.Value;
			Task<MeshData> task = pending.Task;

			if (!task.IsCompleted)
				continue;

			if (!_desired.Contains(coordinate))
			{
				_pendingRemoveList.Add(coordinate);
				continue;
			}

			if (task.IsCanceled)
			{
				_pendingRemoveList.Add(coordinate);
				continue;
			}

			if (task.IsFaulted)
			{
				Exception exception = task.Exception?.GetBaseException()
					?? new InvalidOperationException("Chunk generation failed.");

				_pendingRemoveList.Add(coordinate);
				_failedChunks.Add(coordinate);
				GenerationFailed?.Invoke(exception);
				continue;
			}

			if (uploads >= _maxUploadsPerFrame)
				continue;

			MeshData data = task.GetAwaiter().GetResult();
			_pendingRemoveList.Add(coordinate);

			if (_disposed ||
				!_desired.Contains(coordinate) ||
				_chunks.ContainsKey(coordinate))
			{
				continue;
			}

			CreateChunk(coordinate, data);
			uploads++;
		}

		for (int i = 0; i < _pendingRemoveList.Count; i++)
		{
			ChunkCoordinate coord = _pendingRemoveList[i];
			if (_pendingBuilds.TryGetValue(coord, out PendingBuild? pending))
				RemovePendingBuild(coord, pending);
		}
	}

	private void ScheduleMissingChunks(ChunkCoordinate center)
	{
		int availableSlots = _maxConcurrentBuilds - _pendingBuilds.Count;
		if (availableSlots <= 0)
			return;

		int countToSchedule = availableSlots < _buildsPerFrame
			? availableSlots
			: _buildsPerFrame;

		_missingList.Clear();

		for (int i = 0; i < _offsets.Length; i++)
		{
			ChunkCoordinate offset = _offsets[i];
			ChunkCoordinate coordinate = new(
				center.X + offset.X,
				center.Z + offset.Z);

			if (_chunks.ContainsKey(coordinate))
				continue;
			if (_pendingBuilds.ContainsKey(coordinate))
				continue;
			if (_failedChunks.Contains(coordinate))
				continue;

			_missingList.Add(coordinate);
		}

		int toStart = _missingList.Count < countToSchedule
			? _missingList.Count
			: countToSchedule;

		for (int i = 0; i < toStart; i++)
		{
			int bestIndex = i;
			long bestDistance = DistanceSquared(_missingList[i], center);

			for (int j = i + 1; j < _missingList.Count; j++)
			{
				long distance = DistanceSquared(_missingList[j], center);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					bestIndex = j;
				}
			}

			if (bestIndex != i)
			{
				ChunkCoordinate tmp = _missingList[i];
				_missingList[i] = _missingList[bestIndex];
				_missingList[bestIndex] = tmp;
			}
		}

		for (int i = 0; i < toStart; i++)
			StartBuild(_missingList[i]);
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

	private void BuildDesired(ChunkCoordinate center)
	{
		_desired.Clear();

		for (int i = 0; i < _offsets.Length; i++)
		{
			ChunkCoordinate offset = _offsets[i];
			_desired.Add(new ChunkCoordinate(
				center.X + offset.X,
				center.Z + offset.Z));
		}
	}

	private ChunkCoordinate GetChunkCoordinate(DVector3 worldPosition)
	{
		int x = (int)Math.Floor(worldPosition.X / _chunkWorldSize + 0.5);
		int z = (int)Math.Floor(worldPosition.Z / _chunkWorldSize + 0.5);

		return new ChunkCoordinate(x, z);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static long DistanceSquared(ChunkCoordinate a, ChunkCoordinate b)
	{
		long dx = (long)a.X - b.X;
		long dz = (long)a.Z - b.Z;

		return dx * dx + dz * dz;
	}

	private void CreateChunk(ChunkCoordinate coordinate, MeshData data)
	{
		string name = $"TerrainChunk_{coordinate.X}_{coordinate.Z}";

		Mesh mesh = new(name, data);

		Entity entity = _scene.Add(name);

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
		int heightSide = resolution + 3;
		int heightCount = heightSide * heightSide;

		const double sampleStep = InvSubdivisions;

		double firstCellX = coordinate.X * _chunkSize - _halfChunkCells;
		double firstCellZ = coordinate.Z * _chunkSize - _halfChunkCells;

		double[] heights = ArrayPool<double>.Shared.Rent(heightCount);

		try
		{
			for (int z = -1; z <= resolution + 1; z++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				int rowOffset = (z + 1) * heightSide;
				double sampleZ = firstCellZ + z * sampleStep;

				for (int x = -1; x <= resolution + 1; x++)
				{
					double sampleX = firstCellX + x * sampleStep;
					heights[rowOffset + x + 1] =
						GetTerrainHeight(sampleX, sampleZ);
				}
			}

			int vertexCount = rowSize * rowSize;
			int indexCount = resolution * resolution * 6;

			DVector3[] positions = new DVector3[vertexCount];
			DVector3[] normals = new DVector3[vertexCount];
			DVector2[] uvs = new DVector2[vertexCount];
			uint[] indices = new uint[indexCount];

			for (int z = 0; z <= resolution; z++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				int outputRow = z * rowSize;
				int heightRow = (z + 1) * heightSide;

				double localZ = (z * sampleStep - _halfChunkCells) * _blockSize;
				double uvZ = z * sampleStep;

				for (int x = 0; x <= resolution; x++)
				{
					int vertexIndex = outputRow + x;
					int heightIndex = heightRow + x + 1;

					double localX =
						(x * sampleStep - _halfChunkCells) * _blockSize;

					double height = heights[heightIndex];

					double slopeX =
						(heights[heightIndex + 1] -
						 heights[heightIndex - 1])
						* SubdivisionsPerCell * 0.5;

					double slopeZ =
						(heights[heightIndex + heightSide] -
						 heights[heightIndex - heightSide])
						* SubdivisionsPerCell * 0.5;

					positions[vertexIndex] = new DVector3(
						localX,
						height * _blockSize,
						localZ);

					normals[vertexIndex] = Normalize(
						new DVector3(-slopeX, 1.0, -slopeZ));

					uvs[vertexIndex] = new DVector2(x * sampleStep, uvZ);
				}
			}

			int indexOffset = 0;

			for (int z = 0; z < resolution; z++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				int row = z * rowSize;
				int nextRow = row + rowSize;

				for (int x = 0; x < resolution; x++)
				{
					uint a = (uint)(row + x);
					uint b = (uint)(nextRow + x);
					uint c = b + 1;
					uint d = a + 1;

					indices[indexOffset++] = a;
					indices[indexOffset++] = c;
					indices[indexOffset++] = b;

					indices[indexOffset++] = a;
					indices[indexOffset++] = d;
					indices[indexOffset++] = c;
				}
			}

			cancellationToken.ThrowIfCancellationRequested();

			return new MeshData(
				positions,
				indices,
				normals,
				uvs);
		}
		finally
		{
			ArrayPool<double>.Shared.Return(heights);
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static DVector3 Normalize(DVector3 value)
	{
		double length = Math.Sqrt(
			value.X * value.X +
			value.Y * value.Y +
			value.Z * value.Z);

		if (length <= 1e-12)
			return new DVector3(0.0, 1.0, 0.0);

		double inv = 1.0 / length;

		return new DVector3(
			value.X * inv,
			value.Y * inv,
			value.Z * inv);
	}

	private double GetTerrainHeight(double cellX, double cellZ)
	{
		double total = 0.0;

		for (int i = 0; i < Octaves; i++)
		{
			total += ValueNoise(
				cellX * _octaveFrequencies[i],
				cellZ * _octaveFrequencies[i],
				_octaveSeedHashes[i]) * _octaveAmplitudes[i];
		}

		return 1.0 + total * _invTotalAmplitude * _heightRange;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double ValueNoise(double x, double z, uint seedHash)
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
			HashNoise(x0, z0, seedHash),
			HashNoise(x1, z0, seedHash),
			tx);

		double b = Lerp(
			HashNoise(x0, z1, seedHash),
			HashNoise(x1, z1, seedHash),
			tx);

		return Lerp(a, b, tz);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double HashNoise(int x, int z, uint seedHash)
	{
		unchecked
		{
			uint value = (uint)x * 374761393u;
			value += (uint)z * 668265263u;
			value += seedHash;
			value = (value ^ (value >> 13)) * 1274126177u;
			value ^= value >> 16;

			return (value & 0x00FFFFFFu) * InvFF;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static double Fade(double t)
	{
		return t * t * (3.0 - 2.0 * t);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
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