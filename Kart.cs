using Godot;
using System;

public partial class Kart : RigidBody3D
{
    [ExportGroup("Input")]
    [Export] public bool UseLocalInput { get; set; } = true;
    [Export] public bool IsLocalPlayer { get; set; } = false;

    [ExportGroup("Driving")]
    [Export] public float Acceleration = 4200.0f;
    [Export] public float ReverseAcceleration = 2600.0f;
    [Export] public float BrakeForce = 5200.0f;
    [Export] public float MaxForwardSpeed = 34.0f;
    [Export] public float MaxReverseSpeed = 13.0f;
    [Export] public float SteeringSpeed = 3.0f;
    [Export] public float MinSteeringSpeed = 2.0f;
    [Export] public float SideGrip = 18.0f;
    [Export] public float RollingDrag = 1.8f;
    [Export] public float ExtraGravity = 35.0f;

    [ExportGroup("Visuals")]
    [Export] public float VisualRotationSpeed = 12.0f;

    private RayCast3D _groundRay;
    private Node3D _visualContainer;

    private float _forwardInput;
    private float _steeringInput;
    private float _yaw = 0.0f;
    private bool _isGrounded;

    private const float InputDeadzone = 0.05f;

    public override void _Ready()
    {
        _groundRay = GetNode<RayCast3D>("GroundRay");
        _visualContainer = GetNode<Node3D>("VisualContainer");

        // Prevent the physics sphere from naturally rolling and tilting on its own axes
        AxisLockAngularX = true;
        AxisLockAngularY = true;
        AxisLockAngularZ = true;

        _yaw = _visualContainer.Rotation.Y;

        // Server is always the authority for physics
        SetMultiplayerAuthority(1);
    }

    public override void _Process(double delta)
    {
        if (IsLocalPlayer && Multiplayer.IsServer() == false)
        {
            CaptureLocalInput();
            RpcId(1, nameof(SendInputRpc), _forwardInput, _steeringInput);
        }

        if (_visualContainer == null) return;

        _visualContainer.GlobalPosition = GlobalPosition;

        AlignVisualsWithGround((float)delta);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SendInputRpc(float forward, float steer)
    {
        if (Multiplayer.IsServer())
        {
            _forwardInput = Mathf.Clamp(forward, -1.0f, 1.0f);
            _steeringInput = Mathf.Clamp(steer, -1.0f, 1.0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Multiplayer.IsServer() == false) return; // Only server runs physics

        if (UseLocalInput)
        {
            CaptureLocalInput();
        }

        float dt = (float)delta;
        _groundRay.ForceRaycastUpdate();
        _isGrounded = _groundRay.IsColliding();

        if (_isGrounded == false)
        {
            ApplyCentralForce(Vector3.Down * ExtraGravity * Mass);
            return;
        }

        Vector3 groundNormal = _groundRay.GetCollisionNormal();
        Vector3 forwardDirection = GetForwardDirection(groundNormal);
        Vector3 sideDirection = groundNormal.Cross(forwardDirection).Normalized();

        Vector3 planarVelocity = LinearVelocity - groundNormal * LinearVelocity.Dot(groundNormal);
        float forwardSpeed = planarVelocity.Dot(forwardDirection);
        float lateralSpeed = planarVelocity.Dot(sideDirection);
        float planarSpeed = planarVelocity.Length();

        ApplyCentralForce(-sideDirection * lateralSpeed * SideGrip * Mass);
        ApplyCentralForce(-forwardDirection * forwardSpeed * RollingDrag * Mass);

        if (Mathf.Abs(_forwardInput) > InputDeadzone)
        {
            bool braking = Mathf.Abs(forwardSpeed) > 1.0f && Mathf.Sign(_forwardInput) != Mathf.Sign(forwardSpeed);
            float speedLimit = _forwardInput > 0.0f ? MaxForwardSpeed : MaxReverseSpeed;
            float driveForce = braking ? BrakeForce : (_forwardInput > 0.0f ? Acceleration : ReverseAcceleration);

            if (braking || Mathf.Abs(forwardSpeed) < speedLimit)
            {
                ApplyCentralForce(forwardDirection * _forwardInput * driveForce);
            }
        }

        if (Mathf.Abs(_steeringInput) > InputDeadzone && planarSpeed > 0.1f)
        {
            float steeringAuthority = Mathf.Clamp(planarSpeed / MinSteeringSpeed, 0.0f, 1.0f);
            float highSpeedFade = 1.0f - Mathf.Clamp(planarSpeed / MaxForwardSpeed, 0.0f, 1.0f) * 0.45f;
            float reverseMultiplier = forwardSpeed >= -0.5f ? 1.0f : -1.0f;

            _yaw -= _steeringInput * reverseMultiplier * SteeringSpeed * steeringAuthority * highSpeedFade * dt;
        }
    }

    private void CaptureLocalInput()
    {
        _forwardInput = Mathf.Clamp(Input.GetAxis("move_backward", "move_forward"), -1.0f, 1.0f);
        _steeringInput = Mathf.Clamp(Input.GetAxis("move_left", "move_right"), -1.0f, 1.0f);
    }

    private void AlignVisualsWithGround(float delta)
    {
        if (_isGrounded)
        {
            Vector3 groundNormal = _groundRay.GetCollisionNormal();
            Vector3 visualForward = GetForwardDirection(groundNormal);
            Vector3 visualRight = groundNormal.Cross(visualForward).Normalized();

            Basis targetBasis = new Basis(visualRight, groundNormal, visualForward).Orthonormalized();
            Basis currentBasis = _visualContainer.Transform.Basis.Orthonormalized();
            float blend = Mathf.Clamp(VisualRotationSpeed * delta, 0.0f, 1.0f);

            _visualContainer.Transform = new Transform3D(
                currentBasis.Slerp(targetBasis, blend),
                _visualContainer.Transform.Origin
            );
        }
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
