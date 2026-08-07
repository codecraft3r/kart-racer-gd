using Godot;
using System;

/// <summary>
/// Spawns a single Rival behind the player and keeps it alive for the endless run.
/// Escalation is time-based inside EndlessRoadMode; this is just the spawner.
/// </summary>
public partial class EndlessRoadRivalDirector : Node
{
    public static EndlessRoadRivalDirector Instance { get; private set; }

    private Kart _kart;
    private EndlessRoadRival _rival;
    private float _nextSpawnCheck;
    private bool _hasSpawned;

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
        if (mode == null || _kart == null || !IsInstanceValid(_kart)) return;
        if (mode.State != EndlessRoadMode.RunState.Running) return;

        _nextSpawnCheck -= (float)delta;
        if (_nextSpawnCheck > 0.0f) return;
        _nextSpawnCheck = 1.0f;

        // First rival after ~20s of running, matching the tuning target.
        if (!_hasSpawned && mode.DistanceMeters > mode.Settings.OpeningSpeed * 20.0f)
        {
            SpawnRival(mode);
            _hasSpawned = true;
            return;
        }

        // Respawn if the rival fell off / was freed.
        if (_hasSpawned && (_rival == null || !IsInstanceValid(_rival)))
        {
            // Small cooldown before respawn.
            if (mode.DistanceMeters % 120.0f < 2.0f)
                SpawnRival(mode);
        }
        else if (_rival != null && IsInstanceValid(_rival))
        {
            float diff = Mathf.Clamp(mode.DistanceMeters / 900.0f, 0.0f, 1.0f);
            _rival.SetDifficulty(Mathf.Lerp(mode.Settings.RivalSkillStart, mode.Settings.RivalSkillMax, diff));
        }
    }

    private void SpawnRival(EndlessRoadMode mode)
    {
        if (_rival != null && IsInstanceValid(_rival)) _rival.QueueFree();
        _rival = new EndlessRoadRival();
        _rival.SetTarget(_kart);
        _rival.SetDifficulty(mode.Settings.RivalSkillStart);
        Vector3 spawnPos = _kart.GlobalPosition + new Vector3(0, 0.35f, -18.0f);
        // Nudge into adjacent lane so it never spawns on top of the player.
        spawnPos.X += mode.Settings.LaneWidth * (GD.Randf() < 0.5f ? 1 : -1);
        float half = mode.Settings.LaneCount * mode.Settings.LaneWidth * 0.5f;
        spawnPos.X = Mathf.Clamp(spawnPos.X, -half + 1.2f, half - 1.2f);
        GetTree().CurrentScene.AddChild(_rival);
        _rival.GlobalPosition = spawnPos;
        GD.Print($"EndlessRoad: rival spawned at {spawnPos} (dist {mode.DistanceMeters:F0}m)");
    }

    public void Clear()
    {
        if (_rival != null && IsInstanceValid(_rival)) _rival.QueueFree();
        _rival = null;
        _hasSpawned = false;
        _nextSpawnCheck = 0.0f;
    }
}
