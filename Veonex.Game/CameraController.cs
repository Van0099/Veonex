using System.Numerics;
using SDL;
using Veonex.Core;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Render;

namespace Veonex.Game;

public sealed class CameraController
{
	private const byte LeftMouseButton = 1;

	private readonly Camera _camera;
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
		_camera = camera;
		_transform =
			camera.Entity.Get<Transform>();

		_input = input;
		_renderer = renderer;

		_moveSpeed = moveSpeed;
		_mouseSensitivity =
			mouseSensitivity;

		InitializeRotation(target);

		_renderer.SetMouseCapture(true);

		_ignoreMouseDelta = true;
	}


	public void Update(
		double deltaTime)
	{
		UpdateMouseCapture();
		UpdateRotation();
		UpdateMovement(deltaTime);
	}


	private void InitializeRotation(
		DVector3 target)
	{
		DVector3 direction =
			target -
			_transform.Position;

		direction =
			direction.Normalized();

		double horizontalLength =
			Math.Sqrt(
				direction.X * direction.X +
				direction.Z * direction.Z);

		_yaw =
			Math.Atan2(
				direction.X,
				direction.Z) *
			180.0 /
			Math.PI;

		_pitch =
			Math.Atan2(
				-direction.Y,
				horizontalLength) *
			180.0 /
			Math.PI;

		_pitch =
			Math.Clamp(
				_pitch,
				-89.0,
				89.0);

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

		if (_input.IsMouseButtonPressed(
			LeftMouseButton))
		{
			_renderer.SetMouseCapture(true);

			_ignoreMouseDelta = true;
		}
	}


	private void UpdateRotation()
	{
		if (!_renderer.MouseCaptured)
			return;

		if (_ignoreMouseDelta)
		{
			_ignoreMouseDelta = false;
			return;
		}

		_yaw -=
			_input.MouseDeltaX *
			_mouseSensitivity;

		_pitch +=
			_input.MouseDeltaY *
			_mouseSensitivity;

		_pitch =
			Math.Clamp(
				_pitch,
				-89.0,
				89.0);

		ApplyRotation();
	}


	private void ApplyRotation()
	{
		_transform.Rotation =
			new DVector3(
				_pitch,
				_yaw,
				0.0);
	}


	private void UpdateMovement(
		double deltaTime)
	{
		if (!_renderer.MouseCaptured)
			return;

		if (!_input.IsKeyDown(
			SDL_Scancode.SDL_SCANCODE_W))
		{
			return;
		}

		Vector3 rotation =
			new(
				(float)_transform.Rotation.X,
				(float)_transform.Rotation.Y,
				(float)_transform.Rotation.Z);

		Quaternion quaternion =
			Quaternion.CreateFromYawPitchRoll(
				rotation.Y *
					(MathF.PI / 180.0f),

				rotation.X *
					(MathF.PI / 180.0f),

				rotation.Z *
					(MathF.PI / 180.0f));

		Vector3 forward =
			Vector3.Transform(
				Vector3.UnitZ,
				quaternion);

		DVector3 movement =
			new(
				forward.X,
				forward.Y,
				forward.Z);

		movement =
			movement.Normalized();

		_transform.Position +=
			movement *
			(_moveSpeed * deltaTime);
	}
}