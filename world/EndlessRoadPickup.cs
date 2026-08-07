using Godot;

/// <summary>
/// Boost / repair / score pickup that sits in a lane on the endless road.
/// Burnout Paradise style: you weave through traffic to snag it.
/// </summary>
public partial class EndlessRoadPickup : Area3D
{
    public enum PickupKind { Boost, Repair, Score }

    [Export] public PickupKind Kind = PickupKind.Boost;
    [Export] public int ScoreValue = 250;

    private Node3D _visual;
    private bool _collected;

    public bool IsAvailable => !_collected && IsInsideTree();

    public void Configure(PickupKind kind, int scoreValue = 250)
    {
        Kind = kind;
        ScoreValue = scoreValue;
        BuildVisual();
    }

    public override void _Ready()
    {
        Monitoring = true;
        Monitorable = false;
        CollisionLayer = 0;
        CollisionMask = 1;
        if (GetChildCount() == 0)
            BuildVisual();
        BodyEntered += OnBodyEntered;
    }

    private void BuildVisual()
    {
        foreach (Node c in GetChildren())
            if (c.Name == "Visual" || c.Name == "CollisionShape")
                c.QueueFree();
        var shape = new CollisionShape3D { Name = "CollisionShape", Shape = new BoxShape3D { Size = new Vector3(1.6f, 1.6f, 1.6f) } };
        AddChild(shape);
        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);
        Color color = Kind switch
        {
            PickupKind.Repair => new Color(0.2f, 1.0f, 0.4f),
            PickupKind.Score => new Color(1.0f, 0.82f, 0.15f),
            _ => new Color(0.15f, 0.75f, 1.0f)
        };
        var mat = new StandardMaterial3D { AlbedoColor = color, EmissionEnabled = true, Emission = color * 0.7f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
        _visual.AddChild(new MeshInstance3D { Name = "PickupMesh", Mesh = new BoxMesh { Size = new Vector3(1.0f, 1.0f, 1.0f) }, MaterialOverride = mat, Position = new Vector3(0, 0.7f, 0) });
        _visual.AddChild(new OmniLight3D { Name = "PickupLight", LightColor = color, LightEnergy = 0.6f, OmniRange = 6.0f, Position = new Vector3(0, 0.9f, 0) });
        string label = Kind switch { PickupKind.Repair => "REPAIR", PickupKind.Score => "SCORE", _ => "BOOST" };
        _visual.AddChild(new Label3D { Name = "PickupLabel", Text = label, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FontSize = 18, Modulate = color, Position = new Vector3(0, 1.9f, 0) });
    }

    public override void _Process(double delta)
    {
        if (_visual != null && !_collected)
            _visual.RotateY((float)delta * 1.6f);
    }

    private void OnBodyEntered(Node body)
    {
        if (_collected || body is not Kart)
            return;
        if (Multiplayer.HasMultiplayerPeer() && !Multiplayer.IsServer())
            return;
        _collected = true;
        var mode = EndlessRoadMode.Instance;
        if (mode == null) { QueueFree(); return; }
        switch (Kind)
        {
            case PickupKind.Boost:
                mode.AddScore(75);
                // Top up boost charge via reflection-safe path: just add score and let boost recharge tick;
                // also directly bump mode's Boost through its public recharge by setting a one-shot bonus via AddScore path.
                // Proper boost refill:
                mode.Boost = Mathf.Min(mode.Settings.BoostMaxCharge, mode.Boost + 0.45f);
                AudioManager.Instance?.PlayLocal(AudioManager.Cue.WeaponPickup, -3.0f, 1.15f);
                break;
            case PickupKind.Repair:
                mode.Health = Mathf.Min(mode.Settings.VehicleHealth, mode.Health + 28.0f);
                mode.AddScore(40);
                AudioManager.Instance?.PlayLocal(AudioManager.Cue.WeaponPickup, -3.0f, 0.95f);
                break;
            case PickupKind.Score:
                mode.AddScore(ScoreValue);
                AudioManager.Instance?.PlayLocal(AudioManager.Cue.WeaponPickup, -3.0f, 1.25f);
                break;
        }
        Visible = false;
        Monitoring = false;
        QueueFree();
    }
}
