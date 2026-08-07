using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Burnout Paradise-style road hazards: oil slicks, fire strips, ramp wedges.
/// Thin, readable shapes — huge payoff if you drift over a slick or hit a ramp.
/// </summary>
public partial class EndlessRoadHazard : Area3D
{
    public enum HazardKind { OilSlick, FireStrip, Ramp }

    [Export] public HazardKind Kind = HazardKind.OilSlick;

    private MeshInstance3D _mesh;

    public void Configure(HazardKind kind)
    {
        Kind = kind;
        BuildVisual();
    }

    public override void _Ready()
    {
        Monitoring = true;
        Monitorable = false;
        CollisionLayer = 0;
        CollisionMask = 1;
        if (_mesh == null) BuildVisual();
        BodyEntered += OnBodyEntered;
    }

    private void BuildVisual()
    {
        foreach (Node c in GetChildren())
            if (c.Name == "HazardMesh" || c.Name == "HazardCol" || c.Name == "HazardLight")
                c.QueueFree();
        Vector3 size;
        Color color;
        float y;
        switch (Kind)
        {
            case HazardKind.FireStrip:
                size = new Vector3(2.2f, 0.08f, 5.0f); color = new Color(1.0f, 0.28f, 0.05f); y = 0.06f; break;
            case HazardKind.Ramp:
                size = new Vector3(2.6f, 0.55f, 4.0f); color = new Color(0.95f, 0.88f, 0.25f); y = 0.28f; break;
            default:
                size = new Vector3(2.4f, 0.06f, 4.5f); color = new Color(0.08f, 0.08f, 0.09f); y = 0.04f; break;
        }
        _mesh = new MeshInstance3D { Name = "HazardMesh", Mesh = new BoxMesh { Size = size }, Position = new Vector3(0, y, 0) };
        _mesh.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = Kind != HazardKind.OilSlick,
            Emission = color * 0.55f,
            Roughness = Kind == HazardKind.OilSlick ? 0.25f : 0.75f,
            Metallic = Kind == HazardKind.OilSlick ? 0.35f : 0.05f
        };
        AddChild(_mesh);
        AddChild(new CollisionShape3D { Name = "HazardCol", Shape = new BoxShape3D { Size = size }, Position = new Vector3(0, y, 0) });
        if (Kind == HazardKind.FireStrip)
            AddChild(new OmniLight3D { Name = "HazardLight", LightColor = color, LightEnergy = 0.9f, OmniRange = 7.0f, Position = new Vector3(0, 0.7f, 0) });
    }

    private void OnBodyEntered(Node body)
    {
        if (body is not Kart kart) return;
        if (Multiplayer.HasMultiplayerPeer() && !Multiplayer.IsServer()) return;
        var mode = EndlessRoadMode.Instance;
        if (mode == null || mode.State != EndlessRoadMode.RunState.Running) return;
        switch (Kind)
        {
            case HazardKind.OilSlick:
                // Oversteer nudge + tiny score for holding drift through it.
                kart.ApplyCentralImpulse(new Vector3((float)GD.RandRange(-1.0, 1.0), 0, 0) * 140.0f);
                mode.AddScore(20);
                break;
            case HazardKind.FireStrip:
                mode.ApplyImpact(Kart.ImpactSeverity.Bump);
                mode.AddScore(10);
                break;
            case HazardKind.Ramp:
                kart.ApplyCentralImpulse(Vector3.Up * 420.0f + kart.GlobalTransform.Basis.Z * 90.0f);
                mode.AddScore(85);
                AudioManager.Instance?.PlayLocal(AudioManager.Cue.CollisionLight, -4.0f, 1.15f);
                break;
        }
    }
}
