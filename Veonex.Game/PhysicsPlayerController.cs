using SDL;
using Veonex.Core;
using Veonex.Input;
using Veonex.Mathematics;
using Veonex.Physics;
using Veonex.Render;

namespace Veonex.Game;

/// <summary>
/// First-person controller driven by a PhysicsBody.
/// The controller changes horizontal velocity and jump velocity; the physics
/// simulation remains responsible for position, gravity, and collisions.
/// </summary>
public sealed class PhysicsPlayerController
{
    private const byte LeftMouseButton = 1;
    private const double PitchLimit = 85.0;
    private const double RotationSmoothness = 24.0;
    private const double MovementAcceleration = 20.0;
    private const double MovementDeceleration = 24.0;
    private const double MaximumDeltaTime = 0.1;

    private readonly Entity _player;
    private readonly Transform _playerTransform;
    private readonly Transform _cameraTransform;
    private readonly InputManager _input;
    private readonly RenderBackend _renderer;
    private readonly Veonex.Physics.Physics _physics;

    private readonly double _moveSpeed;
    private readonly double _jumpSpeed;
    private readonly double _mouseSensitivity;
    private readonly double _eyeHeight;

    private double _yaw;
    private double _pitch;
    private double _targetYaw;
    private double _targetPitch;
    private bool _ignoreMouseDelta;

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
        double eyeHeight = 0.62)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(physics);

        if (!double.IsFinite(moveSpeed) || moveSpeed <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(moveSpeed));
        if (!double.IsFinite(jumpSpeed) || jumpSpeed <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(jumpSpeed));
        if (!double.IsFinite(mouseSensitivity) || mouseSensitivity <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(mouseSensitivity));
        if (!double.IsFinite(eyeHeight) || eyeHeight < 0.0)
            throw new ArgumentOutOfRangeException(nameof(eyeHeight));

        _player = player;
        _playerTransform = player.Get<Transform>();
        _cameraTransform = camera.Entity.Get<Transform>();
        _input = input;
        _renderer = renderer;
        _physics = physics;

        _moveSpeed = moveSpeed;
        _jumpSpeed = jumpSpeed;
        _mouseSensitivity = mouseSensitivity;
        _eyeHeight = eyeHeight;

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
        deltaTime = Math.Clamp(deltaTime, 0.0, MaximumDeltaTime);

        UpdateMouseCapture();
        UpdateRotation(deltaTime);
        UpdateMovement(deltaTime);
    }

    /// <summary>
    /// Call after Physics.Update() so the camera follows the latest player pose.
    /// </summary>
    public void UpdateCamera()
    {
        UpdateCameraTransform();
    }

    private void InitializeRotation(DVector3 target)
    {
        DVector3 eyePosition = _playerTransform.Position + new DVector3(0.0, _eyeHeight, 0.0);
        DVector3 direction = (target - eyePosition).Normalized();

        double horizontalLength = Math.Sqrt(
            direction.X * direction.X +
            direction.Z * direction.Z);

        _yaw = Math.Atan2(direction.X, direction.Z) * 180.0 / Math.PI;
        _pitch = Math.Atan2(-direction.Y, horizontalLength) * 180.0 / Math.PI;
        _pitch = Math.Clamp(_pitch, -PitchLimit, PitchLimit);

        _targetYaw = _yaw;
        _targetPitch = _pitch;
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
                _targetYaw -= _input.MouseDeltaX * _mouseSensitivity;
                _targetPitch += _input.MouseDeltaY * _mouseSensitivity;
                _targetPitch = Math.Clamp(_targetPitch, -PitchLimit, PitchLimit);
            }
        }

        double factor = 1.0 - Math.Exp(-RotationSmoothness * deltaTime);
        _yaw += (_targetYaw - _yaw) * factor;
        _pitch += (_targetPitch - _pitch) * factor;
    }

    private void UpdateMovement(double deltaTime)
    {
        if (!_physics.TryGetLinearVelocity(_player, out DVector3 currentVelocity))
            return;

        DVector3 movement = DVector3.Zero;

        if (_renderer.MouseCaptured)
        {
            double yawRadians = _yaw * Math.PI / 180.0;
            DVector3 forward = new(
                Math.Sin(yawRadians),
                0.0,
                Math.Cos(yawRadians));

            DVector3 right = new(
                Math.Cos(yawRadians),
                0.0,
                -Math.Sin(yawRadians));

            if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_W))
                movement += forward;
            if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_S))
                movement -= forward;
            if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_D))
                movement -= right;
            if (_input.IsKeyDown(SDL_Scancode.SDL_SCANCODE_A))
                movement += right;

            if (movement.LengthSquared > 0.0)
                movement = movement.Normalized();
        }

        DVector3 desiredHorizontalVelocity = movement * _moveSpeed;
        DVector3 currentHorizontalVelocity = new(
            currentVelocity.X,
            0.0,
            currentVelocity.Z);

        bool accelerating = desiredHorizontalVelocity.LengthSquared > 0.0001;
        double rate = accelerating ? MovementAcceleration : MovementDeceleration;
        double factor = 1.0 - Math.Exp(-rate * deltaTime);

        DVector3 horizontalVelocity = currentHorizontalVelocity +
            (desiredHorizontalVelocity - currentHorizontalVelocity) * factor;

        double verticalVelocity = currentVelocity.Y;
        if (_input.IsKeyPressed(SDL_Scancode.SDL_SCANCODE_SPACE) &&
            _physics.IsGrounded(_player))
        {
            verticalVelocity = _jumpSpeed;
        }

        _physics.TrySetLinearVelocity(
            _player,
            new DVector3(horizontalVelocity.X, verticalVelocity, horizontalVelocity.Z));
    }

    private void UpdateCameraTransform()
    {
        _cameraTransform.Position = _playerTransform.Position +
            new DVector3(0.0, _eyeHeight, 0.0);

        _cameraTransform.Rotation = new DVector3(_pitch, _yaw, 0.0);
    }
}
