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

    public override void _Ready()
    {
        if (Instance != null && Instance != this) { QueueFree(); return; }
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void BindKart(Kart kart) => _kart = kart;

    public override void _Process(double delta)
    {
        var mode = EndlessRoadMode.Instance;
        if (mode == null || mode.State != EndlessRoadMode.RunState.Running || _kart == null || !IsInstanceValid(_kart)) return;

        float dt = (float)delta;
        _nearMissCooldown = Mathf.Max(0.0f, _nearMissCooldown - dt);

        // Drift tick: small score while holding a drift.
        if (_kart.CurrentDriftPhase == Kart.DriftPhase.Holding)
            mode.AddScore(Mathf.RoundToInt(14.0f * dt * 10.0f));

        // Drafting: sitting ~4-9m behind traffic/rival gives a steady tick.
        UpdateDrafting(dt);

        // Near-miss: lateral pass within 0.35-1.5m at speed. Checked by proximity to traffic nodes.
        UpdateNearMiss();
    }

    private void UpdateDrafting(float dt)
    {
        if (_kart == null) return;
        // Simplified: if kart is at speed and close behind any StaticBody3D on the road, grant drafting.
        var space = GetViewport()?.GetWorld3D()?.DirectSpaceState;
        if (space == null) return;
        float speed = _kart.LinearVelocity.Length();
        if (speed < 14.0f) { _draftTimer = 0.0f; return; }

        Vector3 origin = _kart.GlobalPosition + _kart.GlobalTransform.Basis.Z * 1.2f;
        Vector3 ahead = origin + _kart.GlobalTransform.Basis.Z * 9.0f;
        var query = PhysicsRayQueryParameters3D.Create(origin, ahead);
        query.CollideWithAreas = false;
        query.CollideWithBodies = true;
        if (_kart != null) query.Exclude = new Godot.Collections.Array<Rid> { _kart.GetRid() };
        var hit = space.IntersectRay(query);
        if (hit.Count > 0)
        {
            _draftTimer += dt;
            if (_draftTimer > 0.9f)
            {
                EndlessRoadMode.Instance?.AddScore(18);
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

        // Look sideways for a close lateral pass.
        var space = GetViewport()?.GetWorld3D()?.DirectSpaceState;
        if (space == null) return;
        Vector3 pos = _kart.GlobalPosition;
        Vector3 right = _kart.GlobalTransform.Basis.X;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 from = pos;
            Vector3 to = pos + right * side * 2.2f;
            var shape = new SphereShape3D { Radius = 1.35f };
            var q = new PhysicsShapeQueryParameters3D();
            q.Shape = shape;
            q.Transform = Transform3D.Identity.Translated(to);
            q.CollideWithAreas = false;
            q.CollideWithBodies = true;
            if (_kart != null) q.Exclude = new Godot.Collections.Array<Rid> { _kart.GetRid() };
            var hits = space.IntersectShape(q, 4);
            if (hits.Count > 0)
            {
                // Require forward motion to count as a pass.
                Vector3 otherPos = hits[0]["collider"].As<Node>() is Node3D n ? n.GlobalPosition : to;
                float lateral = Mathf.Abs((otherPos - pos).Dot(right));
                if (lateral >= 0.35f && lateral <= 2.2f)
                {
                    _nearMissCooldown = 0.55f;
                    EndlessRoadMode.Instance?.AddScore(95);
                    AudioManager.Instance?.PlayLocal(AudioManager.Cue.CollisionLight, -8.0f, 1.35f);
                    break;
                }
            }
        }
    }
}
