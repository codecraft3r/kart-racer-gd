using Godot;

/// <summary>
/// Reusable traffic / hazard car that lives inside an EndlessRoadChunk.
/// Civilian traffic is slower and drives straight; hazards are static.
/// All instances use simple BoxMesh placeholders so the endless mode
/// stays OpenGL-compat and does not require blocking asset imports.
/// </summary>
public partial class EndlessRoadTraffic : StaticBody3D
{
    public enum TrafficKind { Civilian, Wreck, Barricade, Debris }

    [Export] public TrafficKind Kind = TrafficKind.Civilian;
    [Export] public float SpeedMps = 0.0f;
    [Export] public int Lane = 2;

    private MeshInstance3D _mesh;
    private CollisionShape3D _col;

    public void Configure(TrafficKind kind, int lane, float speed, Color color, Vector3 size)
    {
        Kind = kind;
        Lane = lane;
        SpeedMps = speed;
        if (_mesh == null)
            BuildVisual(size);
        _mesh.Mesh = new BoxMesh { Size = size };
        if (_mesh.MaterialOverride is StandardMaterial3D mat)
            mat.AlbedoColor = color;
        if (_col?.Shape is BoxShape3D box)
            box.Size = size;
    }

    public override void _Ready()
    {
        if (_mesh == null)
            BuildVisual(new Vector3(1.8f, 1.1f, 3.2f));
    }

    private void BuildVisual(Vector3 size)
    {
        _mesh = new MeshInstance3D { Name = "TrafficMesh", Mesh = new BoxMesh { Size = size } };
        _mesh.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.6f, 0.62f), Roughness = 0.8f };
        AddChild(_mesh);
        _col = new CollisionShape3D { Name = "TrafficCol", Shape = new BoxShape3D { Size = size } };
        AddChild(_col);
    }
}
