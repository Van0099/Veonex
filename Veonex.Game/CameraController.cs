
using System;
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

	private int _mouseLogCounter;
	private int _movementLogCounter;

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

		Console.WriteLine("[Camera] Initializing controller");
		Console.WriteLine($"[Camera] Position: {_transform.Position}");
		Console.WriteLine($"[Camera] Target: {target}");
		Console.WriteLine($"[Camera] Mouse sensitivity: {_mouseSensitivity}");
		Console.WriteLine($"[Camera] Move speed: {_moveSpeed}");

		InitializeRotation(target);

		_renderer.SetMouseCapture(true);
		_ignoreMouseDelta = true;

		Console.WriteLine(
			$"[Camera] Mouse capture requested. Actual state: {_renderer.MouseCaptured}");
		LogRotation("Initial rotation");
	}

	public void Update(double deltaTime)
	{
		UpdateMouseCapture();
		LogMouseInputWhenUncaptured();
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

		Console.WriteLine($"[Camera] Initial direction: {direction}");
		Console.WriteLine($"[Camera] Horizontal length: {horizontalLength:F4}");

		ApplyRotation();
	}

	private void UpdateMouseCapture()
	{
		if (_renderer.MouseCaptured)
		{
			if (_input.IsKeyPressed(SDL_Scancode.SDL_SCANCODE_ESCAPE))
			{
				Console.WriteLine("[Camera] Escape pressed. Releasing mouse.");
				_renderer.SetMouseCapture(false);
				Console.WriteLine(
					$"[Camera] Mouse captured: {_renderer.MouseCaptured}");
			}

			return;
		}

		if (_input.IsMouseButtonPressed(LeftMouseButton))
		{
			Console.WriteLine("[Camera] Left mouse button pressed. Capturing mouse.");

			_renderer.SetMouseCapture(true);
			_ignoreMouseDelta = true;

			Console.WriteLine(
				$"[Camera] Mouse captured: {_renderer.MouseCaptured}");
		}
	}

	private void LogMouseInputWhenUncaptured()
	{
		if (_renderer.MouseCaptured)
			return;

		if (_input.MouseDeltaX == 0.0f && _input.MouseDeltaY == 0.0f)
			return;

		_mouseLogCounter++;

		if (_mouseLogCounter % 10 != 1)
			return;

		Console.WriteLine(
			$"[Camera] Mouse moved while NOT captured: " +
			$"dx={_input.MouseDeltaX:F2}, dy={_input.MouseDeltaY:F2}");
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

			Console.WriteLine(
				$"[Camera] Ignoring first mouse delta after capture: " +
				$"dx={dx:F2}, dy={dy:F2}");

			return;
		}

		double oldYaw = _yaw;
		double oldPitch = _pitch;

		_yaw -= dx * _mouseSensitivity;
		_pitch += dy * _mouseSensitivity;

		_pitch = Math.Clamp(_pitch, -PitchLimit, PitchLimit);

		ApplyRotation();

		_mouseLogCounter++;

		if (_mouseLogCounter % 10 == 1)
		{
			Console.WriteLine(
				$"[Camera] Mouse delta: X={dx:F2}, Y={dy:F2}");

			Console.WriteLine(
				$"[Camera] Yaw: {oldYaw:F2} -> {_yaw:F2}");

			Console.WriteLine(
				$"[Camera] Pitch: {oldPitch:F2} -> {_pitch:F2}");

			Console.WriteLine(
				$"[Camera] Transform rotation: {_transform.Rotation}");

			Console.WriteLine(
				$"[Camera] Calculated forward: {GetForward()}");
		}
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

		bool forwardPressed =
			_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_W);

		if (!forwardPressed)
			return;

		DVector3 forward = GetForward();
		DVector3 movement = forward * (_moveSpeed * deltaTime);

		_transform.Position += movement;

		_movementLogCounter++;

		if (_movementLogCounter % 30 == 1)
		{
			Console.WriteLine(
				$"[Camera] W movement. Forward: {forward}");

			Console.WriteLine(
				$"[Camera] Movement delta: {movement}");

			Console.WriteLine(
				$"[Camera] New position: {_transform.Position}");
		}
	}

	private void LogRotation(string message)
	{
		Console.WriteLine($"[Camera] {message}");
		Console.WriteLine($"[Camera] Yaw: {_yaw:F2}, Pitch: {_pitch:F2}");
		Console.WriteLine($"[Camera] Forward: {GetForward()}");
		Console.WriteLine($"[Camera] Transform rotation: {_transform.Rotation}");
	}
}