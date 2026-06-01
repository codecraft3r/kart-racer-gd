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
        if (TargetVehiclePath != null && TargetVehiclePath != "")
        {
            _targetVehicle = GetNode<Node3D>(TargetVehiclePath);
            // Fetch the structural markers we set up in the car prefab
            _cameraTarget = _targetVehicle.GetNode<Marker3D>("CameraTarget");
            _visualContainer = _targetVehicle.GetNodeOrNull<Node3D>("VisualContainer");
        }
    }

    public override void _Process(double delta)
    {
        if (_targetVehicle == null || _cameraTarget == null) return;

        float dt = (float)delta;
        float followWeight = Mathf.Clamp(FollowSpeed * dt, 0.0f, 1.0f);
        float lookAtWeight = Mathf.Clamp(LookAtSpeed * dt, 0.0f, 1.0f);

        // 1. Smoothly transition camera position toward the target anchor position (in Global space relative to target rotation)
        Vector3 targetPosition = _cameraTarget.GlobalPosition;

        // If the CameraTarget is a child of the sphere, it doesn't rotate with the visual mesh.
        // We can dynamically compute the ideal offset behind the visual container instead.
        if (_visualContainer != null)
        {
            // Calculate back and up offsets based on yaw-only visual rotation
            // so pitch/roll from slopes does not affect follow offset.
            Vector3 localOffset = new Vector3(0, 1.8f, -4.5f); // 1.8 units up, 4.5 units backward
            Vector3 forward = -_visualContainer.GlobalTransform.Basis.Z;
            Vector3 flatForward = forward - Vector3.Up * forward.Dot(Vector3.Up);
            if (flatForward.LengthSquared() < 0.0001f && _targetVehicle != null)
            {
                forward = -_targetVehicle.GlobalTransform.Basis.Z;
                flatForward = forward - Vector3.Up * forward.Dot(Vector3.Up);
            }
            if (flatForward.LengthSquared() < 0.0001f)
            {
                flatForward = -Vector3.Forward;
            }
            flatForward = flatForward.Normalized();

            float yaw = Mathf.Atan2(flatForward.X, flatForward.Z);
            Vector3 rotatedOffset = new Quaternion(Vector3.Up, yaw) * localOffset;
            targetPosition = _visualContainer.GlobalPosition + rotatedOffset;
        }

        GlobalPosition = GlobalPosition.Lerp(targetPosition, followWeight);

        // 2. Calculate where the camera should look (the center of the visual car mesh)
        Vector3 lookAnchor = _targetVehicle.GlobalPosition;
        if (_visualContainer != null)
        {
            lookAnchor = _visualContainer.GlobalPosition;
        }
        Vector3 lookTarget = lookAnchor + Vector3.Up * 0.5f;

        // 3. Smoothly rotate the camera matrix to track the vehicle's face direction
        Transform3D targetTransform = Transform.LookingAt(lookTarget, Vector3.Up);
        Transform = new Transform3D(
            Transform.Basis.Slerp(targetTransform.Basis, lookAtWeight),
            GlobalPosition
        );
    }
}
