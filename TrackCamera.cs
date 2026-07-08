using Godot;
using System;

public partial class TrackCamera : Camera3D
{
    [ExportGroup("Target")]
    [Export] public NodePath TargetVehiclePath;

    [ExportGroup("Follow")]
    [Export] public float FollowSpeed = 10.0f;
    [Export] public float LookAtSpeed = 12.0f;
    [Export] public float BaseDistance = 5.2f;
    [Export] public float BaseHeight = 2.1f;
    [Export] public float SpeedPullback = 2.8f;
    [Export] public float SpeedLift = 0.8f;
    [Export] public float LookHeight = 0.75f;
    [Export] public float LookAheadDistance = 4.5f;

    [ExportGroup("Racing Feel")]
    [Export] public float MaxReferenceSpeed = 34.0f;
    [Export] public float MinFov = 63.0f;
    [Export] public float MaxFov = 78.0f;
    [Export] public float FovLerpSpeed = 5.0f;
    [Export] public float MaxRollDegrees = 5.0f;
    [Export] public float RollResponse = 0.18f;

    private Node3D _targetVehicle;
    private Marker3D _cameraTarget;
    private Node3D _visualContainer;
    private RigidBody3D _targetBody;

    public override void _Ready()
    {
        if (TargetVehiclePath == null || string.IsNullOrWhiteSpace(TargetVehiclePath.ToString()))
            return;

        SetTarget(GetNodeOrNull<Node3D>(TargetVehiclePath));
    }

    public void SetTarget(Node3D targetVehicle)
    {
        if (targetVehicle == null || !GodotObject.IsInstanceValid(targetVehicle))
        {
            ClearTarget();
            return;
        }

        _targetVehicle = targetVehicle;
        _cameraTarget = _targetVehicle.GetNodeOrNull<Marker3D>("CameraTarget");
        _visualContainer = _targetVehicle.GetNodeOrNull<Node3D>("VisualContainer");
        _targetBody = _targetVehicle as RigidBody3D;

        if (_cameraTarget == null || _visualContainer == null)
            GD.PushWarning("TrackCamera expects the target vehicle to contain CameraTarget and VisualContainer children.");
    }

    public override void _Process(double delta)
    {
        if (HasValidTarget() == false) return;

        float dt = (float)delta;
        float followBlend = Mathf.Clamp(FollowSpeed * dt, 0.0f, 1.0f);
        float lookBlend = Mathf.Clamp(LookAtSpeed * dt, 0.0f, 1.0f);
        float fovBlend = Mathf.Clamp(FovLerpSpeed * dt, 0.0f, 1.0f);
        float speed = _targetBody?.LinearVelocity.Length() ?? 0.0f;
        float speedT = Mathf.Clamp(speed / Mathf.Max(1.0f, MaxReferenceSpeed), 0.0f, 1.0f);

        Vector3 localOffset = new Vector3(0.0f, BaseHeight + SpeedLift * speedT, -(BaseDistance + SpeedPullback * speedT));
        Vector3 targetPosition = _visualContainer.GlobalPosition + _visualContainer.GlobalTransform.Basis * localOffset;
        GlobalPosition = GlobalPosition.Lerp(targetPosition, followBlend);

        Vector3 forward = _visualContainer.GlobalTransform.Basis.Z.Normalized();
        Vector3 lookTarget = _visualContainer.GlobalPosition + Vector3.Up * LookHeight + forward * LookAheadDistance * speedT;

        float lateralSpeed = 0.0f;
        if (_targetBody != null)
        {
            Vector3 right = _visualContainer.GlobalTransform.Basis.X.Normalized();
            lateralSpeed = _targetBody.LinearVelocity.Dot(right);
        }

        Transform3D targetTransform = Transform.LookingAt(lookTarget, Vector3.Up);
        float rollRadians = Mathf.DegToRad(Mathf.Clamp(-lateralSpeed * RollResponse, -MaxRollDegrees, MaxRollDegrees));
        Basis targetBasis = targetTransform.Basis.Rotated(targetTransform.Basis.Z.Normalized(), rollRadians).Orthonormalized();

        Transform = new Transform3D(
            Transform.Basis.Orthonormalized().Slerp(targetBasis, lookBlend).Orthonormalized(),
            GlobalPosition
        );

        Fov = Mathf.Lerp(Fov, Mathf.Lerp(MinFov, MaxFov, speedT), fovBlend);
    }

    private bool HasValidTarget()
    {
        if (_targetVehicle == null || !GodotObject.IsInstanceValid(_targetVehicle) ||
            _cameraTarget == null || !GodotObject.IsInstanceValid(_cameraTarget) ||
            _visualContainer == null || !GodotObject.IsInstanceValid(_visualContainer))
        {
            ClearTarget();
            return false;
        }

        if (_targetBody != null && !GodotObject.IsInstanceValid(_targetBody))
            _targetBody = null;

        return true;
    }

    private void ClearTarget()
    {
        _targetVehicle = null;
        _cameraTarget = null;
        _visualContainer = null;
        _targetBody = null;
    }
}
