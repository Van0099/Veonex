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

	private readonly Transform _transform;
	private readonly InputManager _input;
	private readonly RenderBackend _renderer;

	private readonly double _moveSpeed;
	private readonly double _mouseSensitivity;

	private double _yaw;
	private double _pitch;
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
		UpdateMouseCapture();
		UpdateRotation();
		UpdateMovement(deltaTime);
	}

	private void InitializeRotation(DVector3 target)
	{
		DVector3 direction = (target - _transform.Position).Normalized();

		double horizontalLength = Math.Sqrt(
			direction.X * direction.X +
			direction.Z * direction.Z);

		_yaw = Math.Atan2(direction.X, direction.Z) * 180.0 / Math.PI;

		_pitch = Math.Atan2(
			-direction.Y,
			horizontalLength) * 180.0 / Math.PI;

		_pitch = Math.Clamp(_pitch, -PitchLimit, PitchLimit);

		ApplyRotation();
	}

	private void UpdateMouseCapture()
	{
		if (_renderer.MouseCaptured)
		{
			if (_input.IsKeyPressed(SDL_Scancode.SDL_SCANCODE_ESCAPE))
				_renderer.SetMouseCapture(false);

			return;
		}

		if (_input.IsMouseButtonPressed(LeftMouseButton))
		{
			_renderer.SetMouseCapture(true);
			_ignoreMouseDelta = true;
		}
	}

	private void UpdateRotation()
	{
		if (!_renderer.MouseCaptured)
			return;

		float dx = _input.MouseDeltaX;
		float dy = _input.MouseDeltaY;

		if (dx == 0.0f && dy == 0.0f)
			return;

		if (_ignoreMouseDelta)
		{
			_ignoreMouseDelta = false;
			return;
		}

		_yaw -= dx * _mouseSensitivity;
		_pitch += dy * _mouseSensitivity;

		_pitch = Math.Clamp(_pitch, -PitchLimit, PitchLimit);

		ApplyRotation();
	}

	private void ApplyRotation()
	{
		_transform.Rotation = new DVector3(_pitch, _yaw, 0.0);
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

	private void UpdateMovement(double deltaTime)
	{
		if (!_renderer.MouseCaptured)
			return;

		if (!_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_W))
			return;

		DVector3 forward = GetForward();
		_transform.Position += forward * (_moveSpeed * deltaTime);
	}
}