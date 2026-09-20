using Godot;

/// <summary>
/// T4 — first combat slice pickup. A cyan wireframe crate parked at the outer
/// edge of the city (>= 90 m from the depot) that grants the taxi its single
/// carried Nudge Charge. Collecting it hides the crate for 10 s and then puts
/// it back, so the combat loop keeps a steady, readable supply without
/// littering the streets with pickups.
/// </summary>
public partial class BumperCrate : Area3D
{
    public const float RespawnSeconds = 10.0f;
    public static readonly Color CrateColor = new(0.10f, 0.92f, 1.0f, 1.0f);

    private Node3D _visual;
    private float _respawnTimer;
    private bool _collected;

    public bool IsAvailable => !_collected;

    public override void _Ready()
    {
        Monitoring = true;
        Monitorable = false;
        CollisionLayer = 0;
        CollisionMask = 1;

        var shape = new CollisionShape3D { Name = "CollisionShape" };
        shape.Shape = new BoxShape3D { Size = new Vector3(2.4f, 2.4f, 2.4f) };
        AddChild(shape);

        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);

        var wireMaterial = new StandardMaterial3D
        {
            AlbedoColor = CrateColor,
            EmissionEnabled = true,
            Emission = CrateColor * 0.85f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };

        var box = new MeshInstance3D
        {
            Name = "CrateWireframe",
            Mesh = new BoxMesh { Size = new Vector3(2.0f, 2.0f, 2.0f) },
            MaterialOverride = wireMaterial,
            Position = new Vector3(0.0f, 1.1f, 0.0f)
        };
        _visual.AddChild(box);

        _visual.AddChild(new OmniLight3D
        {
            Name = "CrateLight",
            LightColor = CrateColor,
            LightEnergy = 0.55f,
            OmniRange = 8.0f,
            Position = new Vector3(0.0f, 1.6f, 0.0f)
        });

        _visual.AddChild(new Label3D
        {
            Name = "CrateLabel",
            Text = "NUDGE",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 22,
            Modulate = CrateColor,
            Position = new Vector3(0.0f, 2.7f, 0.0f)
        });

        BodyEntered += OnBodyEntered;
    }

    public override void _Process(double delta)
    {
        if (_visual != null && !_collected)
            _visual.RotateY((float)delta * 1.4f);

        if (!_collected)
            return;

        _respawnTimer -= (float)delta;
        if (_respawnTimer <= 0.0f)
            SetCollected(false);
    }

    private void OnBodyEntered(Node body)
    {
        if (_collected || body is not Kart kart)
            return;

        if (Multiplayer.HasMultiplayerPeer() && !Multiplayer.IsServer())
            return;

        if (GameManager.Instance?.AddNudgeCharge(kart.OwnerPeerId) != true)
            return;

        SetCollected(true);
        AudioManager.Instance?.PlayLocal(AudioManager.Cue.WeaponPickup, -2.0f);
    }

    private void SetCollected(bool collected)
    {
        _collected = collected;
        _respawnTimer = collected ? RespawnSeconds : 0.0f;
        Monitoring = !collected;
        if (_visual != null)
            _visual.Visible = !collected;
    }
}
