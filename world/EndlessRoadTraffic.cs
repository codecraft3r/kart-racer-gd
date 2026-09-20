using Godot;

/// <summary>
/// Reusable traffic / hazard car that lives inside an EndlessRoadChunk.
/// Civilians drive straight at SpeedMps; wrecks, barricades, and debris are parked.
/// All instances use simple box primitives so the endless mode stays OpenGL-compat
/// and does not require blocking asset imports.
/// </summary>
public partial class EndlessRoadTraffic : AnimatableBody3D
{
    public enum TrafficKind { Civilian, Wreck, Barricade, Debris }

    [Export] public TrafficKind Kind = TrafficKind.Civilian;
    [Export] public float SpeedMps = 0.0f;
    [Export] public int Lane = 2;

    /// <summary>
    /// Current travel velocity. Impact scoring reads this instead of assuming parked
    /// geometry, so rear-ending moving traffic is not scored like hitting a wall.
    /// </summary>
    public Vector3 Velocity { get; private set; }

    private MeshInstance3D _mesh;
    private MeshInstance3D _trim;
    private CollisionShape3D _col;

    public void Configure(TrafficKind kind, int lane, float speed, Color color, Vector3 size)
    {
        Kind = kind;
        Lane = lane;
        SpeedMps = kind == TrafficKind.Civilian ? speed : 0.0f;

        if (_mesh == null)
            BuildVisual(size);

        if (_mesh.Mesh is not BoxMesh bodyMesh || bodyMesh.Size != size)
            _mesh.Mesh = new BoxMesh { Size = size };

        if (_mesh.MaterialOverride is StandardMaterial3D mat)
            mat.AlbedoColor = color;

        if (_col?.Shape is BoxShape3D box)
            box.Size = size;

        if (_trim != null)
        {
            Vector3 trimSize = TrimSize(kind, size);
            if (_trim.Mesh is not BoxMesh trimMesh || trimMesh.Size != trimSize)
                _trim.Mesh = new BoxMesh { Size = trimSize };

            _trim.Position = TrimOffset(kind, size);
        }

        SetPhysicsProcess(SpeedMps > 0.0f);
    }

    public override void _Ready()
    {
        if (_mesh == null)
            BuildVisual(new Vector3(1.8f, 1.1f, 3.2f));

        SetPhysicsProcess(SpeedMps > 0.0f);
    }

    public override void _PhysicsProcess(double delta)
    {
        using var perf = PerfProbe.Measure(PerfHotspot.EndlessTrafficDrive);
        if (SpeedMps <= 0.0f)
            return;

        // The road runs along +Z and the player travels the same way, so a civilian that
        // holds its lane simply advances along local Z.
        Position = new Vector3(Position.X, Position.Y, Position.Z + SpeedMps * (float)delta);
        Velocity = new Vector3(0.0f, 0.0f, SpeedMps);
    }

    private void BuildVisual(Vector3 size)
    {
        _mesh = new MeshInstance3D { Name = "TrafficMesh", Mesh = new BoxMesh { Size = size } };
        _mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.6f, 0.62f), Roughness = 0.8f };
        AddChild(_mesh);

        _col = new CollisionShape3D { Name = "TrafficCol", Shape = new BoxShape3D { Size = size } };
        AddChild(_col);

        // A second box gives each kind a readable silhouette: a cab for vehicles, a striped
        // deck for barricades, and a low lump for debris. Collision stays the plain box.
        _trim = new MeshInstance3D
        {
            Name = "TrafficTrim",
            Mesh = new BoxMesh { Size = TrimSize(Kind, size) },
            Position = TrimOffset(Kind, size),
            MaterialOverride = MakeTrimMaterial(Kind)
        };
        AddChild(_trim);
    }

    private static Vector3 TrimSize(TrafficKind kind, Vector3 size)
    {
        return kind switch
        {
            TrafficKind.Barricade => new Vector3(size.X * 0.86f, size.Y * 0.22f, size.Z * 1.06f),
            TrafficKind.Debris => new Vector3(size.X * 0.62f, size.Y * 0.5f, size.Z * 0.5f),
            TrafficKind.Wreck => new Vector3(size.X * 0.76f, size.Y * 0.46f, size.Z * 0.54f),
            _ => new Vector3(size.X * 0.74f, size.Y * 0.5f, size.Z * 0.46f)
        };
    }

    private static Vector3 TrimOffset(TrafficKind kind, Vector3 size)
    {
        return kind switch
        {
            TrafficKind.Barricade => new Vector3(0.0f, size.Y * 0.56f, 0.0f),
            TrafficKind.Debris => new Vector3(size.X * 0.18f, -size.Y * 0.2f, size.Z * 0.16f),
            _ => new Vector3(0.0f, size.Y * 0.73f, -size.Z * 0.06f)
        };
    }

    private static StandardMaterial3D MakeTrimMaterial(TrafficKind kind)
    {
        return kind switch
        {
            TrafficKind.Barricade => new StandardMaterial3D
            {
                AlbedoColor = new Color(1.0f, 0.55f, 0.08f),
                EmissionEnabled = true,
                Emission = new Color(1.0f, 0.45f, 0.05f) * 0.9f,
                Roughness = 0.5f
            },
            TrafficKind.Wreck => new StandardMaterial3D { AlbedoColor = new Color(0.16f, 0.16f, 0.18f), Roughness = 0.92f },
            TrafficKind.Debris => new StandardMaterial3D { AlbedoColor = new Color(0.29f, 0.21f, 0.14f), Roughness = 0.95f },
            _ => new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.15f, 0.2f), Roughness = 0.35f, Metallic = 0.25f }
        };
    }
}
