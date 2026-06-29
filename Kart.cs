using Godot;
using System;

public partial class Kart : RigidBody3D
{
    // Tuning parameters visible in the Godot Inspector
    [Export] public float Acceleration = 2500.0f;
    [Export] public float SteeringSpeed = 4.0f;
    [Export] public float VisualRotationSpeed = 10.0f;
    [Export] public float SideGrip = 10.0f; // Amount of lateral damping (higher = less sliding/ice physics)

    private RayCast3D _groundRay;
    private Node3D _visualContainer;

    // Networked input (server-authoritative)
    private float _forwardInput;
    private float _steeringInput;
    private float _yaw = 0.0f;

    [Export] public bool IsLocalPlayer { get; set; } = false;

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
            // Send input to server
            SendInputRpc(Input.GetAxis("move_backward", "move_forward"),
                           Input.GetAxis("move_left", "move_right"));
        }

        // 2. Snapping visuals to physics position while decoupling rotation
        _visualContainer.GlobalPosition = GlobalPosition;

        // 3. Smoothly align the car visual mesh with the ground slope
        AlignVisualsWithGround((float)delta);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SendInputRpc(float forward, float steer)
    {
        if (Multiplayer.IsServer())
        {
            _forwardInput = forward;
            _steeringInput = steer;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Multiplayer.IsServer() == false) return; // Only server runs physics

        if (_groundRay.IsColliding())
        {
            Vector3 forwardDirection = _visualContainer.Transform.Basis.Z;
            ApplyCentralForce(forwardDirection * _forwardInput * Acceleration);

            Vector3 currentVelocity = LinearVelocity;
            Vector3 sideDirection = _visualContainer.Transform.Basis.X;
            float lateralSpeed = currentVelocity.Dot(sideDirection);
            ApplyCentralForce(-sideDirection * lateralSpeed * SideGrip * Mass);

            if (Mathf.Abs(_steeringInput) > 0.05f)
            {
                float steerDirection = _forwardInput >= 0 ? -_steeringInput : _steeringInput;
                _yaw += steerDirection * SteeringSpeed * (float)delta;
            }
        }
    }

    private void AlignVisualsWithGround(float delta)
    {
        if (_groundRay.IsColliding())
        {
            Vector3 groundNormal = _groundRay.GetCollisionNormal();

            // Calculate forward direction from yaw
            Vector3 flatForward = new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw)).Normalized();

            // Rebuild visual orientation matrix matching the yaw and ground normal
            Vector3 visualLeft = groundNormal.Cross(flatForward).Normalized();
            Vector3 visualForward = visualLeft.Cross(groundNormal).Normalized();

            Basis targetBasis = new Basis(visualLeft, groundNormal, visualForward).Orthonormalized();

            // Slerp (Spherical Linear Interpolation) for smooth transition over bumps
            Basis currentBasis = _visualContainer.Transform.Basis.Orthonormalized();
            _visualContainer.Transform = new Transform3D(
                currentBasis.Slerp(targetBasis, VisualRotationSpeed * delta),
                _visualContainer.Transform.Origin
            );
        }
    }
}