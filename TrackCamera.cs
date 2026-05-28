using Godot;
using System;

public partial class TrackCamera : Camera3D
{
    // Assign your car node in the Godot inspector
    [Export] public NodePath TargetVehiclePath;

    [Export] public float FollowSpeed = 10.0f;
    [Export] public float LookAtSpeed = 12.0f;

    private Node3D _targetVehicle;
    private Marker3D _cameraTarget;
    private Node3D _visualContainer;

    public override void _Ready()
    {
        if (TargetVehiclePath != null)
        {
            _targetVehicle = GetNode<Node3D>(TargetVehiclePath);
            // Fetch the structural markers we set up in the car prefab
            _cameraTarget = _targetVehicle.GetNode<Marker3D>("CameraTarget");
            _visualContainer = _targetVehicle.GetNode<Node3D>("VisualContainer");
        }
    }

    public override void _Process(double delta)
    {
        if (_targetVehicle == null || _cameraTarget == null) return;

        float dt = (float)delta;

        // 1. Smoothly transition camera position toward the target anchor position (in Global space relative to target rotation)
        Vector3 targetPosition = _cameraTarget.GlobalPosition;

        // If the CameraTarget is a child of the sphere, it doesn't rotate with the visual mesh.
        // We can dynamically compute the ideal offset behind the visual container instead.
        if (_visualContainer != null)
        {
            // Calculate back and up offsets based on the visual rotation
            Vector3 localOffset = new Vector3(0, 1.8f, -4.5f); // 1.8 units up, 4.5 units backward
            Vector3 rotatedOffset = _visualContainer.GlobalTransform.Basis * localOffset;
            targetPosition = _visualContainer.GlobalPosition + rotatedOffset;
        }

        GlobalPosition = GlobalPosition.Lerp(targetPosition, FollowSpeed * dt);

        // 2. Calculate where the camera should look (the center of the visual car mesh)
        Vector3 lookTarget = _visualContainer.GlobalPosition + Vector3.Up * 0.5f;

        // 3. Smoothly rotate the camera matrix to track the vehicle's face direction
        Transform3D targetTransform = Transform.LookingAt(lookTarget, Vector3.Up);
        Transform = new Transform3D(
            Transform.Basis.Slerp(targetTransform.Basis, LookAtSpeed * dt),
            GlobalPosition
        );
    }
}