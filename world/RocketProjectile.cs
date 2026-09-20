using Godot;
using System;

public partial class RocketProjectile : Node3D
{
    [Export] public float Speed = 52.0f;
    [Export] public float BlastRadius = 6.0f;
    [Export] public int BaseDamage = 40;
    [Export] public float ImpulseStrength = 24.0f;
    [Export] public int SourcePeerId = 1;

    private float _lifetime = 4.0f;
    private bool _detonated;
    private MeshInstance3D _missileMesh;
    private OmniLight3D _trailLight;

    public override void _Ready()
    {
        var capsule = new CylinderMesh
        {
            TopRadius = 0.08f,
            BottomRadius = 0.14f,
            Height = 0.85f
        };
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.1f, 0.35f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.3f, 0.1f) * 0.9f,
            Roughness = 0.3f
        };

        _missileMesh = new MeshInstance3D
        {
            Name = "MissileMesh",
            Mesh = capsule,
            MaterialOverride = material,
            RotationDegrees = new Vector3(90.0f, 0.0f, 0.0f)
        };
        AddChild(_missileMesh);

        _trailLight = new OmniLight3D
        {
            Name = "TrailLight",
            LightColor = new Color(1.0f, 0.45f, 0.1f),
            LightEnergy = 1.8f,
            OmniRange = 6.0f
        };
        AddChild(_trailLight);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_detonated)
            return;

        float dt = (float)delta;
        _lifetime -= dt;
        if (_lifetime <= 0.0f)
        {
            Detonate();
            return;
        }

        // Kart forward is +Basis.Z in this project, and the rocket inherits the kart rotation.
        Vector3 moveStep = GlobalTransform.Basis.Z * Speed * dt;
        Vector3 nextPos = GlobalPosition + moveStep;

        // Perform raycast query for continuous collision detection
        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState != null)
        {
            var query = PhysicsRayQueryParameters3D.Create(GlobalPosition, nextPos);
            query.CollideWithBodies = true;
            query.CollideWithAreas = false;
            query.CollisionMask = 1; // Collide with world & vehicles

            var hit = spaceState.IntersectRay(query);
            if (hit.Count > 0)
            {
                GlobalPosition = (Vector3)hit["position"];
                Detonate();
                return;
            }
        }

        GlobalPosition = nextPos;
    }

    public void Detonate()
    {
        if (_detonated)
            return;

        _detonated = true;
        Vector3 explosionPos = GlobalPosition;

        // Area blast damage and impulse
        if (Multiplayer.IsServer() || !Multiplayer.HasMultiplayerPeer())
        {
            var spaceState = GetWorld3D()?.DirectSpaceState;
            if (spaceState != null)
            {
                var sphereQuery = new PhysicsShapeQueryParameters3D();
                var sphere = new SphereShape3D { Radius = BlastRadius };
                sphereQuery.Shape = sphere;
                sphereQuery.Transform = new Transform3D(Basis.Identity, explosionPos);
                sphereQuery.CollisionMask = 1;

                var results = spaceState.IntersectShape(sphereQuery, 16);
                foreach (var result in results)
                {
                    if (result.TryGetValue("collider", out var colliderObj) && colliderObj.As<GodotObject>() is Kart kart)
                    {
                        if (kart.OwnerPeerId == SourcePeerId && _lifetime > 3.85f)
                            continue; // Don't self-damage immediately on firing

                        float dist = explosionPos.DistanceTo(kart.GlobalPosition);
                        float falloff = Mathf.Clamp(1.0f - (dist / BlastRadius), 0.2f, 1.0f);
                        int damage = Mathf.RoundToInt(BaseDamage * falloff);

                        GameManager.Instance?.ApplyVehicleDamage(kart.OwnerPeerId, damage);

                        Vector3 impulseDir = (kart.GlobalPosition - explosionPos).Normalized();
                        if (impulseDir.LengthSquared() < 0.01f)
                            impulseDir = Vector3.Up;

                        kart.ApplyCentralImpulse((impulseDir + Vector3.Up * 0.4f).Normalized() * (ImpulseStrength * falloff * kart.Mass));

                        if (kart.ActivePassenger.HasValue)
                        {
                            kart.SetPanic(kart.PanicMeter + (45.0f * falloff));
                            kart.TriggerSpeechBubble("ROCKET INCOMING!");
                        }
                    }
                }
            }
        }

        // Visual and Audio explosion
        AudioManager.Instance?.PlayWorld(AudioManager.Cue.Explosion, explosionPos, 1.5f, (float)GD.RandRange(0.92, 1.08), 120.0f);
        if (GetViewport()?.GetCamera3D() is TrackCamera cam)
            cam.AddTrauma(0.75f);

        SpawnExplosionVfx(explosionPos);
        QueueFree();
    }

    private void SpawnExplosionVfx(Vector3 pos)
    {
        var explosionRoot = new Node3D { Position = pos };
        GetParent()?.AddChild(explosionRoot);

        var sphereMesh = new SphereMesh { Radius = 0.5f, Height = 1.0f };
        var explosionMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.4f, 0.05f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.8f, 0.2f) * 1.5f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };

        var explosionSphere = new MeshInstance3D
        {
            Mesh = sphereMesh,
            MaterialOverride = explosionMat
        };
        explosionRoot.AddChild(explosionSphere);

        var flashLight = new OmniLight3D
        {
            LightColor = new Color(1.0f, 0.6f, 0.1f),
            // Reduced motion keeps the blast readable without the bright spike.
            LightEnergy = AccessibilitySettings.ReducedMotion ? 1.2f : 4.0f,
            OmniRange = 14.0f
        };
        explosionRoot.AddChild(flashLight);

        var tween = explosionRoot.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(explosionSphere, "scale", Vector3.One * BlastRadius * 1.6f, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(explosionMat, "albedo_color:a", 0.0f, 0.45f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.TweenProperty(flashLight, "light_energy", 0.0f, 0.45f);
        tween.Chain().TweenCallback(Callable.From(explosionRoot.QueueFree));
    }
}
