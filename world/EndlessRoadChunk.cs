using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Represents one fixed-length segment of the endless forward road.
/// </summary>
public partial class EndlessRoadChunk : Node3D
{
    public int ChunkIndex { get; set; }
    public float RoadWidth { get; private set; }
    public Vector3 EntryPoint => new(0.0f, 0.0f, ChunkIndex * 80.0f);
    public Vector3 ExitPoint => new(0.0f, 0.0f, (ChunkIndex + 1) * 80.0f);
    public float CenterZ => ChunkIndex * 80.0f + 40.0f;

    private EndlessRoadSettings _settings;
    private RandomNumberGenerator _rng;
    private StaticBody3D _roadBody;

    public override void _Ready()
    {
        if (_settings == null)
            _settings = EndlessRoadMode.Instance?.Settings ?? new EndlessRoadSettings();
    }

    public void Initialize(EndlessRoadSettings settings, RandomNumberGenerator rng)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _rng = rng ?? new RandomNumberGenerator();
        // Derive a per-chunk RNG so layout is deterministic for a given (RunSeed, ChunkIndex).
        var chunkRng = new RandomNumberGenerator();
        chunkRng.Seed = (ulong)(_settings.RunSeed + ChunkIndex * 7919L);
        _rng = chunkRng;
        RoadWidth = _settings.LaneCount * _settings.LaneWidth;
        GenerateRoadMesh();
        PopulateChunk();
    }

    private void GenerateRoadMesh()
    {
        _roadBody = new StaticBody3D { Name = "RoadBody" };
        var shape = new BoxShape3D { Size = new Vector3(RoadWidth, 0.5f, _settings.ChunkLength) };
        var col = new CollisionShape3D { Shape = shape, Position = new Vector3(0.0f, -0.25f, _settings.ChunkLength * 0.5f) };
        _roadBody.AddChild(col);
        AddChild(_roadBody);

        var roadMesh = new MeshInstance3D { Name = "RoadMesh" };
        roadMesh.Mesh = new BoxMesh { Size = new Vector3(RoadWidth, 0.05f, _settings.ChunkLength) };
        roadMesh.Position = new Vector3(0.0f, -0.025f, _settings.ChunkLength * 0.5f);
        roadMesh.MaterialOverride = CreateRoadMaterial();
        AddChild(roadMesh);

        GenerateLaneMarkers();
        GenerateShoulders();
        GenerateBarriers();
    }

    private void GenerateLaneMarkers()
    {
        for (int lane = 1; lane < _settings.LaneCount; lane++)
        {
            float x = -RoadWidth * 0.5f + lane * _settings.LaneWidth;
            var marker = new MeshInstance3D { Name = $"LaneMarker{lane}" };
            marker.Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.02f, _settings.ChunkLength) };
            marker.Position = new Vector3(x, 0.02f, _settings.ChunkLength * 0.5f);
            marker.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(1.0f, 0.92f, 0.2f),
                EmissionEnabled = true,
                Emission = new Color(1.0f, 0.85f, 0.15f) * 0.45f,
                Roughness = 0.7f
            };
            AddChild(marker);
        }
    }

    private void GenerateShoulders()
    {
        float shoulderWidth = 1.25f;
        float shoulderY = 0.01f;

        var leftShoulder = new MeshInstance3D { Name = "LeftShoulder" };
        leftShoulder.Mesh = new BoxMesh { Size = new Vector3(shoulderWidth, 0.05f, _settings.ChunkLength) };
        leftShoulder.Position = new Vector3(-RoadWidth * 0.5f - shoulderWidth * 0.5f, shoulderY, _settings.ChunkLength * 0.5f);
        leftShoulder.MaterialOverride = CreateShoulderMaterial();
        AddChild(leftShoulder);

        var rightShoulder = new MeshInstance3D { Name = "RightShoulder" };
        rightShoulder.Mesh = new BoxMesh { Size = new Vector3(shoulderWidth, 0.05f, _settings.ChunkLength) };
        rightShoulder.Position = new Vector3(RoadWidth * 0.5f + shoulderWidth * 0.5f, shoulderY, _settings.ChunkLength * 0.5f);
        rightShoulder.MaterialOverride = CreateShoulderMaterial();
        AddChild(rightShoulder);
    }

    private void GenerateBarriers()
    {
        float barrierH = 0.9f;
        float barrierW = 0.35f;
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.08f, 0.2f), Roughness = 0.75f, Metallic = 0.1f };
        float leftX = -RoadWidth * 0.5f - 1.25f - barrierW * 0.5f;
        float rightX = RoadWidth * 0.5f + 1.25f + barrierW * 0.5f;
        var left = new MeshInstance3D { Name = "LeftBarrier" };
        left.Mesh = new BoxMesh { Size = new Vector3(barrierW, barrierH, _settings.ChunkLength) };
        left.Position = new Vector3(leftX, barrierH * 0.5f, _settings.ChunkLength * 0.5f);
        left.MaterialOverride = mat;
        AddChild(left);
        var right = new MeshInstance3D { Name = "RightBarrier" };
        right.Mesh = new BoxMesh { Size = new Vector3(barrierW, barrierH, _settings.ChunkLength) };
        right.Position = new Vector3(rightX, barrierH * 0.5f, _settings.ChunkLength * 0.5f);
        right.MaterialOverride = mat;
        AddChild(right);
        var lbBody = new StaticBody3D { Name = "LeftBarrierBody", Position = left.Position };
        lbBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(barrierW, barrierH, _settings.ChunkLength) } });
        AddChild(lbBody);
        var rbBody = new StaticBody3D { Name = "RightBarrierBody", Position = right.Position };
        rbBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(barrierW, barrierH, _settings.ChunkLength) } });
        AddChild(rbBody);
    }

    private StandardMaterial3D CreateRoadMaterial()
    {
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(0.035f, 0.04f, 0.08f),
            Roughness = 0.92f,
            Metallic = 0.05f
        };
    }

    private StandardMaterial3D CreateShoulderMaterial()
    {
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.09f, 0.14f),
            Roughness = 0.9f,
            Metallic = 0.05f
        };
    }

    private void PopulateChunk()
    {
        // First chunk is always clean (safe start).
        if (ChunkIndex == 0) return;

        // Template-based placement: never block every lane, keep reaction distance.
        float chunkStartZ = ChunkIndex * _settings.ChunkLength;
        int laneCount = _settings.LaneCount;
        float laneW = _settings.LaneWidth;

        // Pick a template.
        int template = _rng.RandiRange(0, 5);
        // 0: single block, 1: two-lane gate, 2: staggered traffic, 3: opening + reward, 4: wreck chicane, 5: hazard strip
        var occupied = new HashSet<int>();

        if (template == 0)
        {
            int lane = _rng.RandiRange(1, laneCount - 2);
            SpawnTraffic(lane, 0.35f, EndlessRoadTraffic.TrafficKind.Civilian);
            occupied.Add(lane);
        }
        else if (template == 1)
        {
            int a = _rng.RandiRange(0, laneCount - 2);
            SpawnTraffic(a, 0.30f, EndlessRoadTraffic.TrafficKind.Civilian);
            SpawnTraffic(a + 1, 0.38f, EndlessRoadTraffic.TrafficKind.Wreck);
            occupied.Add(a); occupied.Add(a + 1);
        }
        else if (template == 2)
        {
            int a = _rng.RandiRange(0, laneCount - 2);
            SpawnTraffic(a, 0.22f, EndlessRoadTraffic.TrafficKind.Civilian);
            SpawnTraffic(a + 1, 0.62f, EndlessRoadTraffic.TrafficKind.Civilian);
            occupied.Add(a);
        }
        else if (template == 3)
        {
            int blocked = _rng.RandiRange(0, laneCount - 2);
            SpawnTraffic(blocked, 0.40f, EndlessRoadTraffic.TrafficKind.Barricade);
            // Leave an opening lane and put a pickup there.
            int freeLane = (blocked + 2) % laneCount;
            SpawnPickup(freeLane, 0.42f, EndlessRoadPickup.PickupKind.Boost);
            occupied.Add(blocked);
        }
        else if (template == 4)
        {
            int lane = _rng.RandiRange(1, laneCount - 2);
            SpawnTraffic(lane, 0.28f, EndlessRoadTraffic.TrafficKind.Wreck);
            SpawnTraffic(lane, 0.62f, EndlessRoadTraffic.TrafficKind.Debris);
            occupied.Add(lane);
        }
        else
        {
            int lane = _rng.RandiRange(0, laneCount - 1);
            SpawnHazard(lane, 0.50f, _rng.RandiRange(0, 1) == 0 ? EndlessRoadHazard.HazardKind.OilSlick : EndlessRoadHazard.HazardKind.Ramp);
            // Ensure at least one adjacent lane stays clear by not filling neighbors with traffic this chunk.
            occupied.Add(lane);
        }

        // Second pass: occasional extra civilian, never filling the last free lane.
        if (_rng.Randf() < 0.55f)
        {
            var free = new System.Collections.Generic.List<int>();
            for (int l = 0; l < laneCount; l++) if (!occupied.Contains(l)) free.Add(l);
            if (free.Count >= 2)
            {
                int extra = free[_rng.RandiRange(0, free.Count - 1)];
                SpawnTraffic(extra, (float)_rng.RandfRange(0.18f, 0.75f), EndlessRoadTraffic.TrafficKind.Civilian);
            }
        }

        // Occasional score pickup in a free lane.
        if (_rng.Randf() < 0.32f)
        {
            var free2 = new System.Collections.Generic.List<int>();
            for (int l = 0; l < laneCount; l++) if (!occupied.Contains(l)) free2.Add(l);
            if (free2.Count > 0)
            {
                int lane = free2[_rng.RandiRange(0, free2.Count - 1)];
                var kind = _rng.Randf() < 0.5f ? EndlessRoadPickup.PickupKind.Score : EndlessRoadPickup.PickupKind.Repair;
                SpawnPickup(lane, (float)_rng.RandfRange(0.25f, 0.78f), kind);
            }
        }
    }

    private float LaneToX(int lane) => -RoadWidth * 0.5f + lane * _settings.LaneWidth + _settings.LaneWidth * 0.5f;

    private void SpawnTraffic(int lane, float tAlong, EndlessRoadTraffic.TrafficKind kind)
    {
        float z = tAlong * _settings.ChunkLength;
        var node = new EndlessRoadTraffic();
        float speed = kind == EndlessRoadTraffic.TrafficKind.Civilian ? (float)_rng.RandfRange(9.0f, 16.0f) : 0.0f;
        Color color = kind switch
        {
            EndlessRoadTraffic.TrafficKind.Wreck => new Color(0.35f, 0.35f, 0.38f),
            EndlessRoadTraffic.TrafficKind.Barricade => new Color(0.92f, 0.78f, 0.18f),
            EndlessRoadTraffic.TrafficKind.Debris => new Color(0.45f, 0.33f, 0.22f),
            _ => new Color((float)_rng.RandfRange(0.35f, 0.85f), (float)_rng.RandfRange(0.35f, 0.85f), (float)_rng.RandfRange(0.35f, 0.85f))
        };
        Vector3 size = kind switch
        {
            EndlessRoadTraffic.TrafficKind.Barricade => new Vector3(2.4f, 1.0f, 1.1f),
            EndlessRoadTraffic.TrafficKind.Debris => new Vector3(1.4f, 0.55f, 1.6f),
            EndlessRoadTraffic.TrafficKind.Wreck => new Vector3(1.9f, 1.15f, 3.6f),
            _ => new Vector3(1.75f, 1.05f, 3.2f)
        };
        node.Configure(kind, lane, speed, color, size);
        AddChild(node);
        node.Position = new Vector3(LaneToX(lane), 0.45f, z);
    }

    private void SpawnPickup(int lane, float tAlong, EndlessRoadPickup.PickupKind kind)
    {
        float z = tAlong * _settings.ChunkLength;
        var node = new EndlessRoadPickup();
        node.Configure(kind, kind == EndlessRoadPickup.PickupKind.Score ? 300 : 250);
        AddChild(node);
        node.Position = new Vector3(LaneToX(lane), 0.35f, z);
    }

    private void SpawnHazard(int lane, float tAlong, EndlessRoadHazard.HazardKind kind)
    {
        float z = tAlong * _settings.ChunkLength;
        var node = new EndlessRoadHazard();
        node.Configure(kind);
        AddChild(node);
        node.Position = new Vector3(LaneToX(lane), 0.02f, z);
    }
}
