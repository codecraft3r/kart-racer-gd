using Godot;
using System;
using System.Collections.Generic;

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
    private readonly List<PassengerActor> _passengers = new();
    private readonly List<StandardMaterial3D> _markerMaterials = new();
    private OmniLight3D _markerLight;
    private HolographicArrow _markerArrow;
    private Color _markerColor;

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
        _markerColor = TaxiMode.WealthColor(Wealth);
        Color zoneColor = new(_markerColor.R, _markerColor.G, _markerColor.B, 0.30f);

        var beaconMesh = new TorusMesh
        {
            InnerRadius = 4.65f,
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
        _markerMaterials.Add(beaconMaterial);
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

        _markerLight = new OmniLight3D
        {
            LightColor = zoneColor,
            LightEnergy = 0.45f,
            OmniRange = 10.0f,
            Position = new Vector3(0, 2.5f, 0)
        };
        _visual.AddChild(_markerLight);

        _markerArrow = new HolographicArrow
        {
            Name = "HolographicArrow",
            ArrowColor = new Color(zoneColor.R, zoneColor.G, zoneColor.B, 1.0f),
            Position = new Vector3(0.0f, 4.0f, 0.0f)
        };
        _visual.AddChild(_markerArrow);

        // Visible customers turn the abstract pickup ring into a readable curbside scene.
        for (int index = 0; index < GroupSize; index++)
        {
            float angle = Mathf.Pi * 0.25f + index * 0.72f;
            var passenger = new PassengerActor { Name = $"WaitingPassenger{index}", Position = new Vector3(Mathf.Cos(angle) * 3.1f, 0.05f, Mathf.Sin(angle) * 3.1f) };
            AddChild(passenger);
            passenger.Build(zoneColor, index == 0 ? "HAIL" : "", index * 0.8f);
            _passengers.Add(passenger);
        }

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
        using var perf = PerfProbe.Measure(PerfHotspot.PickupZonePhysics);
        if (!Multiplayer.IsServer() || TaxiMode.Instance == null || !TaxiMode.Instance.MatchActive)
            return;

        if (_overlappingKarts.Count == 0)
            return;

        Kart selectedKart = null;
        int selectedHealth = int.MinValue;
        foreach (Kart kart in _overlappingKarts)
        {
            if (!IsInstanceValid(kart) || kart.LinearVelocity.Length() >= 0.8f || kart.ActivePassenger.HasValue)
                continue;

            int health = GameManager.Instance.GetPlayerHealth(kart.OwnerPeerId);
            if (health < 100 - MaxAcceptableDamage || health <= selectedHealth)
                continue;

            selectedKart = kart;
            selectedHealth = health;
        }

        if (selectedKart == null)
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

        // Decay other stopped/valid karts' progress, increment selected kart's progress.
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
                UpdatePassengerBoarding(kart, progress);

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

    public override void _Process(double delta)
    {
        if (_visual == null || TaxiMode.Instance == null)
            return;

        Kart localKart = GameManager.Instance?.GetKart(GetLocalPeerId());
        if (localKart == null || !GodotObject.IsInstanceValid(localKart) || localKart.ActivePassenger.HasValue)
        {
            _visual.Visible = false;
            return;
        }

        int health = GameManager.Instance?.GetPlayerHealth(localKart.OwnerPeerId) ?? 100;
        if (health < 100 - MaxAcceptableDamage)
        {
            _visual.Visible = false;
            return;
        }

        _visual.Visible = true;
        bool selected = TaxiMode.Instance.TryGetObjectiveForKart(localKart, out TaxiMode.ObjectiveTarget target) &&
            target.Kind == TaxiMode.ObjectiveKind.Pickup && target.WorldPosition.DistanceSquaredTo(GlobalPosition) < 0.01f;
        float distance = localKart.GlobalPosition.DistanceTo(GlobalPosition);
        float alpha = selected ? distance <= 5.5f ? 0.14f : distance <= 12.0f ? 0.22f : 0.30f : 0.10f;
        foreach (StandardMaterial3D material in _markerMaterials)
        {
            material.AlbedoColor = new Color(_markerColor.R, _markerColor.G, _markerColor.B, alpha);
            material.Emission = _markerColor * (0.35f + alpha);
        }
        if (_markerLight != null)
            _markerLight.LightEnergy = selected ? 0.45f : 0.12f;
    }

    private static int GetLocalPeerId()
    {
        return MultiplayerManager.Instance != null && MultiplayerManager.Instance.Multiplayer.HasMultiplayerPeer()
            ? MultiplayerManager.Instance.Multiplayer.GetUniqueId()
            : 1;
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

    private void UpdatePassengerBoarding(Kart kart, float progress)
    {
        for (int index = 0; index < _passengers.Count; index++)
        {
            PassengerActor passenger = _passengers[index];
            if (GodotObject.IsInstanceValid(passenger))
                passenger.SetBoarding(kart, Mathf.Clamp(progress - index * 0.12f, 0.0f, 1.0f));
        }
    }
}
