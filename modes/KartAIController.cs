using Godot;
using System;
using System.Collections.Generic;

public partial class KartAIController : Node
{
    private Kart _kart;
    private float _stuckTimer = 0.0f;
    private float _reverseTimer = 0.0f;
    private float _speechTimer = 0.0f;
    private readonly List<Vector3> _route = new();
    private int _routeIndex;
    private Vector3 _lastObjective = new(float.PositiveInfinity, 0.0f, float.PositiveInfinity);
    private float _routeRefreshTimer;
    private Label3D _rivalLabel;

    public override void _Ready()
    {
        _kart = GetParent<Kart>();
        _speechTimer = (float)GD.RandRange(0.0, 10.0); // Offset speech timers

        _rivalLabel = new Label3D
        {
            Name = "RivalLabel",
            Text = "RIVAL",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 28,
            Modulate = _kart.OwnerPeerId % 2 == 0 ? new Color(1.0f, 0.08f, 0.52f) : new Color(1.0f, 0.75f, 0.16f),
            OutlineModulate = Colors.Black,
            Position = new Vector3(0.0f, 2.8f, 0.0f)
        };
        CallDeferred(nameof(AttachRivalLabel));
    }

    private void AttachRivalLabel()
    {
        if (_kart != null && GodotObject.IsInstanceValid(_kart) && _rivalLabel != null && !_rivalLabel.IsInsideTree())
            _kart.AddChild(_rivalLabel);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_kart == null || !GodotObject.IsInstanceValid(_kart))
            return;

        if (TaxiMode.Instance == null || !TaxiMode.Instance.MatchActive)
        {
            _kart.SetAIInput(0.0f, 0.0f);
            return;
        }

        Vector3 targetPos = Vector3.Zero;
        bool hasPassenger = _kart.ActivePassenger.HasValue;

        if (hasPassenger)
        {
            targetPos = TaxiMode.Instance.GetPlayerDestination(_kart.OwnerPeerId);
        }
        else
        {
            // Find nearest active pickup zone
            float nearestDist = float.MaxValue;
            PickupZone nearestZone = null;

            foreach (var child in TaxiMode.Instance.GetChildren())
            {
                if (child is PickupZone zone && GodotObject.IsInstanceValid(zone))
                {
                    float dist = _kart.GlobalPosition.DistanceSquaredTo(zone.GlobalPosition);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestZone = zone;
                    }
                }
            }

            if (nearestZone != null)
                targetPos = nearestZone.GlobalPosition;
        }

        if (targetPos == Vector3.Zero)
        {
            _kart.SetAIInput(0.0f, 0.0f);
            return;
        }

        float dt = (float)delta;
        _routeRefreshTimer -= dt;
        Vector3 currentPos = _kart.GlobalPosition;
        float distToTarget = currentPos.DistanceTo(targetPos);

        if (TrackBuilder.Instance != null &&
            (_routeRefreshTimer <= 0.0f || _lastObjective.DistanceSquaredTo(targetPos) > 1.0f || _routeIndex >= _route.Count))
        {
            _route.Clear();
            _route.AddRange(TrackBuilder.Instance.BuildStreetRoute(currentPos, targetPos));
            _routeIndex = 0;
            _lastObjective = targetPos;
            _routeRefreshTimer = 2.0f;
        }

        Vector3 steeringTarget = targetPos;
        while (_routeIndex < _route.Count)
        {
            steeringTarget = _route[_routeIndex];
            float waypointDistance = currentPos.DistanceTo(steeringTarget);
            bool finalWaypoint = _routeIndex == _route.Count - 1;
            if (waypointDistance > (finalWaypoint ? 3.5f : 5.0f))
                break;
            _routeIndex++;
        }

        if (_routeIndex < _route.Count)
            steeringTarget = _route[_routeIndex];
        else
            steeringTarget = targetPos;

        // 1. Calculate steering angle
        Vector3 toTarget = steeringTarget - currentPos;
        float targetAngle = Mathf.Atan2(toTarget.X, toTarget.Z);
        float angleDiff = WrapAngle(targetAngle - _kart.Rotation.Y);
        float steer = Mathf.Clamp(-angleDiff * 2.2f, -1.0f, 1.0f);

        // 2. Calculate throttle and braking
        float forward = 1.0f;
        if (Mathf.Abs(angleDiff) > 1.05f)
            forward = 0.15f;
        else if (Mathf.Abs(angleDiff) > 0.55f)
            forward = 0.45f;

        // Slow down to stop when close to target
        if (distToTarget < (hasPassenger ? 10.0f : 8.0f))
        {
            float stoppingSpeed = _kart.LinearVelocity.Length();
            forward = distToTarget < 4.0f ? (stoppingSpeed > 0.8f ? -0.35f : 0.0f) : 0.25f;
        }

        // 3. Stuck recovery system
        float speed = _kart.LinearVelocity.Length();
        if (_reverseTimer > 0.0f)
        {
            _reverseTimer -= dt;
            forward = -0.8f;
            steer = -steer;
        }
        else
        {
            if (forward > 0.1f && speed < 0.6f)
            {
                _stuckTimer += dt;
                if (_stuckTimer > 1.8f)
                {
                    _reverseTimer = 1.2f; // reverse for 1.2s
                    _stuckTimer = 0.0f;

                    if (GD.Randf() < 0.5f)
                        _kart.TriggerSpeechBubble("Move it!");
                }
            }
            else
            {
                _stuckTimer = Mathf.Max(0.0f, _stuckTimer - dt);
            }
        }

        // Random speech bubbles
        _speechTimer += dt;
        if (_speechTimer > 20.0f)
        {
            _speechTimer = 0.0f;
            if (GD.Randf() < 0.35f)
            {
                string[] phrases = {
                    "Outta my way!",
                    "This fare is mine!",
                    "Beep beep!",
                    "Pain Taxi coming through!",
                    "Out of my way!",
                    "Fasten your seatbelts!"
                };
                _kart.TriggerSpeechBubble(phrases[GD.RandRange(0, phrases.Length - 1)]);
            }
        }

        _kart.SetAIInput(forward, steer);
    }

    private float WrapAngle(float angle)
    {
        while (angle > Mathf.Pi) angle -= Mathf.Tau;
        while (angle < -Mathf.Pi) angle += Mathf.Tau;
        return angle;
    }
}
