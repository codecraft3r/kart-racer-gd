using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Spawns and maintains the rival pack behind the player. Both the count and the skill
/// of the pack escalate with distance travelled.
/// </summary>
public partial class EndlessRoadRivalDirector : Node
{
    private const float SkillRampDistance = 900.0f;
    private const float FirstRivalDistanceFactor = 20.0f;

    public static EndlessRoadRivalDirector Instance { get; private set; }

    private readonly List<EndlessRoadRival> _rivals = new();
    private Kart _kart;
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

        PruneRivals();

        // First rival after ~20s of running, matching the tuning target.
        if (!_hasSpawned && mode.DistanceMeters > mode.Settings.OpeningSpeed * FirstRivalDistanceFactor)
        {
            SpawnRival(mode);
            _hasSpawned = true;
        }

        if (!_hasSpawned)
            return;

        // Count and skill share one ramp so the pack grows and sharpens together. Checking the
        // count also covers a rival that fell off or was freed.
        float diff = Mathf.Clamp(mode.DistanceMeters / SkillRampDistance, 0.0f, 1.0f);
        int maxRivals = Mathf.Max(1, mode.Settings.MaxRivals);
        int targetCount = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(mode.Settings.StartingRivals, maxRivals, diff)),
            1,
            maxRivals);

        if (_rivals.Count < targetCount)
            SpawnRival(mode);

        float skill = Mathf.Lerp(mode.Settings.RivalSkillStart, mode.Settings.RivalSkillMax, diff);
        for (int index = 0; index < _rivals.Count; index++)
            _rivals[index].SetDifficulty(skill);
    }

    private void PruneRivals()
    {
        for (int index = _rivals.Count - 1; index >= 0; index--)
        {
            EndlessRoadRival rival = _rivals[index];
            if (rival == null || !IsInstanceValid(rival))
                _rivals.RemoveAt(index);
        }
    }

    private void SpawnRival(EndlessRoadMode mode)
    {
        int slot = _rivals.Count;
        var rival = new EndlessRoadRival();
        rival.SetTarget(_kart);
        rival.SetDifficulty(mode.Settings.RivalSkillStart);

        Vector3 spawnPos = _kart.GlobalPosition + new Vector3(0, 0.35f, -18.0f);
        // Fan the pack across adjacent lanes and stagger it so no two spawn on top of each other.
        float laneOffset = mode.Settings.LaneWidth * (slot % 2 == 0 ? 1.0f : -1.0f) * (1 + slot / 2);
        spawnPos.X += laneOffset;
        spawnPos.Z -= slot * 4.0f;
        float half = mode.Settings.LaneCount * mode.Settings.LaneWidth * 0.5f;
        spawnPos.X = Mathf.Clamp(spawnPos.X, -half + 1.2f, half - 1.2f);

        GetTree().CurrentScene.AddChild(rival);
        rival.GlobalPosition = spawnPos;
        _rivals.Add(rival);
        GD.Print($"EndlessRoad: rival {slot + 1} spawned at {spawnPos} (dist {mode.DistanceMeters:F0}m)");
    }

    public void Clear()
    {
        for (int index = 0; index < _rivals.Count; index++)
        {
            EndlessRoadRival rival = _rivals[index];
            if (rival != null && IsInstanceValid(rival))
                rival.QueueFree();
        }

        _rivals.Clear();
        _hasSpawned = false;
        _nextSpawnCheck = 0.0f;
    }
}
