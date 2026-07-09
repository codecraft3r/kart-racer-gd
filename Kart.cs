using Godot;
using System;

public partial class Kart : RigidBody3D
{
    [ExportGroup("Input")]
    [Export] public int OwnerPeerId { get; set; } = 1;
    [Export] public bool UseLocalInput { get; set; } = true;
    [Export] public bool IsLocalPlayer { get; set; } = false;
    [Export] public float TapInputHoldTime = 0.16f;

    [ExportGroup("Driving")]
    [Export] public float Acceleration = 4200.0f;
    [Export] public float MaxSpeed = 34.0f;
    [Export] public float CoastDeceleration = 30.0f;
    [Export] public float SteeringSpeed = 3.0f;
    [Export] public float ExtraGravity = 35.0f;

    private RayCast3D _groundRay;

    private float _forwardInput;
    private float _steeringInput;
    private float _forwardTapInput;
    private float _steeringTapInput;
    private float _forwardTapTimer;
    private float _steeringTapTimer;
    private float _yaw = 0.0f;
    private bool _isGrounded;

    private const float InputDeadzone = 0.05f;

    public override void _Ready()
    {
        _groundRay = GetNode<RayCast3D>("GroundRay");

        AxisLockAngularX = true;
        AxisLockAngularY = true;
        AxisLockAngularZ = true;

        _yaw = Rotation.Y;

        SetMultiplayerAuthority(1);
    }

    public override void _Process(double delta)
    {
        UpdateTapTimers((float)delta);

        if (IsLocalPlayer && IsConnectedClient())
        {
            CaptureLocalInput();
            RpcId(1, nameof(SendInputRpc), _forwardInput, _steeringInput);
        }

        if (ShouldRunPhysics() == false)
        {
            _yaw = Rotation.Y;
            _groundRay.ForceRaycastUpdate();
            _isGrounded = _groundRay.IsColliding();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (UseLocalInput == false && IsLocalPlayer == false && IsOffline() == false) return;

        if (@event.IsActionPressed("move_forward"))
            BufferTapInput(1.0f, 0.0f);
        else if (@event.IsActionPressed("move_backward"))
            BufferTapInput(-1.0f, 0.0f);
        else if (@event.IsActionPressed("move_right"))
            BufferTapInput(0.0f, 1.0f);
        else if (@event.IsActionPressed("move_left"))
            BufferTapInput(0.0f, -1.0f);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SendInputRpc(float forward, float steer)
    {
        if (Multiplayer.IsServer())
        {
            int senderId = Multiplayer.GetRemoteSenderId();
            if (senderId != OwnerPeerId)
            {
                GD.PushWarning($"Rejected kart input from peer {senderId}; kart belongs to peer {OwnerPeerId}.");
                return;
            }

            _forwardInput = Mathf.Clamp(forward, -1.0f, 1.0f);
            _steeringInput = Mathf.Clamp(steer, -1.0f, 1.0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (ShouldRunPhysics() == false) return;

        if (UseLocalInput || IsOffline())
            CaptureLocalInput();

        var dt = (float)delta;
        _groundRay.ForceRaycastUpdate();
        _isGrounded = _groundRay.IsColliding();

        if (!_isGrounded)
        {
            ApplyCentralForce(Vector3.Down * ExtraGravity * Mass);
            SyncYawToBodyRotation();
            return;
        }

        // Steer: rotate the body. With angular axes locked, modifying Rotation.Y is the only
        // free rotation degree.
        if (Mathf.Abs(_steeringInput) > InputDeadzone)
            _yaw -= _steeringInput * SteeringSpeed * dt;

        // Force all planar momentum to lie along the body's local +Z axis. The kart can
        // never carry lateral velocity -- collisions and ramps redirect it forward.
        Vector3 groundNormal = _groundRay.GetCollisionNormal();
        Vector3 forwardOnGround = GetForwardDirection(groundNormal);
        Vector3 rightOnGround = groundNormal.Cross(forwardOnGround).Normalized();
        Basis alignedBasis = new Basis(rightOnGround, groundNormal, forwardOnGround).Orthonormalized();
        Rotation = alignedBasis.GetEuler();
        Vector3 bodyForward = GlobalTransform.Basis.Z;
        Vector3 planarVel = LinearVelocity - groundNormal * LinearVelocity.Dot(groundNormal);
        LinearVelocity = bodyForward * planarVel.Dot(bodyForward) + groundNormal * LinearVelocity.Dot(groundNormal);

        // Throttle: accelerate along body local +Z, clamped to MaxSpeed.
        if (Mathf.Abs(_forwardInput) > InputDeadzone)
        {
            ApplyCentralForce(bodyForward * Acceleration * _forwardInput);
            float newForwardSpeed = (LinearVelocity - groundNormal * LinearVelocity.Dot(groundNormal)).Dot(bodyForward);
            float clamped = Mathf.Clamp(newForwardSpeed, -MaxSpeed, MaxSpeed);
            LinearVelocity = bodyForward * clamped + groundNormal * LinearVelocity.Dot(groundNormal);
        }
        // Coast: bleed off forward speed when throttle is released.
        else
        {
            float currentForwardSpeed = (LinearVelocity - groundNormal * LinearVelocity.Dot(groundNormal)).Dot(bodyForward);
            if (Mathf.Abs(currentForwardSpeed) > 0.0f)
            {
                float slowed = Mathf.MoveToward(currentForwardSpeed, 0.0f, CoastDeceleration * dt);
                LinearVelocity = bodyForward * slowed + groundNormal * LinearVelocity.Dot(groundNormal);
            }
        }
    }

    public void ApplyNetworkSnapshot(Vector3 position, Vector3 rotation, Vector3 velocity)
    {
        if (ShouldRunPhysics())
            return;

        GlobalPosition = position;
        Rotation = rotation;
        LinearVelocity = velocity;
        _yaw = rotation.Y;
    }

    private void SyncYawToBodyRotation()
    {
        _yaw = Rotation.Y;
    }

    private void CaptureLocalInput()
    {
        float forwardAxis = Input.GetAxis("move_backward", "move_forward");
        float steeringAxis = Input.GetAxis("move_left", "move_right");

        _forwardInput = Mathf.Abs(forwardAxis) > InputDeadzone ? forwardAxis : (_forwardTapTimer > 0.0f ? _forwardTapInput : 0.0f);
        _steeringInput = Mathf.Abs(steeringAxis) > InputDeadzone ? steeringAxis : (_steeringTapTimer > 0.0f ? _steeringTapInput : 0.0f);

        _forwardInput = Mathf.Clamp(_forwardInput, -1.0f, 1.0f);
        _steeringInput = Mathf.Clamp(_steeringInput, -1.0f, 1.0f);
    }

    private void BufferTapInput(float forward, float steering)
    {
        if (Mathf.Abs(forward) > InputDeadzone)
        {
            _forwardTapInput = forward;
            _forwardTapTimer = TapInputHoldTime;
        }

        if (Mathf.Abs(steering) > InputDeadzone)
        {
            _steeringTapInput = steering;
            _steeringTapTimer = TapInputHoldTime;
        }
    }

    private void UpdateTapTimers(float delta)
    {
        _forwardTapTimer = Mathf.Max(0.0f, _forwardTapTimer - delta);
        _steeringTapTimer = Mathf.Max(0.0f, _steeringTapTimer - delta);
    }

    private bool ShouldRunPhysics()
    {
        return IsOffline() || Multiplayer.IsServer();
    }

    private bool IsConnectedClient()
    {
        return IsOffline() == false && Multiplayer.IsServer() == false;
    }

    private bool IsOffline()
    {
        return Multiplayer.HasMultiplayerPeer() == false || Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer;
    }

    private Vector3 GetForwardDirection(Vector3 groundNormal)
    {
        Vector3 forward = new Vector3(Mathf.Sin(_yaw), 0.0f, Mathf.Cos(_yaw));
        forward -= groundNormal * forward.Dot(groundNormal);

        if (forward.LengthSquared() < 0.0001f)
        {
            forward = Vector3.Back - groundNormal * Vector3.Back.Dot(groundNormal);
        }

        return forward.Normalized();
    }
}