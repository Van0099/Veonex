using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Veonex.Core;
using Veonex.Mathematics;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;
using SourceMesh = Veonex.Core.Mesh;

namespace Veonex.Physics;

/// <summary>
/// Public physics entry point for a Veonex scene.
/// Reconciles scene modules, runs fixed simulation steps, and synchronizes transforms.
/// Collision detection and solving are delegated to smaller helper classes and BepuPhysics.
/// </summary>
public sealed class Physics : IDisposable
{
	private const double FixedTimeStep = 1.0 / 60.0;
	private const double MaximumFrameTime = 0.25;
	private const int MaximumSubsteps = 8;
	private const float SpeculativeMargin = 0.01f;

	private readonly BufferPool _bufferPool;
	private readonly TriggerSystem _triggerSystem;
	private readonly CollisionDetector _collisionDetector;
	private readonly PhysicsSolver _solver;

	private readonly Dictionary<Guid, PhysicsObject> _objects = [];

	private readonly Dictionary<CollisionShapeKey, CachedCollisionShape>
	_shapeCache = [];

	private sealed class CachedCollisionShape
	{
		public CollisionShapeData Data { get; }

		public int ReferenceCount { get; set; } = 1;

		public CachedCollisionShape(CollisionShapeData data)
		{
			Data = data;
		}
	}

	private DVector3 _gravity = new(0.0, -9.81, 0.0);

	private double _accumulator;
	private bool _disposed;

	public DVector3 Gravity
	{
		get => _gravity;

		set
		{
			ObjectDisposedException.ThrowIf(_disposed, this);

			ValidateVector3(value, nameof(value));

			_gravity = value;
		}
	}

	/// <summary>
	/// Number of colliders currently registered in the scene.
	/// </summary>
	public int ColliderCount => _objects.Count;

	/// <summary>
	/// Number of colliders which currently have a PhysicsBody.
	/// </summary>
	public int BodyCount
	{
		get
		{
			int count = 0;

			foreach (PhysicsObject obj in _objects.Values)
			{
				if (obj.IsDynamic)
					count++;
			}

			return count;
		}
	}

	public Physics()
	{
		_bufferPool = new BufferPool();

		_triggerSystem = new TriggerSystem();

		_collisionDetector = new CollisionDetector();

		try
		{
			_solver = new PhysicsSolver(
				_bufferPool,
				_collisionDetector,
				_triggerSystem);
		}
		catch
		{
			_bufferPool.Clear();
			throw;
		}
	}

	/// <summary>
	/// Advances the physics simulation. Call once per rendered frame.
	/// Actual simulation work uses a fixed 1/60 second time step.
	/// </summary>
	public void Update(Scene scene, double deltaTime)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		ArgumentNullException.ThrowIfNull(scene);

		if (!double.IsFinite(deltaTime) || deltaTime < 0.0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(deltaTime),
				"Delta time must be finite and non-negative.");
		}

		ReconcileScene(scene);

		_accumulator += Math.Min(
			deltaTime,
			MaximumFrameTime);

		int steps = 0;

		while (_accumulator >= FixedTimeStep &&
			   steps < MaximumSubsteps)
		{
			_solver.Step(
				_objects.Values,
				_gravity,
				FixedTimeStep);

			_accumulator -= FixedTimeStep;
			steps++;
		}

		// Avoid a spiral of death after a long pause or breakpoint.
		if (steps == MaximumSubsteps &&
			_accumulator >= FixedTimeStep)
		{
			_accumulator = 0.0;
		}

		SynchronizeDynamicTransforms();
	}

	public bool IsGrounded(Entity entity)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		ArgumentNullException.ThrowIfNull(entity);

		return _objects.TryGetValue(
				   entity.Id,
				   out PhysicsObject obj) &&
			   obj.IsDynamic &&
			   _collisionDetector.IsGrounded(
				   obj.BodyHandle);
	}

	public bool TryGetLinearVelocity(
		Entity entity,
		out DVector3 velocity)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		ArgumentNullException.ThrowIfNull(entity);

		if (!TryGetDynamicObject(entity, out PhysicsObject obj))
		{
			velocity = DVector3.Zero;
			return false;
		}

		NVector3 value =
			_solver.Simulation.Bodies[obj.BodyHandle]
				.Velocity.Linear;

		velocity = new DVector3(
			value.X,
			value.Y,
			value.Z);

		return true;
	}

	public bool TrySetLinearVelocity(
		Entity entity,
		DVector3 velocity)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		ArgumentNullException.ThrowIfNull(entity);

		ValidateVector3(velocity, nameof(velocity));

		if (!TryGetDynamicObject(entity, out PhysicsObject obj))
			return false;

		BodyReference body =
			_solver.Simulation.Bodies[obj.BodyHandle];

		body.Awake = true;

		body.Velocity.Linear =
			ToVector3(velocity);

		return true;
	}

	public bool TryGetAngularVelocity(
		Entity entity,
		out DVector3 velocity)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		ArgumentNullException.ThrowIfNull(entity);

		if (!TryGetDynamicObject(entity, out PhysicsObject obj))
		{
			velocity = DVector3.Zero;
			return false;
		}

		NVector3 value =
			_solver.Simulation.Bodies[obj.BodyHandle]
				.Velocity.Angular;

		velocity = new DVector3(
			value.X,
			value.Y,
			value.Z);

		return true;
	}

	public bool TrySetAngularVelocity(
		Entity entity,
		DVector3 velocity)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		ArgumentNullException.ThrowIfNull(entity);

		ValidateVector3(velocity, nameof(velocity));

		if (!TryGetDynamicObject(entity, out PhysicsObject obj))
			return false;

		BodyReference body =
			_solver.Simulation.Bodies[obj.BodyHandle];

		body.Awake = true;

		body.Velocity.Angular =
			ToVector3(velocity);

		return true;
	}

	private bool TryGetDynamicObject(
		Entity entity,
		out PhysicsObject obj)
	{
		if (_objects.TryGetValue(
				entity.Id,
				out PhysicsObject? found) &&
			found.IsDynamic)
		{
			obj = found;
			return true;
		}

		obj = null!;
		return false;
	}

	private void ReconcileScene(Scene scene)
	{
		HashSet<Guid> seen = [];

		foreach (Entity entity in scene.Entities)
		{
			if (!entity.Has<Collider>())
				continue;

			if (!entity.Has<Transform>())
			{
				throw new InvalidOperationException(
					$"Entity \"{entity.Name}\" has a Collider " +
					"but does not have a Transform.");
			}

			if (!entity.Has<MeshRenderer>())
			{
				throw new InvalidOperationException(
					$"Entity \"{entity.Name}\" has a Collider " +
					"but does not have a MeshRenderer.");
			}

			Transform transform =
				entity.Get<Transform>();

			Collider collider =
				entity.Get<Collider>();

			MeshRenderer meshRenderer =
				entity.Get<MeshRenderer>();

			SourceMesh? mesh =
				meshRenderer.Mesh;

			if (mesh is null)
			{
				throw new InvalidOperationException(
					$"Entity \"{entity.Name}\" has a Collider, " +
					"but MeshRenderer.Mesh is not assigned.");
			}

			PhysicsBody? bodyModule =
				entity.Has<PhysicsBody>()
					? entity.Get<PhysicsBody>()
					: null;

			seen.Add(entity.Id);

			if (_objects.TryGetValue(
					entity.Id,
					out PhysicsObject? existing))
			{
				if (existing.Matches(
						transform,
						collider,
						bodyModule,
						mesh))
				{
					SynchronizeExternalTransform(existing);
					continue;
				}

				DVector3 oldLinearVelocity =
					DVector3.Zero;

				DVector3 oldAngularVelocity =
					DVector3.Zero;

				bool preserveVelocity =
					existing.IsDynamic &&
					bodyModule is not null;

				if (preserveVelocity)
				{
					TryGetLinearVelocity(
						entity,
						out oldLinearVelocity);

					TryGetAngularVelocity(
						entity,
						out oldAngularVelocity);
				}

				RemoveObject(existing);

				_objects.Remove(entity.Id);

				PhysicsObject replacement = CreateObject(
					entity,
					transform,
					collider,
					bodyModule,
					mesh);

				_objects.Add(
					entity.Id,
					replacement);

				if (preserveVelocity)
				{
					BodyReference newBody =
						_solver.Simulation.Bodies[
							replacement.BodyHandle];

					newBody.Velocity.Linear =
						ToVector3(oldLinearVelocity);

					newBody.Velocity.Angular =
						ToVector3(oldAngularVelocity);
				}

				continue;
			}

			PhysicsObject created = CreateObject(
				entity,
				transform,
				collider,
				bodyModule,
				mesh);

			_objects.Add(
				entity.Id,
				created);
		}

		foreach (KeyValuePair<Guid, PhysicsObject> pair
				 in _objects.ToArray())
		{
			if (seen.Contains(pair.Key))
				continue;

			RemoveObject(pair.Value);
			_objects.Remove(pair.Key);
		}
	}

	private CollisionShapeData AcquireCollisionShape(
	SourceMesh mesh,
	Transform transform,
	Collider collider,
	bool dynamic,
	double mass,
	out CollisionShapeKey key)
	{
		DVector3 effectiveScale = new(
			transform.Scale.X * collider.SizePercent.X / 100.0,
			transform.Scale.Y * collider.SizePercent.Y / 100.0,
			transform.Scale.Z * collider.SizePercent.Z / 100.0);

		DVector3 scaledOffset = new(
			transform.Scale.X * collider.Offset.X,
			transform.Scale.Y * collider.Offset.Y,
			transform.Scale.Z * collider.Offset.Z);

		// Dynamic triangle meshes have mass-dependent inertia.
		// CollisionMesh also recenters dynamic mesh geometry while
		// computing inertia, so dynamic/static forms are kept separate.
		// CollisionQuality is part of the cache key only for complex mesh geometry.
		// SimpleGeometry uses a bounds box, so quality has no effect there.
		double collisionQuality = collider.SimpleGeometry
			? 1.0
			: collider.CollisionQuality;

		key = new CollisionShapeKey(
			mesh.Id,
			effectiveScale,
			scaledOffset,
			collider.SimpleGeometry,
			collisionQuality,
			dynamic,
			dynamic ? mass : 0.0);

		if (_shapeCache.TryGetValue(
				key,
				out CachedCollisionShape? cached))
		{
			cached.ReferenceCount =
				checked(cached.ReferenceCount + 1);

			return cached.Data;
		}

		CollisionShapeData data = CollisionMesh.Create(
			_solver.Simulation,
			_bufferPool,
			mesh,
			transform,
			collider,
			dynamic,
			mass);

		try
		{
			_shapeCache.Add(
				key,
				new CachedCollisionShape(data));
		}
		catch
		{
			_solver.Simulation.Shapes.RemoveAndDispose(
				data.ShapeIndex,
				_bufferPool);

			throw;
		}

		return data;
	}

	private void ReleaseCollisionShape(CollisionShapeKey key)
	{
		if (!_shapeCache.TryGetValue(
				key,
				out CachedCollisionShape? cached))
		{
			throw new InvalidOperationException(
				"Collision shape was not present in the shape cache.");
		}

		if (cached.ReferenceCount > 1)
		{
			cached.ReferenceCount--;
			return;
		}

		_shapeCache.Remove(key);

		_solver.Simulation.Shapes.RemoveAndDispose(
			cached.Data.ShapeIndex,
			_bufferPool);
	}

	private PhysicsObject CreateObject(
		Entity entity,
		Transform transform,
		Collider collider,
		PhysicsBody? bodyModule,
		SourceMesh mesh)
	{
		bool dynamic = bodyModule is not null;
		double mass = bodyModule?.Mass ?? 1.0;

		CollisionShapeData shape = AcquireCollisionShape(
			mesh,
			transform,
			collider,
			dynamic,
			mass,
			out CollisionShapeKey shapeKey);

		PhysicsObject obj = new(
				entity,
				transform,
				collider,
				bodyModule,
				mesh,
				shape,
				shapeKey);

		bool bodyCreated = false;
		bool staticCreated = false;

		try
		{
			NQuaternion orientation =
				ToQuaternion(transform.Quaternion);

			NVector3 position =
				GetBodyPosition(
					transform,
					obj.LocalCenterOffset);

			if (bodyModule is not null)
			{
				BodyInertia bodyInertia =
					shape.Inertia;

				if (bodyModule.FreezeRotation)
				{
					bodyInertia.InverseInertiaTensor = default;
				}

				obj.BodyHandle =
					_solver.Simulation.Bodies.Add(
						BodyDescription.CreateDynamic(
							(position, orientation),
							bodyInertia,
							shape.ShapeIndex,
							SpeculativeMargin));

				bodyCreated = true;

				_collisionDetector.RegisterBody(
					obj.BodyHandle,
					collider);
			}
			else
			{
				obj.StaticHandle =
					_solver.Simulation.Statics.Add(
						new StaticDescription(
							position,
							orientation,
							shape.ShapeIndex));

				staticCreated = true;

				_collisionDetector.RegisterStatic(
					obj.StaticHandle,
					collider);
			}

			return obj;
		}
		catch
		{
			if (bodyCreated)
			{
				_collisionDetector.UnregisterBody(
					obj.BodyHandle);

				_solver.Simulation.Bodies.Remove(
					obj.BodyHandle);
			}

			if (staticCreated)
			{
				_collisionDetector.UnregisterStatic(
					obj.StaticHandle);

				_solver.Simulation.Statics.Remove(
					obj.StaticHandle);
			}

			ReleaseCollisionShape(shapeKey);

			throw;
		}
	}

	private void SynchronizeExternalTransform(
		PhysicsObject obj)
	{
		DVector3 entityPosition =
			obj.Transform.Position;

		DQuaternion entityRotation =
			obj.Transform.Quaternion;

		if (entityPosition == obj.LastTransformPosition &&
			entityRotation == obj.LastTransformRotation)
		{
			return;
		}

		NVector3 bodyPosition =
			GetBodyPosition(
				obj.Transform,
				obj.LocalCenterOffset);

		NQuaternion orientation =
			ToQuaternion(entityRotation);

		if (obj.IsDynamic)
		{
			BodyReference body =
				_solver.Simulation.Bodies[obj.BodyHandle];

			body.Pose.Position = bodyPosition;
			body.Pose.Orientation = orientation;
			body.Awake = true;
		}
		else
		{
			_collisionDetector.UnregisterStatic(
				obj.StaticHandle);

			_solver.Simulation.Statics.Remove(
				obj.StaticHandle);

			obj.StaticHandle =
				_solver.Simulation.Statics.Add(
					new StaticDescription(
						bodyPosition,
						orientation,
						obj.ShapeIndex));

			_collisionDetector.RegisterStatic(
				obj.StaticHandle,
				obj.Collider);
		}

		obj.LastTransformPosition =
			entityPosition;

		obj.LastTransformRotation =
			entityRotation;
	}

	private void SynchronizeDynamicTransforms()
	{
		foreach (PhysicsObject obj in _objects.Values)
		{
			if (!obj.IsDynamic)
				continue;

			BodyReference body =
				_solver.Simulation.Bodies[obj.BodyHandle];

			NVector3 bodyPosition =
				body.Pose.Position;

			NQuaternion orientation =
				body.Pose.Orientation;

			DQuaternion rotation = new(
				orientation.X,
				orientation.Y,
				orientation.Z,
				orientation.W);

			rotation = rotation.Normalized();

			DVector3 worldCenterOffset =
				rotation * obj.LocalCenterOffset;

			DVector3 entityPosition = new(
				bodyPosition.X - worldCenterOffset.X,
				bodyPosition.Y - worldCenterOffset.Y,
				bodyPosition.Z - worldCenterOffset.Z);

			obj.Transform.Position =
				entityPosition;

			obj.Transform.Quaternion =
				rotation;

			obj.LastTransformPosition =
				entityPosition;

			obj.LastTransformRotation =
				rotation;
		}
	}

	private void RemoveObject(PhysicsObject obj)
	{
		_triggerSystem.RemoveEntity(
			obj.Entity.Id);

		if (obj.IsDynamic)
		{
			_collisionDetector.UnregisterBody(
				obj.BodyHandle);

			_solver.Simulation.Bodies.Remove(
				obj.BodyHandle);
		}
		else
		{
			_collisionDetector.UnregisterStatic(
				obj.StaticHandle);

			_solver.Simulation.Statics.Remove(
				obj.StaticHandle);
		}

		ReleaseCollisionShape(obj.ShapeKey);
	}

	private static NVector3 GetBodyPosition(
		Transform transform,
		DVector3 localCenterOffset)
	{
		DVector3 worldOffset =
			transform.Quaternion * localCenterOffset;

		DVector3 bodyPosition =
			transform.Position + worldOffset;

		return ToVector3(bodyPosition);
	}

	private static NVector3 ToVector3(
		DVector3 value)
	{
		ValidateVector3(
			value,
			nameof(value));

		return new NVector3(
			(float)value.X,
			(float)value.Y,
			(float)value.Z);
	}

	private static NQuaternion ToQuaternion(
		DQuaternion value)
	{
		NQuaternion result = new(
			(float)value.X,
			(float)value.Y,
			(float)value.Z,
			(float)value.W);

		if (!float.IsFinite(result.X) ||
			!float.IsFinite(result.Y) ||
			!float.IsFinite(result.Z) ||
			!float.IsFinite(result.W))
		{
			throw new ArgumentOutOfRangeException(
				nameof(value),
				"Rotation must be finite and representable " +
				"as a float quaternion.");
		}

		return NQuaternion.Normalize(result);
	}

	private static void ValidateVector3(
		DVector3 value,
		string parameterName)
	{
		float x = (float)value.X;
		float y = (float)value.Y;
		float z = (float)value.Z;

		if (!double.IsFinite(value.X) ||
			!double.IsFinite(value.Y) ||
			!double.IsFinite(value.Z) ||
			!float.IsFinite(x) ||
			!float.IsFinite(y) ||
			!float.IsFinite(z))
		{
			throw new ArgumentOutOfRangeException(
				parameterName,
				"Vector values must be finite and representable as floats.");
		}
	}

	public void Dispose()
	{
		if (_disposed)
			return;

		_disposed = true;

		foreach (PhysicsObject obj in _objects.Values.ToArray())
			RemoveObject(obj);

		_objects.Clear();

		_solver.Dispose();

		_bufferPool.Clear();
	}
}