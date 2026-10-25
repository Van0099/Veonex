using BepuPhysics;
using BepuPhysics.Collidables;
using Veonex.Core;
using Veonex.Mathematics;
using SourceMesh = Veonex.Core.Mesh;

namespace Veonex.Physics;

/// <summary>
/// Identifies collision geometry that can be shared
/// between compatible physics objects.
///
/// The key includes all settings that affect the generated
/// shape, center offset, or inertia in CollisionMesh.Create.
/// </summary>
internal readonly record struct CollisionShapeKey(
	Guid MeshId,
	DVector3 EffectiveScale,
	DVector3 ScaledOffset,
	bool SimpleGeometry,
	bool Dynamic,
	double Mass);

/// <summary>
/// Runtime data connecting one Veonex entity to one Bepu collidable.
/// Multiple objects may reference the same collision shape.
/// </summary>
internal sealed class PhysicsObject
{
	public Entity Entity { get; }
	public Transform Transform { get; }
	public Collider Collider { get; }
	public PhysicsBody? BodyModule { get; }

	public Guid MeshId { get; }

	public TypedIndex ShapeIndex { get; }
	public DVector3 LocalCenterOffset { get; }

	public CollisionShapeKey ShapeKey { get; }

	public BodyHandle BodyHandle { get; set; }
	public StaticHandle StaticHandle { get; set; }

	public bool IsDynamic => BodyModule is not null;

	public DVector3 LastTransformPosition { get; set; }
	public DQuaternion LastTransformRotation { get; set; }

	private readonly bool _simpleGeometry;
	private readonly DVector3 _sizePercent;
	private readonly DVector3 _offset;
	private readonly DVector3 _transformScale;
	private readonly double _mass;

	public PhysicsObject(
		Entity entity,
		Transform transform,
		Collider collider,
		PhysicsBody? bodyModule,
		SourceMesh mesh,
		CollisionShapeData shapeData,
		CollisionShapeKey shapeKey)
	{
		Entity = entity;
		Transform = transform;
		Collider = collider;
		BodyModule = bodyModule;

		MeshId = mesh.Id;

		ShapeIndex = shapeData.ShapeIndex;
		LocalCenterOffset = shapeData.LocalCenterOffset;
		ShapeKey = shapeKey;

		_simpleGeometry = collider.SimpleGeometry;
		_sizePercent = collider.SizePercent;
		_offset = collider.Offset;
		_transformScale = transform.Scale;
		_mass = bodyModule?.Mass ?? 0.0;

		LastTransformPosition = transform.Position;
		LastTransformRotation = transform.Quaternion;
	}

	public bool Matches(
		Transform transform,
		Collider collider,
		PhysicsBody? bodyModule,
		SourceMesh mesh)
	{
		return ReferenceEquals(Transform, transform) &&
			   ReferenceEquals(Collider, collider) &&
			   ReferenceEquals(BodyModule, bodyModule) &&
			   MeshId == mesh.Id &&
			   _simpleGeometry == collider.SimpleGeometry &&
			   _sizePercent == collider.SizePercent &&
			   _offset == collider.Offset &&
			   _transformScale == transform.Scale &&
			   _mass == (bodyModule?.Mass ?? 0.0);
	}
}