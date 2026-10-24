using SDL;
using Veonex.Core;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Render;

namespace Veonex.Game;

public sealed class CameraController
{
	private const byte LeftMouseButton = 1;
	private const double PitchLimit = 85.0;

	private const double RotationSmoothness = 30.0;
	private const double MovementAcceleration = 14.0;
	private const double MovementDeceleration = 18.0;
	private const double MaxDeltaTime = 0.1;

	private readonly Transform _transform;
	private readonly InputManager _input;
	private readonly RenderBackend _renderer;

	private readonly double _moveSpeed;
	private readonly double _mouseSensitivity;

	private double _yaw;
	private double _pitch;

	private double _targetYaw;
	private double _targetPitch;

	private DVector3 _movementVelocity = DVector3.Zero;

	private bool _ignoreMouseDelta;

	public CameraController(
		Camera camera,
		InputManager input,
		RenderBackend renderer,
		DVector3 target,
		double moveSpeed = 5.0,
		double mouseSensitivity = 0.15)
	{
		_transform = camera.Entity.Get<Transform>();
		_input = input;
		_renderer = renderer;
		_moveSpeed = moveSpeed;
		_mouseSensitivity = mouseSensitivity;

		InitializeRotation(target);

		_renderer.SetMouseCapture(true);
		_ignoreMouseDelta = true;
	}

	public void Update(double deltaTime)
	{
		deltaTime = Math.Clamp(deltaTime, 0.0, MaxDeltaTime);

		UpdateMouseCapture();
		UpdateRotation(deltaTime);
		UpdateMovement(deltaTime);
	}

	private void InitializeRotation(DVector3 target)
	{
		DVector3 direction =
			(target - _transform.Position).Normalized();

		double horizontalLength = Math.Sqrt(
			direction.X * direction.X +
			direction.Z * direction.Z);

		_yaw =
			Math.Atan2(direction.X, direction.Z) *
			180.0 / Math.PI;

		_pitch =
			Math.Atan2(-direction.Y, horizontalLength) *
			180.0 / Math.PI;

		_pitch = Math.Clamp(
			_pitch,
			-PitchLimit,
			PitchLimit);

		_targetYaw = _yaw;
		_targetPitch = _pitch;

		ApplyRotation();
	}

	private void UpdateMouseCapture()
	{
		if (_renderer.MouseCaptured)
		{
			if (_input.IsKeyPressed(
				SDL_Scancode.SDL_SCANCODE_ESCAPE))
			{
				_renderer.SetMouseCapture(false);
			}

			return;
		}

		if (_input.IsMouseButtonPressed(LeftMouseButton))
		{
			_renderer.SetMouseCapture(true);
			_ignoreMouseDelta = true;
		}
	}

	private void UpdateRotation(double deltaTime)
	{
		if (_renderer.MouseCaptured)
		{
			if (_ignoreMouseDelta)
			{
				_ignoreMouseDelta = false;
			}
			else
			{
				float dx = _input.MouseDeltaX;
				float dy = _input.MouseDeltaY;

				_targetYaw -= dx * _mouseSensitivity;
				_targetPitch += dy * _mouseSensitivity;

				_targetPitch = Math.Clamp(
					_targetPitch,
					-PitchLimit,
					PitchLimit);
			}
		}

		double smoothFactor =
			1.0 - Math.Exp(-RotationSmoothness * deltaTime);

		_yaw += (_targetYaw - _yaw) * smoothFactor;
		_pitch += (_targetPitch - _pitch) * smoothFactor;

		ApplyRotation();
	}

	private void ApplyRotation()
	{
		_transform.Rotation = new DVector3(
			_pitch,
			_yaw,
			0.0);
	}

	private DVector3 GetForward()
	{
		double yawRadians = _yaw * Math.PI / 180.0;
		double pitchRadians = _pitch * Math.PI / 180.0;

		double cosPitch = Math.Cos(pitchRadians);

		return new DVector3(
			Math.Sin(yawRadians) * cosPitch,
			-Math.Sin(pitchRadians),
			Math.Cos(yawRadians) * cosPitch);
	}

	private DVector3 GetBackward()
	{
		double yawRadians = _yaw * Math.PI / 180.0;
		double pitchRadians = _pitch * Math.PI / 180.0;

		double cosPitch = Math.Cos(pitchRadians);

		return new DVector3(
			-Math.Sin(yawRadians) * cosPitch,
			Math.Sin(pitchRadians),
			-Math.Cos(yawRadians) * cosPitch);
	}

	private DVector3 GetLeft()
	{
		double yawRadians = (_yaw - 90.0) * Math.PI / 180.0;

		return new DVector3(
			Math.Sin(yawRadians),
			0.0,
			Math.Cos(yawRadians));
	}

	private DVector3 GetRight()
	{
		double yawRadians = (_yaw + 90.0) * Math.PI / 180.0;

		return new DVector3(
			Math.Sin(yawRadians),
			0.0,
			Math.Cos(yawRadians));
	}

	private void UpdateMovement(double deltaTime)
	{
		DVector3 desiredVelocity = DVector3.Zero;

		if (_renderer.MouseCaptured)
		{
			DVector3 movement = DVector3.Zero;

			if (_input.IsKeyDown(
				SDL_Scancode.SDL_SCANCODE_W))
			{
				movement += GetForward();
			}

			if (_input.IsKeyDown(
				SDL_Scancode.SDL_SCANCODE_S))
			{
				movement += GetBackward();
			}

			if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_A))
				movement += GetRight();

			if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_D))
				movement += GetLeft();

			bool hasMovement =
				movement.X != 0.0 ||
				movement.Y != 0.0 ||
				movement.Z != 0.0;

			if (hasMovement)
			{
				movement = movement.Normalized();
				desiredVelocity = movement * _moveSpeed;
			}
		}

		bool accelerating =
			desiredVelocity.X != 0.0 ||
			desiredVelocity.Y != 0.0 ||
			desiredVelocity.Z != 0.0;

		double rate = accelerating
			? MovementAcceleration
			: MovementDeceleration;

		double smoothFactor =
			1.0 - Math.Exp(-rate * deltaTime);

		_movementVelocity +=
			(desiredVelocity - _movementVelocity) *
			smoothFactor;

		_transform.Position +=
			_movementVelocity * deltaTime;
	}
}