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
    [Export] public float VelocityHeadingWeight = 0.3f;
    [Export] public float DriftCameraOffset = 0.65f;

    [ExportGroup("Racing Feel")]
    [Export] public float MaxReferenceSpeed = 46.0f;
    [Export] public float MinFov = 63.0f;
    [Export] public float MaxFov = 78.0f;
    [Export] public float FovLerpSpeed = 5.0f;
    [Export] public float MaxRollDegrees = 4.0f;
    [Export] public float RollResponse = 0.144f;

    [ExportGroup("Impact Shake")]
    [Export] public float ShakeDecay = 1.8f;
    [Export] public float MaxShakeOffset = 0.22f;
    [Export] public float MaxShakeRollDegrees = 2.2f;

    private Node3D _targetVehicle;
    private Marker3D _cameraTarget;
    private Node3D _visualContainer;
    private RigidBody3D _targetBody;
    private bool _hasSnapped;
    private float _trauma;
    private float _shakeTime;

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
        _hasSnapped = false;

        if (_visualContainer == null)
            GD.PushWarning("TrackCamera expects the target vehicle to contain a VisualContainer child.");
    }

    public override void _Process(double delta)
    {
        if (HasValidTarget() == false) return;

        float dt = (float)delta;
        float followBlend = 1.0f - Mathf.Exp(-FollowSpeed * dt);
        float lookBlend = 1.0f - Mathf.Exp(-LookAtSpeed * dt);
        float fovBlend = 1.0f - Mathf.Exp(-FovLerpSpeed * dt);
        Vector3 planarVelocity = _targetBody?.LinearVelocity ?? Vector3.Zero;
        planarVelocity.Y = 0.0f;
        float speed = planarVelocity.Length();
        float speedT = Mathf.Clamp(speed / Mathf.Max(1.0f, MaxReferenceSpeed), 0.0f, 1.0f);

        Vector3 bodyForward = _visualContainer.GlobalTransform.Basis.Z;
        bodyForward.Y = 0.0f;
        bodyForward = bodyForward.LengthSquared() > 0.0001f ? bodyForward.Normalized() : Vector3.Back;

        Vector3 chaseForward = bodyForward;
        if (speed > 4.0f)
        {
            Vector3 travelDirection = planarVelocity.Normalized();
            if (travelDirection.Dot(bodyForward) > 0.15f)
                chaseForward = bodyForward.Slerp(travelDirection, VelocityHeadingWeight * speedT).Normalized();
        }

        Vector3 right = Vector3.Up.Cross(chaseForward).Normalized();
        float lateralSpeed = planarVelocity.Dot(right);
        float slipT = Mathf.Clamp(lateralSpeed / 15.0f, -1.0f, 1.0f);
        float distance = BaseDistance + SpeedPullback * speedT;
        float height = BaseHeight + SpeedLift * speedT;
        Vector3 targetPosition = _visualContainer.GlobalPosition + Vector3.Up * height - chaseForward * distance - right * slipT * DriftCameraOffset;
        Vector3 smoothPosition = _hasSnapped ? GlobalPosition.Lerp(targetPosition, followBlend) : targetPosition;

        Vector3 lookTarget = _visualContainer.GlobalPosition + Vector3.Up * LookHeight + chaseForward * LookAheadDistance * Mathf.Lerp(0.35f, 1.0f, speedT);
        Transform3D targetTransform = new Transform3D(Basis.Identity, smoothPosition).LookingAt(lookTarget, Vector3.Up);
        float rollRadians = Mathf.DegToRad(Mathf.Clamp(-lateralSpeed * RollResponse, -MaxRollDegrees, MaxRollDegrees));
        Basis targetBasis = targetTransform.Basis.Rotated(targetTransform.Basis.Z.Normalized(), rollRadians).Orthonormalized();

        _trauma = Mathf.Max(0.0f, _trauma - ShakeDecay * dt);
        _shakeTime += dt * 28.0f;
        float shake = _trauma * _trauma;
        Vector3 shakeOffset = right * (Mathf.Sin(_shakeTime * 1.7f) * MaxShakeOffset * shake) +
            Vector3.Up * (Mathf.Sin(_shakeTime * 2.3f) * MaxShakeOffset * 0.65f * shake);
        float shakeRoll = Mathf.DegToRad(Mathf.Sin(_shakeTime * 1.3f) * MaxShakeRollDegrees * shake);
        targetBasis = targetBasis.Rotated(targetBasis.Z.Normalized(), shakeRoll).Orthonormalized();

        GlobalTransform = new Transform3D(
            _hasSnapped ? GlobalTransform.Basis.Orthonormalized().Slerp(targetBasis, lookBlend).Orthonormalized() : targetBasis,
            smoothPosition + shakeOffset
        );

        float targetFov = Mathf.Lerp(MinFov, MaxFov, speedT);
        Fov = _hasSnapped ? Mathf.Lerp(Fov, targetFov, fovBlend) : targetFov;
        _hasSnapped = true;
    }

    public void AddTrauma(float amount)
    {
        _trauma = Mathf.Clamp(_trauma + amount, 0.0f, 1.0f);
    }

    private bool HasValidTarget()
    {
        if (_targetVehicle == null || !GodotObject.IsInstanceValid(_targetVehicle) ||
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
        _hasSnapped = false;
        _trauma = 0.0f;
    }
}
