
using System;
using JoltPhysicsSharp;
using Veonex.Core;
using Veonex.Mathematics;

using NVector3 = System.Numerics.Vector3;

namespace Veonex.Physics;

public sealed class Physics : IDisposable
{
	public const double FixedTimeStep = 1.0 / 60.0;

	private const int MaximumSubSteps = 8;
	private const double MaximumFrameDeltaTime = 0.25;

	private const uint NonMovingLayer = 0;
	private const uint MovingLayer = 1;

	private const byte NonMovingBroadPhaseLayer = 0;
	private const byte MovingBroadPhaseLayer = 1;

	private static readonly object FoundationLock = new();
	private static int _foundationUsers;

	private PhysicsSystem? _system;
	private JobSystemThreadPool? _jobSystem;

	private double _accumulator;
	private bool _foundationAcquired;
	private bool _disposed;

	public DVector3 Gravity
	{
		get
		{
			ThrowIfDisposed();

			NVector3 gravity = _system!.Gravity;

			return new DVector3(
				gravity.X,
				gravity.Y,
				gravity.Z);
		}
		set
		{
			ThrowIfDisposed();

			_system!.Gravity = ToVector3(value);
		}
	}

	public uint BodyCount
	{
		get
		{
			ThrowIfDisposed();
			return _system!.BodiesCount;
		}
	}

	public Physics()
	{
		AcquireFoundation();
		_foundationAcquired = true;

		try
		{
			Initialize();
		}
		catch
		{
			DisposeNativeObjects();
			ReleaseFoundation();

			_foundationAcquired = false;

			throw;
		}
	}

	private void Initialize()
	{
		ObjectLayerPairFilterTable? objectLayerFilter = null;
		BroadPhaseLayerInterfaceTable? broadPhaseInterface = null;
		ObjectVsBroadPhaseLayerFilterTable? objectVsBroadPhaseFilter = null;

		try
		{
			objectLayerFilter = new ObjectLayerPairFilterTable(2);

			objectLayerFilter.EnableCollision(
				NonMovingLayer,
				MovingLayer);

			objectLayerFilter.EnableCollision(
				MovingLayer,
				MovingLayer);

			broadPhaseInterface =
				new BroadPhaseLayerInterfaceTable(2, 2);

			broadPhaseInterface.MapObjectToBroadPhaseLayer(
				NonMovingLayer,
				NonMovingBroadPhaseLayer);

			broadPhaseInterface.MapObjectToBroadPhaseLayer(
				MovingLayer,
				MovingBroadPhaseLayer);

			objectVsBroadPhaseFilter =
				new ObjectVsBroadPhaseLayerFilterTable(
					broadPhaseInterface,
					2,
					objectLayerFilter,
					2);

			var settings = new PhysicsSystemSettings
			{
				MaxBodies = 4096,
				NumBodyMutexes = 0,
				MaxBodyPairs = 65536,
				MaxContactConstraints = 10240,

				ObjectLayerPairFilter = objectLayerFilter,
				BroadPhaseLayerInterface = broadPhaseInterface,
				ObjectVsBroadPhaseLayerFilter = objectVsBroadPhaseFilter
			};

			_system = new PhysicsSystem(settings);

			// PhysicsSystem takes ownership of the filters.
			objectLayerFilter = null;
			broadPhaseInterface = null;
			objectVsBroadPhaseFilter = null;

			_jobSystem = new JobSystemThreadPool();

			_system.Gravity = new NVector3(
				0.0f,
				-9.81f,
				0.0f);
		}
		finally
		{
			objectVsBroadPhaseFilter?.Dispose();
			broadPhaseInterface?.Dispose();
			objectLayerFilter?.Dispose();
		}
	}

	public void Update(double deltaTime)
	{
		ThrowIfDisposed();

		if (!double.IsFinite(deltaTime) || deltaTime < 0.0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(deltaTime),
				"Delta time must be finite and non-negative.");
		}

		// Nothing needs simulating until physical bodies exist.
		if (_system!.BodiesCount == 0)
		{
			_accumulator = 0.0;
			return;
		}

		double acceptedDelta = Math.Min(
			deltaTime,
			MaximumFrameDeltaTime);

		double maximumAccumulatedTime =
			FixedTimeStep * MaximumSubSteps;

		_accumulator = Math.Min(
			_accumulator + acceptedDelta,
			maximumAccumulatedTime);

		int subSteps = 0;

		while (_accumulator >= FixedTimeStep &&
			   subSteps < MaximumSubSteps)
		{
			PhysicsUpdateError error = _system.Update(
				(float)FixedTimeStep,
				1,
				_jobSystem!);

			if (error != PhysicsUpdateError.None)
			{
				throw new InvalidOperationException(
					$"Jolt physics update failed: {error}.");
			}

			_accumulator -= FixedTimeStep;
			subSteps++;
		}
	}

	private static NVector3 ToVector3(DVector3 value)
	{
		if (!double.IsFinite(value.X) ||
			!double.IsFinite(value.Y) ||
			!double.IsFinite(value.Z) ||
			Math.Abs(value.X) > float.MaxValue ||
			Math.Abs(value.Y) > float.MaxValue ||
			Math.Abs(value.Z) > float.MaxValue)
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				"Gravity components must be finite and representable as floats.");
		}

		return new NVector3(
			(float)value.X,
			(float)value.Y,
			(float)value.Z);
	}

	private static void AcquireFoundation()
	{
		lock (FoundationLock)
		{
			if (_foundationUsers == 0 && !Foundation.Init())
			{
				throw new InvalidOperationException(
					"Failed to initialize Jolt Physics.");
			}

			_foundationUsers++;
		}
	}

	private static void ReleaseFoundation()
	{
		lock (FoundationLock)
		{
			if (_foundationUsers <= 0)
				return;

			_foundationUsers--;

			if (_foundationUsers == 0)
				Foundation.Shutdown();
		}
	}

	private void DisposeNativeObjects()
	{
		_jobSystem?.Dispose();
		_jobSystem = null;

		_system?.Dispose();
		_system = null;
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

		DisposeNativeObjects();

		if (_foundationAcquired)
		{
			ReleaseFoundation();
			_foundationAcquired = false;
		}

		GC.SuppressFinalize(this);
	}
}
