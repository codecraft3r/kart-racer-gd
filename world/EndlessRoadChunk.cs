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
    public Vector3 EntryPoint => new(0.0f, 0.0f, ChunkIndex * ChunkLength);
    public Vector3 ExitPoint => new(0.0f, 0.0f, (ChunkIndex + 1) * ChunkLength);
    public float CenterZ => ChunkIndex * ChunkLength + ChunkLength * 0.5f;

    private EndlessRoadSettings _settings;
    private RandomNumberGenerator _rng;
    private StaticBody3D _roadBody;

    private float ChunkLength => _settings?.ChunkLength ?? 80.0f;

    public override void _Ready()
    {
        if (_settings == null)
            _settings = EndlessRoadMode.Instance?.Settings ?? new EndlessRoadSettings();
    }

    public void Initialize(EndlessRoadSettings settings, int runSeed)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        // Derive a per-chunk RNG so layout is deterministic for a given (runSeed, ChunkIndex)
        // no matter when the chunk is streamed in. The cast keeps negative seeds usable.
        var chunkRng = new RandomNumberGenerator();
        chunkRng.Seed = (ulong)(uint)runSeed * 2654435761UL + (ulong)(uint)ChunkIndex * 7919UL;
        _rng = chunkRng;
        RoadWidth = _settings.LaneCount * _settings.LaneWidth;
        GenerateRoadMesh();
        PopulateChunk();
    }

    /// <summary>
    /// Chunk meshes, shapes, and materials are identical for a given road size, so they are
    /// built once and shared. A chunk used to allocate nine meshes, three shapes, and a
    /// material per lane, and that churn fired on every stream boundary.
    /// </summary>
    private sealed class ChunkGeometry
    {
        public float RoadWidth;
        public float ChunkLength;
        public int LaneCount;
        public float LaneWidth;
        public BoxShape3D RoadShape;
        public BoxMesh RoadMesh;
        public BoxMesh LaneMarkerMesh;
        public BoxMesh ShoulderMesh;
        public BoxMesh BarrierMesh;
        public BoxShape3D BarrierShape;
        public StandardMaterial3D RoadMaterial;
        public StandardMaterial3D LaneMarkerMaterial;
        public StandardMaterial3D ShoulderMaterial;
        public StandardMaterial3D BarrierMaterial;
    }

    private static ChunkGeometry _sharedGeometry;

    private ChunkGeometry SharedGeometry
    {
        get
        {
            ChunkGeometry cached = _sharedGeometry;
            if (cached != null &&
                Mathf.IsEqualApprox(cached.RoadWidth, RoadWidth) &&
                Mathf.IsEqualApprox(cached.ChunkLength, _settings.ChunkLength) &&
                cached.LaneCount == _settings.LaneCount &&
                Mathf.IsEqualApprox(cached.LaneWidth, _settings.LaneWidth))
            {
                return cached;
            }

            const float shoulderWidth = 1.25f;
            const float barrierHeight = 0.9f;
            const float barrierWidth = 0.35f;
            float roadWidth = RoadWidth;
            float chunkLength = _settings.ChunkLength;

            var geometry = new ChunkGeometry
            {
                RoadWidth = roadWidth,
                ChunkLength = chunkLength,
                LaneCount = _settings.LaneCount,
                LaneWidth = _settings.LaneWidth,
                RoadShape = new BoxShape3D { Size = new Vector3(roadWidth, 0.5f, chunkLength) },
                RoadMesh = new BoxMesh { Size = new Vector3(roadWidth, 0.05f, chunkLength) },
                LaneMarkerMesh = new BoxMesh { Size = new Vector3(0.18f, 0.02f, chunkLength) },
                ShoulderMesh = new BoxMesh { Size = new Vector3(shoulderWidth, 0.05f, chunkLength) },
                BarrierMesh = new BoxMesh { Size = new Vector3(barrierWidth, barrierHeight, chunkLength) },
                BarrierShape = new BoxShape3D { Size = new Vector3(barrierWidth, barrierHeight, chunkLength) },
                RoadMaterial = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.035f, 0.04f, 0.08f),
                    Roughness = 0.92f,
                    Metallic = 0.05f
                },
                LaneMarkerMaterial = new StandardMaterial3D
                {
                    AlbedoColor = new Color(1.0f, 0.92f, 0.2f),
                    EmissionEnabled = true,
                    Emission = new Color(1.0f, 0.85f, 0.15f) * 0.45f,
                    Roughness = 0.7f
                },
                ShoulderMaterial = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.08f, 0.09f, 0.14f),
                    Roughness = 0.9f,
                    Metallic = 0.05f
                },
                BarrierMaterial = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.85f, 0.08f, 0.2f),
                    Roughness = 0.75f,
                    Metallic = 0.1f
                }
            };

            _sharedGeometry = geometry;
            return geometry;
        }
    }
    private void GenerateRoadMesh()
    {
        ChunkGeometry geometry = SharedGeometry;
        _roadBody = new StaticBody3D { Name = "RoadBody" };
        _roadBody.AddChild(new CollisionShape3D
        {
            Shape = geometry.RoadShape,
            Position = new Vector3(0.0f, -0.25f, _settings.ChunkLength * 0.5f)
        });
        AddChild(_roadBody);

        var roadMesh = new MeshInstance3D { Name = "RoadMesh" };
        roadMesh.Mesh = geometry.RoadMesh;
        roadMesh.Position = new Vector3(0.0f, -0.025f, _settings.ChunkLength * 0.5f);
        roadMesh.MaterialOverride = geometry.RoadMaterial;
        AddChild(roadMesh);

        GenerateLaneMarkers(geometry);
        GenerateShoulders(geometry);
        GenerateBarriers(geometry);
    }

    private void GenerateLaneMarkers(ChunkGeometry geometry)
    {
        for (int lane = 1; lane < _settings.LaneCount; lane++)
        {
            float x = -RoadWidth * 0.5f + lane * _settings.LaneWidth;
            var marker = new MeshInstance3D { Name = $"LaneMarker{lane}" };
            marker.Mesh = geometry.LaneMarkerMesh;
            marker.Position = new Vector3(x, 0.02f, _settings.ChunkLength * 0.5f);
            marker.MaterialOverride = geometry.LaneMarkerMaterial;
            AddChild(marker);
        }
    }

    private void GenerateShoulders(ChunkGeometry geometry)
    {
        float shoulderWidth = 1.25f;
        float shoulderY = 0.01f;

        var leftShoulder = new MeshInstance3D { Name = "LeftShoulder" };
        leftShoulder.Mesh = geometry.ShoulderMesh;
        leftShoulder.Position = new Vector3(-RoadWidth * 0.5f - shoulderWidth * 0.5f, shoulderY, _settings.ChunkLength * 0.5f);
        leftShoulder.MaterialOverride = geometry.ShoulderMaterial;
        AddChild(leftShoulder);

        var rightShoulder = new MeshInstance3D { Name = "RightShoulder" };
        rightShoulder.Mesh = geometry.ShoulderMesh;
        rightShoulder.Position = new Vector3(RoadWidth * 0.5f + shoulderWidth * 0.5f, shoulderY, _settings.ChunkLength * 0.5f);
        rightShoulder.MaterialOverride = geometry.ShoulderMaterial;
        AddChild(rightShoulder);
    }

    private void GenerateBarriers(ChunkGeometry geometry)
    {
        float barrierH = 0.9f;
        float barrierW = 0.35f;
        float leftX = -RoadWidth * 0.5f - 1.25f - barrierW * 0.5f;
        float rightX = RoadWidth * 0.5f + 1.25f + barrierW * 0.5f;
        var left = new MeshInstance3D { Name = "LeftBarrier" };
        left.Mesh = geometry.BarrierMesh;
        left.Position = new Vector3(leftX, barrierH * 0.5f, _settings.ChunkLength * 0.5f);
        left.MaterialOverride = geometry.BarrierMaterial;
        AddChild(left);
        var right = new MeshInstance3D { Name = "RightBarrier" };
        right.Mesh = geometry.BarrierMesh;
        right.Position = new Vector3(rightX, barrierH * 0.5f, _settings.ChunkLength * 0.5f);
        right.MaterialOverride = geometry.BarrierMaterial;
        AddChild(right);
        var lbBody = new StaticBody3D { Name = "LeftBarrierBody", Position = left.Position };
        lbBody.AddChild(new CollisionShape3D { Shape = geometry.BarrierShape });
        AddChild(lbBody);
        var rbBody = new StaticBody3D { Name = "RightBarrierBody", Position = right.Position };
        rbBody.AddChild(new CollisionShape3D { Shape = geometry.BarrierShape });
        AddChild(rbBody);
    }


    private void PopulateChunk()
    {
        using var perf = PerfProbe.Measure(PerfHotspot.EndlessChunkPopulate);
        // First chunk is always clean (safe start).
        if (ChunkIndex == 0) return;

        // Template-based placement: never block every lane, keep reaction distance.
        float chunkStartZ = ChunkIndex * _settings.ChunkLength;
        int laneCount = _settings.LaneCount;

        // Hazards stay out of the opening stretch. Chunks stream in before the player reaches
        // them, so the authored delay becomes a distance at the opening speed; that keeps a
        // chunk's layout identical for a given (runSeed, ChunkIndex).
        float hazardGate = Mathf.Max(
            _settings.FirstHazardMinimumDistance,
            _settings.OpeningSpeed * _settings.FirstHazardDelay);
        bool hazardsAllowed = chunkStartZ >= hazardGate;

        // Pick a template.
        int template = _rng.RandiRange(0, 5);
        if (template == 5 && !hazardsAllowed)
            template = _rng.RandiRange(0, 4);
        // 0: single block, 1: two-lane gate, 2: staggered traffic, 3: opening + reward, 4: wreck chicane, 5: hazard strip
        var occupied = new HashSet<int>();
        var hazardLanes = new HashSet<int>();

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
            hazardLanes.Add(lane);
        }

        // Traffic density escalates with distance travelled. The authored per-chunk counts are
        // the target, and the "never fill the last free lane" rule below still holds.
        var freeLanes = new System.Collections.Generic.List<int>();
        for (int l = 0; l < laneCount; l++)
            if (!occupied.Contains(l)) freeLanes.Add(l);

        int targetTraffic = Mathf.RoundToInt(Mathf.Lerp(
            _settings.StartingTrafficPerChunk,
            _settings.MaxTrafficPerChunk,
            Difficulty));
        int trafficPlaced = CountTraffic();

        // Lanes beside a hazard stay out of the density pass so the way around it stays readable;
        // they can still take a pickup.
        var trafficLanes = new System.Collections.Generic.List<int>();
        for (int l = 0; l < freeLanes.Count; l++)
        {
            if (!IsAdjacentToHazard(freeLanes[l], hazardLanes))
                trafficLanes.Add(freeLanes[l]);
        }

        while (trafficPlaced < targetTraffic && trafficLanes.Count >= 2)
        {
            int pick = _rng.RandiRange(0, trafficLanes.Count - 1);
            int extra = trafficLanes[pick];
            trafficLanes.RemoveAt(pick);
            occupied.Add(extra);
            SpawnTraffic(extra, (float)_rng.RandfRange(0.18f, 0.75f), EndlessRoadTraffic.TrafficKind.Civilian);
            trafficPlaced++;
        }

        // Occasional score pickup in a free lane.
        if (_rng.Randf() < 0.32f && freeLanes.Count > 0)
        {
            int lane = freeLanes[_rng.RandiRange(0, freeLanes.Count - 1)];
            var kind = _rng.Randf() < 0.5f ? EndlessRoadPickup.PickupKind.Score : EndlessRoadPickup.PickupKind.Repair;
            SpawnPickup(lane, (float)_rng.RandfRange(0.25f, 0.78f), kind);
        }
    }

    /// <summary>
    /// Escalation is driven by chunk index rather than elapsed time, so a chunk's contents
    /// stay identical however late it streams in.
    /// </summary>
    private float Difficulty => Mathf.Clamp(ChunkIndex / (float)Mathf.Max(1, _settings.EscalationChunks), 0.0f, 1.0f);

    private static bool IsAdjacentToHazard(int lane, HashSet<int> hazardLanes)
    {
        return hazardLanes.Contains(lane) || hazardLanes.Contains(lane - 1) || hazardLanes.Contains(lane + 1);
    }

    private int CountTraffic()
    {
        int count = 0;
        foreach (Node child in GetChildren())
        {
            if (child is EndlessRoadTraffic)
                count++;
        }

        return count;
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
