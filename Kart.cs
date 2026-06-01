using Godot;
using System;

public partial class Kart : RigidBody3D
{
    // Tuning parameters visible in the Godot Inspector
    [Export] public float Acceleration = 2500.0f;
    [Export] public float SteeringSpeed = 4.0f;
    [Export] public float VisualRotationSpeed = 10.0f;
    [Export] public float MaxSpeed = 28.0f;
    [Export] public float Drag = 6.0f;
    [Export] public float Braking = 18.0f;
    [Export] public float SideGrip = 10.0f; // Amount of lateral damping (higher = less sliding/ice physics)
    [Export] public float MaxSideGrip = 40.0f;
    [Export] public float MinSpeedForFullSteer = 6.0f;
    [Export] public float MinSteerScaleWhileStopped = 0.2f;

    private RayCast3D _groundRay;
    private Node3D _visualContainer;

    private float _forwardInput;
    private float _steeringInput;
    private float _yaw = 0.0f;

    public override void _Ready()
    {
        _groundRay = GetNode<RayCast3D>("GroundRay");
        _visualContainer = GetNode<Node3D>("VisualContainer");

        // Prevent the physics sphere from naturally rolling and tilting on its own axes
        AxisLockAngularX = true;
        AxisLockAngularY = true;
        AxisLockAngularZ = true;

        _yaw = _visualContainer.Rotation.Y;
    }

    public override void _Process(double delta)
    {
        // 1. Handle inputs (Ensure you map these in Project Settings -> Input Map)
        _forwardInput = Input.GetAxis("move_backward", "move_forward");
        _steeringInput = Input.GetAxis("move_left", "move_right");

        // 2. Snapping visuals to physics position while decoupling rotation
        _visualContainer.GlobalPosition = GlobalPosition;

        // 3. Smoothly align the car visual mesh with the ground slope
        AlignVisualsWithGround((float)delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        Vector3 groundNormal = Vector3.Up;
        Vector3 forwardDirection;
        Vector3 sideDirection;

        bool isGrounded = _groundRay.IsColliding();
        if (isGrounded)
        {
            groundNormal = _groundRay.GetCollisionNormal();
        }

        (forwardDirection, sideDirection) = GetGroundAlignedBasis(groundNormal);

        if (isGrounded)
        {
            ApplyDriveAndGrip(forwardDirection, sideDirection);
            ApplySteering(forwardDirection, (float)delta);
        }

        CapHorizontalSpeed();
    }

    private void ApplyDriveAndGrip(Vector3 forwardDirection, Vector3 sideDirection)
    {
        float throttle = Mathf.Clamp(_forwardInput, -1.0f, 1.0f);

        // Drive force (constant force per-frame is now bounded by drag + speed cap).
        ApplyCentralForce(forwardDirection * throttle * Acceleration);

        // General drag always applied to stop runaway acceleration.
        Vector3 horizontalVelocity = new Vector3(LinearVelocity.X, 0, LinearVelocity.Z);
        ApplyCentralForce(-horizontalVelocity * Drag);

        // Braking when throttle is released or fighting current direction.
        float forwardSpeed = LinearVelocity.Dot(forwardDirection);
        if (Mathf.Abs(throttle) < 0.05f || (throttle != 0.0f && Mathf.Sign(throttle) != Mathf.Sign(forwardSpeed)))
        {
            ApplyCentralForce(-forwardDirection * forwardSpeed * Braking);
        }

        // Clamp lateral grip to keep tunables from exploding and to avoid icey side drift.
        float stableSideGrip = Mathf.Clamp(SideGrip, 0.0f, MaxSideGrip);
        float lateralSpeed = LinearVelocity.Dot(sideDirection);
        ApplyCentralForce(-sideDirection * lateralSpeed * stableSideGrip * Mass);
    }

    private void ApplySteering(Vector3 forwardDirection, float delta)
    {
        if (Mathf.Abs(_steeringInput) <= 0.05f)
        {
            return;
        }

        float forwardSpeed = Mathf.Abs(LinearVelocity.Dot(forwardDirection));
        float forwardDirSign = Mathf.Abs(LinearVelocity.Dot(forwardDirection)) > 0.001f
            ? Mathf.Sign(LinearVelocity.Dot(forwardDirection))
            : 1.0f;

        float steerScale = Mathf.Max(MinSteerScaleWhileStopped, Mathf.Min(1.0f, forwardSpeed / MinSpeedForFullSteer));
        _yaw += _steeringInput * SteeringSpeed * forwardDirSign * steerScale * delta;
    }

    private void CapHorizontalSpeed()
    {
        Vector3 velocity = LinearVelocity;
        Vector3 horizontalVelocity = new Vector3(velocity.X, 0, velocity.Z);
        float horizontalSpeed = horizontalVelocity.Length();

        if (horizontalSpeed > MaxSpeed)
        {
            Vector3 clamped = horizontalVelocity.Normalized() * MaxSpeed;
            LinearVelocity = new Vector3(clamped.X, velocity.Y, clamped.Z);
        }
    }

    private (Vector3 forward, Vector3 side) GetGroundAlignedBasis(Vector3 groundNormal)
    {
        Vector3 flatForward = new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw)).Normalized();
        Vector3 side = groundNormal.Cross(flatForward);

        if (side.LengthSquared() < 0.0001f)
        {
            side = Vector3.Right;
        }
        else
        {
            side = side.Normalized();
        }

        Vector3 forward = side.Cross(groundNormal).Normalized();
        return (forward, side);
    }

    private void AlignVisualsWithGround(float delta)
    {
        Vector3 groundNormal = Vector3.Up;
        if (_groundRay.IsColliding())
        {
            groundNormal = _groundRay.GetCollisionNormal();
        }

        Vector3 flatForward = new Vector3(Mathf.Sin(_yaw), 0, Mathf.Cos(_yaw)).Normalized();
        Vector3 visualLeft = groundNormal.Cross(flatForward);
        if (visualLeft.LengthSquared() < 0.0001f)
        {
            visualLeft = Vector3.Right;
        }
        else
        {
            visualLeft = visualLeft.Normalized();
        }

        Vector3 visualForward = visualLeft.Cross(groundNormal).Normalized();
        Basis targetBasis = new Basis(visualLeft, groundNormal, visualForward).Orthonormalized();

        // Slerp (Spherical Linear Interpolation) for smooth transition over bumps
        float visualWeight = Mathf.Clamp(VisualRotationSpeed * delta, 0.0f, 1.0f);
        Basis currentBasis = _visualContainer.Transform.Basis.Orthonormalized();
        _visualContainer.Transform = new Transform3D(
            currentBasis.Slerp(targetBasis, visualWeight),
            _visualContainer.Transform.Origin
        );
    }
}
