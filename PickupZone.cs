using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class PickupZone : Area3D
{
    [Export] public GameManager.CustomerDistance Distance = GameManager.CustomerDistance.Near;
    [Export] public GameManager.CustomerWealth Wealth = GameManager.CustomerWealth.Low;
    [Export] public int MaxAcceptableDamage = 30;
    [Export] public int GroupSize = 1;
    [Export] public float LoadTime = 5.0f;

    private readonly List<Kart> _overlappingKarts = new();
    private readonly Dictionary<int, float> _boardingTimers = new();
    private CollisionShape3D _collisionShape;
    private Node3D _visual;

    public override void _Ready()
    {
        // 1. Setup Area3D collision layers/monitoring
        Monitoring = true;
        Monitorable = false;
        CollisionLayer = 0; // Don't block anything
        CollisionMask = 1;  // Detect karts (Layer 1)

        // 2. Create CollisionShape3D
        _collisionShape = new CollisionShape3D { Name = "CollisionShape" };
        var cylinder = new CylinderShape3D { Radius = 5.0f, Height = 3.0f };
        _collisionShape.Shape = cylinder;
        AddChild(_collisionShape);

        // 3. Create visual ring & light
        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);

        // Color based on wealth, using the same broadcast palette as the HUD.
        Color zoneColor = new Color(0.96f, 0.72f, 0.18f, 0.58f); // Low: Fare yellow
        if (Wealth == GameManager.CustomerWealth.Medium)
            zoneColor = new Color(0.1f, 0.86f, 0.95f, 0.58f); // Medium: Cyan
        else if (Wealth == GameManager.CustomerWealth.High)
            zoneColor = new Color(0.93f, 0.16f, 0.5f, 0.58f); // High: Neon pink

        var beaconMesh = new TorusMesh
        {
            InnerRadius = 4.1f,
            OuterRadius = 5.0f,
            Rings = 8,
            RingSegments = 32
        };
        var beaconMaterial = new StandardMaterial3D
        {
            AlbedoColor = zoneColor,
            EmissionEnabled = true,
            Emission = new Color(zoneColor.R, zoneColor.G, zoneColor.B) * 0.58f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        var ring = new MeshInstance3D
        {
            Mesh = beaconMesh,
            MaterialOverride = beaconMaterial,
            Position = new Vector3(0, 0.14f, 0)
        };
        _visual.AddChild(ring);

        for (int index = 0; index < 4; index++)
        {
            float angle = index * Mathf.Pi * 0.5f;
            Vector3 direction = new(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            var bracket = new MeshInstance3D
            {
                Name = $"PickupBracket{index}",
                Mesh = new BoxMesh
                {
                    Size = new Vector3(index % 2 == 0 ? 0.75f : 2.1f, 0.12f, index % 2 == 0 ? 2.1f : 0.75f)
                },
                MaterialOverride = beaconMaterial,
                Position = direction * 4.55f + Vector3.Up * 0.14f
            };
            _visual.AddChild(bracket);
        }

        var light = new OmniLight3D
        {
            LightColor = zoneColor,
            LightEnergy = 0.75f,
            OmniRange = 11.0f,
            Position = new Vector3(0, 2.5f, 0)
        };
        _visual.AddChild(light);

        var arrow = new HolographicArrow
        {
            Name = "HolographicArrow",
            ArrowColor = new Color(zoneColor.R, zoneColor.G, zoneColor.B, 1.0f),
            Position = new Vector3(0.0f, 6.0f, 0.0f)
        };
        _visual.AddChild(arrow);

        // 4. Connect signals
        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void OnBodyEntered(Node body)
    {
        if (body is Kart kart)
        {
            if (!_overlappingKarts.Contains(kart))
            {
                _overlappingKarts.Add(kart);
                kart.PlayPickupEnterAudio();
                GD.Print($"PickupZone: Kart {kart.Name} entered zone.");
            }
        }
    }

    private void OnBodyExited(Node body)
    {
        if (body is Kart kart)
        {
            _overlappingKarts.Remove(kart);
            _boardingTimers.Remove(kart.OwnerPeerId);
            kart.SetBoardingProgress(0.0f);
            GD.Print($"PickupZone: Kart {kart.Name} exited zone.");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer() || TaxiMode.Instance == null || !TaxiMode.Instance.MatchActive)
            return;

        if (_overlappingKarts.Count == 0)
            return;

        // 1. Filter karts that are valid, stopped, and don't have passengers
        var validKarts = _overlappingKarts
            .Where(k => IsInstanceValid(k) &&
                k.LinearVelocity.Length() < 0.8f &&
                !k.ActivePassenger.HasValue &&
                GameManager.Instance.GetPlayerHealth(k.OwnerPeerId) >= 100 - MaxAcceptableDamage)
            .ToList();

        if (validKarts.Count == 0)
        {
            // Decay boarding progress for any moving karts in the zone
            foreach (var kart in _overlappingKarts)
            {
                if (IsInstanceValid(kart))
                {
                    if (_boardingTimers.ContainsKey(kart.OwnerPeerId))
                    {
                        _boardingTimers[kart.OwnerPeerId] = Mathf.Max(0.0f, _boardingTimers[kart.OwnerPeerId] - (float)delta * 2.0f);
                        kart.SetBoardingProgress(_boardingTimers[kart.OwnerPeerId] / LoadTime);
                    }
                }
            }
            return;
        }

        // 2. If multiple karts are stopped, select the one with the highest health
        Kart selectedKart = validKarts[0];
        if (validKarts.Count > 1)
        {
            selectedKart = validKarts
                .OrderByDescending(k => GameManager.Instance.GetPlayerHealth(k.OwnerPeerId))
                .First();
        }

        // 3. Decay other stopped/valid karts' progress, increment selected kart's progress
        foreach (var kart in _overlappingKarts)
        {
            if (!IsInstanceValid(kart)) continue;

            int peerId = kart.OwnerPeerId;
            if (kart == selectedKart)
            {
                if (!_boardingTimers.ContainsKey(peerId))
                    _boardingTimers[peerId] = 0.0f;

                _boardingTimers[peerId] += (float)delta;
                float progress = Mathf.Clamp(_boardingTimers[peerId] / LoadTime, 0.0f, 1.0f);
                kart.SetBoardingProgress(progress);

                if (progress >= 1.0f)
                {
                    // Boarding complete!
                    TriggerBoarding(kart);
                    return;
                }
            }
            else
            {
                if (_boardingTimers.ContainsKey(peerId))
                {
                    _boardingTimers[peerId] = Mathf.Max(0.0f, _boardingTimers[peerId] - (float)delta * 2.0f);
                    kart.SetBoardingProgress(_boardingTimers[peerId] / LoadTime);
                }
            }
        }
    }

    private void TriggerBoarding(Kart kart)
    {
        var data = new GameManager.CustomerData
        {
            Distance = Distance,
            Wealth = Wealth,
            MaxAcceptableDamage = MaxAcceptableDamage,
            GroupSize = GroupSize,
            LoadTime = LoadTime
        };

        kart.BoardPassenger(data);
        TaxiMode.Instance.OnPassengerBoarded(kart.OwnerPeerId, data);

        // Reset progress on all karts
        foreach (var k in _overlappingKarts)
        {
            if (IsInstanceValid(k))
                k.SetBoardingProgress(0.0f);
        }

        // Delete the pickup zone
        QueueFree();
    }
}
