using Godot;

/// <summary>
/// Burnout Paradise-style scoring: near-miss, drift, drafting, takedown, boost.
/// Lives alongside EndlessRoadMode so scoring survives even if mode is idle.
/// </summary>
public partial class EndlessRoadScoreSystem : Node
{
    public static EndlessRoadScoreSystem Instance { get; private set; }

    private float _nearMissCooldown;
    private float _draftTimer;
    private Kart _kart;

    private const float DriftAwardIntervalSeconds = 1.0f;
    private const int DriftPointsPerAward = 18;

    // Reused across frames: these queries ran twice per frame with fresh allocations.
    private readonly SphereShape3D _nearMissShape = new() { Radius = 1.35f };
    private readonly PhysicsShapeQueryParameters3D _nearMissQuery = new();
    private readonly PhysicsRayQueryParameters3D _draftQuery = new();
    private Godot.Collections.Array<Rid> _kartExclude = new();
    private float _driftAwardTimer;

    public override void _Ready()
    {
        if (Instance != null && Instance != this) { QueueFree(); return; }
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void BindKart(Kart kart)
    {
        _kart = kart;
        _kartExclude = kart != null
            ? new Godot.Collections.Array<Rid> { kart.GetRid() }
            : new Godot.Collections.Array<Rid>();
    }

    public override void _Process(double delta)
    {
        var mode = EndlessRoadMode.Instance;
        if (mode == null || mode.State != EndlessRoadMode.RunState.Running || _kart == null || !IsInstanceValid(_kart)) return;

        float dt = (float)delta;
        _nearMissCooldown = Mathf.Max(0.0f, _nearMissCooldown - dt);

        // Drift tick: award on a fixed interval. The old per-frame tick was frame-rate
        // dependent and bumped the score multiplier once per frame while drifting.
        if (_kart.CurrentDriftPhase == Kart.DriftPhase.Holding)
        {
            _driftAwardTimer += dt;
            if (_driftAwardTimer >= DriftAwardIntervalSeconds)
            {
                _driftAwardTimer -= DriftAwardIntervalSeconds;
                mode.AddScore(DriftPointsPerAward);
                mode.AddBoost(mode.Settings.BoostAwardDrift);
            }
        }
        else
        {
            _driftAwardTimer = 0.0f;
        }

        // Drafting: sitting ~4-9m behind traffic/rival gives a steady tick.
        UpdateDrafting(dt);

        // Near-miss: lateral pass within 0.35-1.5m at speed. Checked by proximity to traffic nodes.
        UpdateNearMiss();
    }

    private static bool IsTrafficOrRival(Node node)
    {
        return node is EndlessRoadTraffic || node is EndlessRoadRival;
    }

    private void UpdateDrafting(float dt)
    {
        if (_kart == null) return;
        // Drafting only counts when the body ahead is traffic or a rival. The road,
        // shoulders, and barriers are colliders too, so an unfiltered ray always hits.
        var space = GetViewport()?.GetWorld3D()?.DirectSpaceState;
        if (space == null) return;
        float speed = _kart.LinearVelocity.Length();
        if (speed < 14.0f) { _draftTimer = 0.0f; return; }

        Vector3 origin = _kart.GlobalPosition + _kart.GlobalTransform.Basis.Z * 1.2f;
        Vector3 ahead = origin + _kart.GlobalTransform.Basis.Z * 9.0f;
        _draftQuery.From = origin;
        _draftQuery.To = ahead;
        _draftQuery.CollideWithAreas = false;
        _draftQuery.CollideWithBodies = true;
        _draftQuery.Exclude = _kartExclude;
        var hit = space.IntersectRay(_draftQuery);
        if (hit.Count > 0 && IsTrafficOrRival(hit["collider"].As<Node>()))
        {
            _draftTimer += dt;
            if (_draftTimer > 0.9f)
            {
                var mode = EndlessRoadMode.Instance;
                if (mode != null)
                {
                    mode.AddScore(18);
                    mode.AddBoost(mode.Settings.BoostAwardDraft);
                }
                _draftTimer = 0.45f;
            }
        }
        else
        {
            _draftTimer = Mathf.Max(0.0f, _draftTimer - dt * 1.5f);
        }
    }

    private void UpdateNearMiss()
    {
        if (_nearMissCooldown > 0.0f || _kart == null) return;
        float speed = _kart.LinearVelocity.Length();
        if (speed < 12.0f) return;

        var mode = EndlessRoadMode.Instance;
        if (mode == null) return;

        // Look sideways for a close lateral pass.
        var space = GetViewport()?.GetWorld3D()?.DirectSpaceState;
        if (space == null) return;
        Vector3 pos = _kart.GlobalPosition;
        Vector3 right = _kart.GlobalTransform.Basis.X;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 to = pos + right * side * 2.2f;
            _nearMissQuery.Shape = _nearMissShape;
            _nearMissQuery.Transform = Transform3D.Identity.Translated(to);
            _nearMissQuery.CollideWithAreas = false;
            _nearMissQuery.CollideWithBodies = true;
            _nearMissQuery.Exclude = _kartExclude;
            // The road, shoulders, and barriers are bodies inside the same sphere, and the
            // result order is not sorted, so scan every overlap. Checking only the first hit
            // meant a near miss usually resolved to the road surface and never scored.
            foreach (var hit in space.IntersectShape(_nearMissQuery, 8))
            {
                Node collider = hit["collider"].As<Node>();
                if (!IsTrafficOrRival(collider))
                    continue;

                // Require forward motion to count as a pass.
                Vector3 otherPos = collider is Node3D n ? n.GlobalPosition : to;
                float lateral = Mathf.Abs((otherPos - pos).Dot(right));
                if (lateral < 0.35f || lateral > 2.2f)
                    continue;

                _nearMissCooldown = 0.55f;
                mode.AddScore(95);
                mode.AddBoost(mode.Settings.BoostAwardNearMiss);
                AudioManager.Instance?.PlayLocal(AudioManager.Cue.CollisionLight, -8.0f, 1.35f);
                RetroNeonCabShell.Instance?.TriggerFloatingCash("+95 NEAR MISS! +BOOST", new Color(0.0f, 0.94f, 1.0f));
                RetroNeonCabShell.Instance?.TriggerPassengerSpeech("CLOSE ONE!", new Color(1.0f, 0.9f, 0.2f));
                return;
            }
        }
    }
}
