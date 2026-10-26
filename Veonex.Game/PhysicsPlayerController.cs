
using SDL;
using Veonex.Core;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Physics;
using Veonex.Render;

namespace Veonex.Game;
public sealed class PhysicsPlayerController
{
	private const byte LeftMouseButton = 1;

	private const double PitchLimit = 85.0;
	private const double RotationSmoothness = 24.0;

	// Ground movement.
	private const double GroundAcceleration = 16.0;
	private const double GroundFriction = 5.0;
	private const double StopSpeed = 1.5;

	// Air movement. Total horizontal speed is intentionally unlimited.
	private const double AirAcceleration = 3.0;

	// Jump timing.
	private const double JumpBufferDuration = 0.12;
	private const double CoyoteTimeDuration = 0.08;

	// Dynamic field of view.
	private const double FovStartSpeedMultiplier = 0.65;
	private const double FovFullSpeedMultiplier = 2.6;
	private const double FovSmoothness = 5.5;

	private const double MaximumDeltaTime = 0.1;
	private const double DirectionEpsilon = 0.000001;
	private const double VelocityEpsilon = 0.0001;

	private readonly Entity _player;
	private readonly Transform _playerTransform;
	private readonly Transform _cameraTransform;

	private readonly Camera _camera;
	private readonly InputManager _input;
	private readonly RenderBackend _renderer;
	private readonly Veonex.Physics.Physics _physics;

	private const double ThirdPersonDistance = 4.0;
	private const double ThirdPersonHeightOffset = 0.15;
	private const double MinThirdPersonDistance = 0.5;
	private const double MaxThirdPersonDistance = 20.0;

	private readonly double _moveSpeed;
	private readonly double _jumpSpeed;
	private readonly double _mouseSensitivity;
	private readonly double _eyeHeight;

	private double _baseFieldOfView;
	private double _maximumFieldOfViewIncrease = 10.0;

	private double _yaw;
	private double _pitch;
	private double _targetYaw;
	private double _targetPitch;

	private double _cameraDistance = ThirdPersonDistance;
	private double _shoulderOffset = 0.0;

	private double _jumpBufferTimer;
	private double _coyoteTimer;

	private bool _ignoreMouseDelta;

	/// <summary>
	/// Automatically jumps when landing while Space is held.
	/// </summary>
	public bool AutoBunnyHop { get; set; } = true;

	/// <summary>
	/// Enables speed-based field-of-view changes.
	/// </summary>
	public bool DynamicFieldOfView { get; set; } = true;

	/// <summary>
	/// Camera FOV at rest, in degrees.
	/// Defaults to the camera's FOV when this controller is created.
	/// </summary>
	public double BaseFieldOfView
	{
		get => _baseFieldOfView;

		set
		{
			if (!double.IsFinite(value) || value < 1.0 || value > 170.0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(value),
					"Base FOV must be between 1 and 170 degrees.");
			}

			_baseFieldOfView = value;
		}
	}

	/// <summary>
	/// Maximum additional FOV at high movement speeds, in degrees.
	/// </summary>
	public double MaximumFieldOfViewIncrease
	{
		get => _maximumFieldOfViewIncrease;

		set
		{
			if (!double.IsFinite(value) || value < 0.0 || value > 30.0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(value),
					"Maximum FOV increase must be between 0 and 30 degrees.");
			}

			_maximumFieldOfViewIncrease = value;
		}
	}

	/// <summary>
	/// Current horizontal speed in world units per second.
	/// Air speed can exceed the normal movement speed.
	/// </summary>
	public double HorizontalSpeed
	{
		get
		{
			if (!_physics.TryGetLinearVelocity(
					_player,
					out DVector3 velocity))
			{
				return 0.0;
			}

			return Math.Sqrt(
				velocity.X * velocity.X +
				velocity.Z * velocity.Z);
		}
	}

	public PhysicsPlayerController(
		Entity player,
		Camera camera,
		InputManager input,
		RenderBackend renderer,
		Veonex.Physics.Physics physics,
		DVector3 lookTarget,
		double moveSpeed = 6.0,
		double jumpSpeed = 7.0,
		double mouseSensitivity = 0.15,
		double eyeHeight = 0.62,
		bool autoBunnyHop = true,
		bool dynamicFieldOfView = true)
	{
		ArgumentNullException.ThrowIfNull(player);
		ArgumentNullException.ThrowIfNull(camera);
		ArgumentNullException.ThrowIfNull(input);
		ArgumentNullException.ThrowIfNull(renderer);
		ArgumentNullException.ThrowIfNull(physics);

		if (!double.IsFinite(moveSpeed) || moveSpeed <= 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(moveSpeed));
		}

		if (!double.IsFinite(jumpSpeed) || jumpSpeed <= 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(jumpSpeed));
		}

		if (!double.IsFinite(mouseSensitivity) ||
			mouseSensitivity <= 0.0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(mouseSensitivity));
		}

		if (!double.IsFinite(eyeHeight) || eyeHeight < 0.0)
		{
			throw new ArgumentOutOfRangeException(nameof(eyeHeight));
		}

		if (!double.IsFinite(camera.FieldOfView) ||
			camera.FieldOfView < 1.0 ||
			camera.FieldOfView > 170.0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(camera),
				"Camera FOV must be between 1 and 170 degrees.");
		}

		_player = player;
		_playerTransform = player.Get<Transform>();

		_camera = camera;
		_cameraTransform = camera.Entity.Get<Transform>();

		_input = input;
		_renderer = renderer;
		_physics = physics;

		_moveSpeed = moveSpeed;
		_jumpSpeed = jumpSpeed;
		_mouseSensitivity = mouseSensitivity;
		_eyeHeight = eyeHeight;

		_cameraDistance = ThirdPersonDistance;
		_shoulderOffset = 0.0;

		_baseFieldOfView = camera.FieldOfView;

		AutoBunnyHop = autoBunnyHop;
		DynamicFieldOfView = dynamicFieldOfView;

		InitializeRotation(lookTarget);

		_renderer.SetMouseCapture(true);
		_ignoreMouseDelta = true;

		UpdateCameraTransform();
	}

	/// <summary>
	/// Call after InputManager.Update() and before Physics.Update().
	/// </summary>
	public void UpdateInput(double deltaTime)
	{
		if (!double.IsFinite(deltaTime) || deltaTime < 0.0)
		{
			deltaTime = 0.0;
		}

		deltaTime = Math.Min(deltaTime, MaximumDeltaTime);

		UpdateMouseCapture();
		UpdateRotation(deltaTime);
		UpdateMovement(deltaTime);
		UpdateFieldOfView(deltaTime);
	}

	/// <summary>
	/// Call after Physics.Update() so the camera follows the latest pose.
	/// </summary>
	public void UpdateCamera()
	{
		UpdateCameraTransform();
	}

	private void InitializeRotation(DVector3 target)
	{
		DVector3 eyePosition = _playerTransform.Position +
			new DVector3(0.0, _eyeHeight, 0.0);

		DVector3 direction = target - eyePosition;

		if (direction.LengthSquared > DirectionEpsilon)
		{
			direction = direction.Normalized();
		}
		else
		{
			direction = new DVector3(0.0, 0.0, 1.0);
		}

		double horizontalLength = Math.Sqrt(
			direction.X * direction.X +
			direction.Z * direction.Z);

		_yaw = NormalizeAngle(
			Math.Atan2(direction.X, direction.Z) *
			180.0 / Math.PI);

		_pitch = Math.Atan2(
			-direction.Y,
			horizontalLength) * 180.0 / Math.PI;

		_pitch = Math.Clamp(_pitch, -PitchLimit, PitchLimit);

		_targetYaw = _yaw;
		_targetPitch = _pitch;
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
				_targetYaw = NormalizeAngle(
					_targetYaw -
					_input.MouseDeltaX * _mouseSensitivity);

				_targetPitch = Math.Clamp(
					_targetPitch +
					_input.MouseDeltaY * _mouseSensitivity,
					-PitchLimit,
					PitchLimit);
			}
		}

		double factor = 1.0 -
			Math.Exp(-RotationSmoothness * deltaTime);

		// Follow the shortest path when crossing +/-180 degrees.
		_yaw = NormalizeAngle(
			_yaw +
			NormalizeAngle(_targetYaw - _yaw) * factor);

		_pitch += (_targetPitch - _pitch) * factor;
	}

	private void UpdateMovement(double deltaTime)
	{
		if (!_physics.TryGetLinearVelocity(
				_player,
				out DVector3 currentVelocity))
		{
			return;
		}

		bool controlsEnabled = _renderer.MouseCaptured;

		// Do not treat the player as grounded while they are already
		// moving upwards. This helps prevent repeated jumps on one landing.
		bool grounded = true;

		if (grounded)
		{
			_coyoteTimer = CoyoteTimeDuration;
		}
		else
		{
			_coyoteTimer = Math.Max(
				0.0,
				_coyoteTimer - deltaTime);
		}

		UpdateJumpBuffer(deltaTime, controlsEnabled);

		DVector3 wishDirection = GetMovementDirection(
			controlsEnabled);

		DVector3 horizontalVelocity = new(
			currentVelocity.X,
			0.0,
			currentVelocity.Z);

		double verticalVelocity = currentVelocity.Y;

		bool jumped = false;

		// Jump buffering and coyote time improve responsiveness.
		if (_jumpBufferTimer > 0.0 && _coyoteTimer > 0.0)
		{
			verticalVelocity = _jumpSpeed;

			_jumpBufferTimer = 0.0;
			_coyoteTimer = 0.0;

			jumped = true;
		}

		if (grounded)
		{
			if (!jumped)
			{
				horizontalVelocity = ApplyGroundFriction(
					horizontalVelocity,
					deltaTime);

				if (wishDirection.LengthSquared > DirectionEpsilon)
				{
					horizontalVelocity = Accelerate(
						horizontalVelocity,
						wishDirection,
						_moveSpeed,
						GroundAcceleration,
						deltaTime);
				}
			}

			// When jumping, skip ground friction and ground acceleration.
			// This preserves existing momentum for bunny hopping.
		}
		else if (wishDirection.LengthSquared > DirectionEpsilon)
		{
			// Air acceleration only limits speed gained along the input
			// direction. There is no cap on the total horizontal velocity.
			horizontalVelocity = Accelerate(
				horizontalVelocity,
				wishDirection,
				_moveSpeed,
				AirAcceleration,
				deltaTime);
		}

		_physics.TrySetLinearVelocity(
			_player,
			new DVector3(
				horizontalVelocity.X,
				verticalVelocity,
				horizontalVelocity.Z));
	}

	private void UpdateJumpBuffer(
		double deltaTime,
		bool controlsEnabled)
	{
		if (!controlsEnabled)
		{
			_jumpBufferTimer = 0.0;
			return;
		}

		bool jumpHeld = _input.IsKeyDown(
			SDL_Scancode.SDL_SCANCODE_SPACE);

		bool jumpPressed = _input.IsKeyPressed(
			SDL_Scancode.SDL_SCANCODE_SPACE);

		bool requestJump = AutoBunnyHop
			? jumpHeld
			: jumpPressed;

		if (requestJump)
		{
			_jumpBufferTimer = JumpBufferDuration;
		}
		else
		{
			_jumpBufferTimer = Math.Max(
				0.0,
				_jumpBufferTimer - deltaTime);
		}
	}

	private DVector3 GetMovementDirection(bool controlsEnabled)
	{
		if (!controlsEnabled)
		{
			return DVector3.Zero;
		}

		double yawRadians = _yaw * Math.PI / 180.0;

		DVector3 forward = new(
			Math.Sin(yawRadians),
			0.0,
			Math.Cos(yawRadians));

		DVector3 right = new(
			Math.Cos(yawRadians),
			0.0,
			-Math.Sin(yawRadians));

		DVector3 movement = DVector3.Zero;

		if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_W))
		{
			movement += forward;
		}

		if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_S))
		{
			movement -= forward;
		}

		// Preserve the A/D inversion from your current controller.
		if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_A))
		{
			movement += right;
		}

		if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_D))
		{
			movement -= right;
		}

		// Diagonal movement has the same input magnitude as straight
		// movement, without removing momentum already present in the air.
		if (movement.LengthSquared > DirectionEpsilon)
		{
			movement = movement.Normalized();
		}

		return movement;
	}

	/// <summary>
	/// Proportional ground friction gives a gradual, speed-dependent stop.
	/// </summary>
	private static DVector3 ApplyGroundFriction(
		DVector3 velocity,
		double deltaTime)
	{
		double speed = Math.Sqrt(
			velocity.X * velocity.X +
			velocity.Z * velocity.Z);

		if (speed < VelocityEpsilon)
		{
			return DVector3.Zero;
		}

		double control = Math.Max(speed, StopSpeed);
		double drop = control * GroundFriction * deltaTime;

		double newSpeed = Math.Max(speed - drop, 0.0);
		double scale = newSpeed / speed;

		return new DVector3(
			velocity.X * scale,
			0.0,
			velocity.Z * scale);
	}

	/// <summary>
	/// Accelerates towards a wish direction.
	/// Only the velocity projection in that direction is limited;
	/// total horizontal speed is not clamped.
	/// </summary>
	private static DVector3 Accelerate(
		DVector3 velocity,
		DVector3 wishDirection,
		double wishSpeed,
		double acceleration,
		double deltaTime)
	{
		double currentSpeed =
			velocity.X * wishDirection.X +
			velocity.Z * wishDirection.Z;

		double additionalSpeed = wishSpeed - currentSpeed;

		if (additionalSpeed <= 0.0)
		{
			return velocity;
		}

		double accelerationSpeed =
			acceleration * deltaTime * wishSpeed;

		accelerationSpeed = Math.Min(
			accelerationSpeed,
			additionalSpeed);

		return new DVector3(
			velocity.X + wishDirection.X * accelerationSpeed,
			0.0,
			velocity.Z + wishDirection.Z * accelerationSpeed);
	}

	/// <summary>
	/// Widens FOV as horizontal speed rises and smoothly restores the
	/// configured base FOV when the player slows down.
	/// RenderBackend reads Camera.FieldOfView when building projection.
	/// </summary>
	private void UpdateFieldOfView(double deltaTime)
	{
		double targetFov = _baseFieldOfView;

		if (DynamicFieldOfView)
		{
			double speedRatio = HorizontalSpeed / _moveSpeed;

			double blend = (
				speedRatio - FovStartSpeedMultiplier) /
				(FovFullSpeedMultiplier - FovStartSpeedMultiplier);

			blend = Math.Clamp(blend, 0.0, 1.0);

			// Smoothstep avoids a sudden change near the start/end thresholds.
			blend = blend * blend * (3.0 - 2.0 * blend);

			targetFov += blend * _maximumFieldOfViewIncrease;
		}

		targetFov = Math.Clamp(targetFov, 1.0, 179.0);

		double factor = 1.0 -
			Math.Exp(-FovSmoothness * deltaTime);

		_camera.FieldOfView +=
			(targetFov - _camera.FieldOfView) * factor;
	}

	private void UpdateCameraTransform()
	{
		DVector3 eye = _playerTransform.Position +
			new DVector3(0.0, _eyeHeight + ThirdPersonHeightOffset, 0.0);

		double yawRad = _yaw * Math.PI / 180.0;
		double pitchRad = _pitch * Math.PI / 180.0;

		DVector3 lookDirection = new(
			Math.Sin(yawRad) * Math.Cos(pitchRad),
			-Math.Sin(pitchRad),
			Math.Cos(yawRad) * Math.Cos(pitchRad));

		DVector3 forward = new(
			Math.Sin(yawRad),
			0.0,
			Math.Cos(yawRad));

		DVector3 right = new(
			Math.Cos(yawRad),
			0.0,
			-Math.Sin(yawRad));

		DVector3 cameraPosition =
			eye -
			lookDirection * _cameraDistance +
			right * _shoulderOffset;

		_cameraTransform.Position = cameraPosition;

		_cameraTransform.Rotation = new DVector3(_pitch, _yaw, 0.0);
	}

	private static double NormalizeAngle(double angle)
	{
		angle %= 360.0;

		if (angle > 180.0)
		{
			angle -= 360.0;
		}
		else if (angle < -180.0)
		{
			angle += 360.0;
		}

		return angle;
	}
}
