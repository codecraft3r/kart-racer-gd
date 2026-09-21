using Godot;
using System;

public partial class CompassArrow : Node3D
{
    private MeshInstance3D _meshInstance;
    private float _timeAccumulator = 0.0f;

    // The arrow hugs the roof of the cab. It used to be a 2 m opaque cone sitting 2.6 m up,
    // and even a 0.72 m cone at 2 m reads as a screen-filling slab because the chase camera
    // sits only ~5.8 m behind it. Keep it small, low, and translucent.
    private const float HoverHeight = 1.5f;
    private const float BobAmplitude = 0.07f;
    private const float BobSpeed = 4.5f;
    private const float HideWithinMeters = 7.0f;
    private const float FullOpacityMeters = 28.0f;

    public override void _Ready()
    {
        // 1. Create arrow visual programmatically: a slim hologram chevron, not a solid cone.
        var arrowMesh = new CylinderMesh
        {
            TopRadius = 0.0f,      // Cone tip pointing forward
            BottomRadius = 0.17f,
            Height = 0.46f,
            RadialSegments = 6
        };

        var arrowMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.0f, 0.95f, 1.0f, 0.42f), // Glowing neon cyan
            EmissionEnabled = true,
            Emission = new Color(0.0f, 0.95f, 1.0f) * 1.4f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = false
        };

        _meshInstance = new MeshInstance3D
        {
            Name = "ArrowMesh",
            Mesh = arrowMesh,
            MaterialOverride = arrowMaterial,
            // Cylinder stands vertical by default, rotate it so the tip points forward (-Z)
            Rotation = new Vector3(-Mathf.Pi / 2.0f, 0.0f, 0.0f),
            Position = new Vector3(0.0f, 0.0f, -0.23f) // Offset so pivot is at the tail
        };
        AddChild(_meshInstance);

        // Position above the kart
        Position = new Vector3(0.0f, HoverHeight, 0.0f);
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

        if (TaxiMode.Instance == null)
        {
            Visible = false;
            return;
        }

        _timeAccumulator += (float)delta;

        // 1. Hover/floating animation
        float hoverOffset = Mathf.Sin(_timeAccumulator * BobSpeed) * BobAmplitude;
        Position = new Vector3(0.0f, HoverHeight + hoverOffset, 0.0f);

        Vector3 targetPos = Vector3.Zero;
        Color arrowColor = new Color(0.0f, 0.95f, 1.0f); // Default Cyan

        if (parentKart.ActivePassenger.HasValue)
        {
            Vector3 dest = TaxiMode.Instance.ActiveDestination;
            targetPos = new Vector3(dest.X, GlobalPosition.Y, dest.Z);

            var wealth = parentKart.ActivePassenger.Value.Wealth;
            if (wealth == GameManager.CustomerWealth.Medium)
                arrowColor = new Color(1.0f, 0.82f, 0.22f); // Amber
            else if (wealth == GameManager.CustomerWealth.High)
                arrowColor = new Color(1.0f, 0.0f, 0.5f); // Neon Pink

            Visible = dest != Vector3.Zero;
        }
        else
        {
            // Find nearest active pickup zone
            float nearestDist = float.MaxValue;
            PickupZone nearestZone = null;

            foreach (var child in TaxiMode.Instance.GetChildren())
            {
                if (child is PickupZone zone && GodotObject.IsInstanceValid(zone))
                {
                    float dist = GlobalPosition.DistanceSquaredTo(zone.GlobalPosition);
                    if (dist < nearestDist)
                    {
                        nearestDist = dist;
                        nearestZone = zone;
                    }
                }
            }

            if (nearestZone != null)
            {
                targetPos = new Vector3(nearestZone.GlobalPosition.X, GlobalPosition.Y, nearestZone.GlobalPosition.Z);
                arrowColor = new Color(0.48f, 0.70f, 0.45f); // Green
                Visible = true;
            }
            else
            {
                Visible = false;
            }
        }

        if (!Visible)
            return;

        // Fade the cue out as the objective arrives so it never sits on top of the pickup or
        // drop-off marker it is pointing at.
        float distance = GlobalPosition.DistanceTo(targetPos);
        if (distance <= HideWithinMeters)
        {
            Visible = false;
            return;
        }

        float alpha = Mathf.Lerp(0.16f, 0.44f, Mathf.Clamp((distance - HideWithinMeters) / (FullOpacityMeters - HideWithinMeters), 0.0f, 1.0f));
        if (GlobalPosition.DistanceSquaredTo(targetPos) > 0.2f)
        {
            LookAt(targetPos, Vector3.Up);

            var material = _meshInstance?.MaterialOverride as StandardMaterial3D;
            if (material != null)
            {
                material.AlbedoColor = new Color(arrowColor.R, arrowColor.G, arrowColor.B, alpha);
                material.Emission = arrowColor * 1.2f;
            }
        }
    }
}
