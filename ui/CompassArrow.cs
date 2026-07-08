using Godot;
using System;

public partial class CompassArrow : Node3D
{
    private MeshInstance3D _meshInstance;
    private float _timeAccumulator = 0.0f;

    public override void _Ready()
    {
        // 1. Create arrow visual programmatically
        var arrowMesh = new CylinderMesh
        {
            TopRadius = 0.0f,      // Cone tip pointing forward
            BottomRadius = 0.25f,
            Height = 1.0f,
            RadialSegments = 6
        };

        var arrowMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.0f, 0.95f, 1.0f), // Glowing neon cyan
            EmissionEnabled = true,
            Emission = new Color(0.0f, 0.95f, 1.0f) * 1.2f,
            Roughness = 0.2f
        };

        _meshInstance = new MeshInstance3D
        {
            Name = "ArrowMesh",
            Mesh = arrowMesh,
            MaterialOverride = arrowMaterial,
            // Cylinder stands vertical by default, rotate it so the tip points forward (-Z)
            Rotation = new Vector3(Mathf.Pi / 2.0f, 0.0f, 0.0f),
            Position = new Vector3(0.0f, 0.0f, -0.5f) // Offset so pivot is at the tail
        };
        AddChild(_meshInstance);

        // Position above the kart
        Position = new Vector3(0.0f, 2.6f, 0.0f);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        var parentKart = GetParent<Kart>();
        if (parentKart == null || !IsInstanceValid(parentKart))
            return;

        // Only show arrow for local player
        if (!parentKart.IsLocalPlayer)
        {
            Visible = false;
            return;
        }

        // Only show if there is an active destination
        if (TaxiMode.Instance == null || TaxiMode.Instance.ActiveDestination == Vector3.Zero)
        {
            Visible = false;
            return;
        }

        Visible = true;
        _timeAccumulator += (float)delta;

        // 1. Hover/floating animation
        float hoverOffset = Mathf.Sin(_timeAccumulator * 4.5f) * 0.15f;
        Position = new Vector3(0.0f, 2.6f + hoverOffset, 0.0f);

        // 2. Point towards the destination in the horizontal plane (flat X-Z direction)
        Vector3 dest = TaxiMode.Instance.ActiveDestination;
        Vector3 targetPos = new Vector3(dest.X, GlobalPosition.Y, dest.Z);

        if (GlobalPosition.DistanceSquaredTo(targetPos) > 0.2f)
        {
            LookAt(targetPos, Vector3.Up);
        }
    }
}
