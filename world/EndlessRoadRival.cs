using Godot;
using System;

/// <summary>
/// A Burnout Paradise-style rival that shadows and tries to ram the player.
/// Telegraphs its ram (indicator + audio), then commits. Uses the same
/// Kart physics so it collides fairly — no teleporting into your lane.
/// </summary>
public partial class EndlessRoadRival : RigidBody3D
{
    public enum RivalState { Approach, Shadow, TelegraphRam, Ram, Recover, Disabled }

    [Export] public float BaseSpeed = 22.0f;
    [Export] public float Aggression = 0.35f;

    public RivalState State { get; private set; } = RivalState.Approach;

    private Node3D _visual;
    private Kart _target;
    private float _stateTimer;
    private float _ramCooldown;
    private int _lane = 2;
    private MeshInstance3D _telegraphMesh;
    private RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _rng.Randomize();
        Mass = 155.0f;
        ContinuousCd = true;
        ContactMonitor = true;
        MaxContactsReported = 4;
        BodyEntered += OnBodyEntered;
        BuildVisual();
    }

    public void SetTarget(Kart kart) => _target = kart;

    public void SetDifficulty(float t)
    {
        t = Mathf.Clamp(t, 0.0f, 1.0f);
        BaseSpeed = Mathf.Lerp(21.0f, 29.0f, t);
        Aggression = Mathf.Lerp(0.25f, 0.78f, t);
    }

    private void BuildVisual()
    {
        _visual = new Node3D { Name = "RivalVisual" };
        AddChild(_visual);
        var body = new MeshInstance3D { Name = "RivalBody", Mesh = new BoxMesh { Size = new Vector3(1.55f, 0.95f, 2.9f) } };
        body.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.12f, 0.12f), Roughness = 0.65f, Metallic = 0.15f };
        _visual.AddChild(body);
        AddChild(new CollisionShape3D { Name = "RivalCol", Shape = new BoxShape3D { Size = new Vector3(1.55f, 0.95f, 2.9f) }, Position = new Vector3(0, 0.35f, 0) });
        _telegraphMesh = new MeshInstance3D { Name = "Telegraph", Mesh = new BoxMesh { Size = new Vector3(1.8f, 0.12f, 0.4f) }, Position = new Vector3(0, 1.35f, 0.9f), Visible = false };
        _telegraphMesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1, 0.2f, 0.2f), EmissionEnabled = true, Emission = new Color(1, 0.15f, 0.15f) * 1.2f };
        _visual.AddChild(_telegraphMesh);
        var light = new SpotLight3D { Name = "RivalHeadlight", LightColor = new Color(1, 0.95f, 0.7f), LightEnergy = 1.6f, SpotRange = 18.0f, SpotAngle = 22.0f, Position = new Vector3(0, 0.5f, 1.35f) };
        _visual.AddChild(light);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target == null || !IsInstanceValid(_target)) return;
        var mode = EndlessRoadMode.Instance;
        if (mode == null || mode.State != EndlessRoadMode.RunState.Running) return;

        float dt = (float)delta;
        _ramCooldown = Mathf.Max(0.0f, _ramCooldown - dt);
        _stateTimer -= dt;

        Vector3 toTarget = _target.GlobalPosition - GlobalPosition;
        float dist = toTarget.Length();
        float laneWidth = mode.Settings.LaneWidth;
        float roadHalf = mode.Settings.LaneCount * laneWidth * 0.5f;

        switch (State)
        {
            case RivalState.Approach:
                DriveToward(_target.GlobalPosition + new Vector3(0, 0, -6.0f), dt);
                if (dist < 18.0f) Transition(RivalState.Shadow, 2.5f + (float)_rng.RandfRange(0.8f, 2.2f));
                break;

            case RivalState.Shadow:
                // Stay in adjacent lane, matching speed.
                float targetX = Mathf.Clamp(_target.GlobalPosition.X + Mathf.Sign(GlobalPosition.X - _target.GlobalPosition.X + 0.5f) * laneWidth, -roadHalf + 1.2f, roadHalf - 1.2f);
                DriveToward(new Vector3(targetX, GlobalPosition.Y, _target.GlobalPosition.Z - 2.5f), dt);
                if (_stateTimer <= 0.0f && _ramCooldown <= 0.0f && dist < 14.0f && _rng.Randf() < Aggression * 0.45f)
                    Transition(RivalState.TelegraphRam, 0.85f);
                else if (_stateTimer <= 0.0f)
                    Transition(RivalState.Shadow, 2.0f + (float)_rng.RandfRange(0.5f, 2.5f));
                break;

            case RivalState.TelegraphRam:
                _telegraphMesh.Visible = true;
                DriveToward(_target.GlobalPosition, dt, 1.15f);
                if (_stateTimer <= 0.0f)
                {
                    _telegraphMesh.Visible = false;
                    AudioManager.Instance?.PlayLocal(AudioManager.Cue.Warning, -4.0f, 1.05f);
                    Transition(RivalState.Ram, 0.55f);
                }
                break;

            case RivalState.Ram:
                DriveToward(_target.GlobalPosition, dt, 1.55f);
                if (_stateTimer <= 0.0f)
                {
                    _ramCooldown = 4.5f;
                    Transition(RivalState.Recover, 1.2f);
                }
                break;

            case RivalState.Recover:
                DriveToward(GlobalPosition + Vector3.Back * 6.0f, dt, 0.65f);
                if (_stateTimer <= 0.0f) Transition(RivalState.Shadow, 1.5f);
                break;

            case RivalState.Disabled:
                if (_stateTimer <= 0.0f) Transition(RivalState.Approach, 1.0f);
                break;
        }
    }

    private void DriveToward(Vector3 targetPos, float dt, float speedMul = 1.0f)
    {
        Vector3 flat = targetPos - GlobalPosition;
        flat.Y = 0.0f;
        if (flat.LengthSquared() < 0.001f) return;
        Vector3 dir = flat.Normalized();
        float speed = BaseSpeed * speedMul;
        Vector3 desiredVel = dir * speed;
        desiredVel.Y = LinearVelocity.Y;
        Vector3 steer = (desiredVel - LinearVelocity) * 2.2f;
        ApplyCentralForce(steer * Mass * 0.9f);
        // Yaw toward movement
        if (LinearVelocity.LengthSquared() > 1.0f)
        {
            float yaw = Mathf.Atan2(LinearVelocity.X, LinearVelocity.Z);
            Rotation = new Vector3(Rotation.X, Mathf.LerpAngle(Rotation.Y, yaw, dt * 4.0f), Rotation.Z);
        }
    }

    private void Transition(RivalState next, float duration)
    {
        State = next;
        _stateTimer = duration;
        if (next != RivalState.TelegraphRam)
            _telegraphMesh.Visible = false;
    }

    private void OnBodyEntered(Node body)
    {
        if (body is Kart kart && kart == _target)
        {
            // Failed ram — player gets a takedown window if rival hits barrier next.
            EndlessRoadMode.Instance?.AddScore(120);
            _ramCooldown = 3.0f;
            Transition(RivalState.Recover, 1.4f);
            AudioManager.Instance?.PlayLocal(AudioManager.Cue.CollisionMedium, -2.0f, 1.0f);
        }
        else if (body is StaticBody3D && State == RivalState.Ram)
        {
            // Rival ate a wall on its own ram — big takedown.
            EndlessRoadMode.Instance?.AddScore(450);
            AudioManager.Instance?.PlayLocal(AudioManager.Cue.CollisionHeavy, -1.0f, 0.95f);
            Transition(RivalState.Disabled, 3.5f);
            ApplyCentralImpulse(new Vector3((float)GD.RandRange(-1, 1), 0.6f, -1) * 260.0f);
        }
    }
}
