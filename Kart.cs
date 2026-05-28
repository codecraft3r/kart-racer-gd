using Godot;
using System;

public partial class Kart : RigidBody3D
{
    // Tuning parameters visible in the Godot Inspector
    [Export] public float Acceleration = 2500.0f;
    [Export] public float SteeringSpeed = 4.0f;
    [Export] public float VisualRotationSpeed = 10.0f;

    private RayCast3D _groundRay;
    private Node3D _visualContainer;

    private float _forwardInput;
    private float _steeringInput;

    public override void _Ready()
    {
        _groundRay = GetNode<RayCast3D>("GroundRay");
        _visualContainer = GetNode<Node3D>("VisualContainer");

        // Prevent the physics sphere from naturally rolling and tilting on its own axes
        AxisLockAngularX = true;
        AxisLockAngularY = true;
        AxisLockAngularZ = true;
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
        if (_groundRay.IsColliding())
        {
            // Calculate movement forces relative to where the visual car is facing
            Vector3 forwardDirection = -_visualContainer.Transform.Basis.Z;

            // Apply drive force
            ApplyCentralForce(forwardDirection * _forwardInput * Acceleration);

            // Handle steering by rotating the visual container manually
            if (Mathf.Abs(_forwardInput) > 0.05f)
            {
                // Reverse steering direction if driving backward
                float steerDirection = _forwardInput > 0 ? -_steeringInput : _steeringInput;
                _visualContainer.RotateOnAxis(Vector3.Up, steerDirection * SteeringSpeed * (float)delta);
            }
        }
    }

    private void AlignVisualsWithGround(float delta)
    {
        if (_groundRay.IsColliding())
        {
            Vector3 groundNormal = _groundRay.GetCollisionNormal();

            // Rebuild visual orientation matrix (Basis) matching the ground slope normal
            Vector3 visualLeft = _visualContainer.Transform.Basis.X;
            Vector3 visualForward = visualLeft.Cross(groundNormal).Normalized();
            visualLeft = groundNormal.Cross(visualForward).Normalized();

            Basis targetBasis = new Basis(visualLeft, groundNormal, visualForward);

            // Slerp (Spherical Linear Interpolation) for smooth transition over bumps
            _visualContainer.Transform = new Transform3D(
                _visualContainer.Transform.Basis.Slerp(targetBasis, VisualRotationSpeed * delta),
                _visualContainer.Transform.Origin
            );
        }
    }
}