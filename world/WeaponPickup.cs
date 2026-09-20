using Godot;
using System;

public partial class WeaponPickup : Area3D
{
    [Export] public GameManager.WeaponClass WeaponType = GameManager.WeaponClass.Rocket;
    [Export] public int StartingAmmo = 3;
    [Export] public float RespawnTimeSeconds = 15.0f;

    private Node3D _visual;
    private CollisionShape3D _collisionShape;
    private OmniLight3D _glowLight;
    private bool _isAvailable = true;
    private float _respawnTimer;
    private Color _pickupColor;

    public override void _Ready()
    {
        Monitoring = true;
        Monitorable = false;
        CollisionLayer = 0;
        CollisionMask = 1; // Detect karts

        _collisionShape = new CollisionShape3D { Name = "CollisionShape" };
        var box = new BoxShape3D { Size = new Vector3(2.8f, 2.5f, 2.8f) };
        _collisionShape.Shape = box;
        AddChild(_collisionShape);

        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);

        _pickupColor = WeaponType switch
        {
            GameManager.WeaponClass.Rocket => new Color(1.0f, 0.12f, 0.45f),
            GameManager.WeaponClass.Assault => new Color(0.1f, 0.88f, 1.0f),
            _ => new Color(1.0f, 0.78f, 0.12f)
        };

        var crateMesh = new BoxMesh { Size = new Vector3(1.2f, 1.2f, 1.2f) };
        var crateMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.08f, 0.08f, 0.12f),
            EmissionEnabled = true,
            Emission = _pickupColor * 0.8f,
            Roughness = 0.4f
        };

        var crateInstance = new MeshInstance3D
        {
            Name = "CrateMesh",
            Mesh = crateMesh,
            MaterialOverride = crateMat,
            Position = Vector3.Up * 0.9f
        };
        _visual.AddChild(crateInstance);

        var label = new Label3D
        {
            Name = "WeaponLabel",
            Text = WeaponType == GameManager.WeaponClass.Rocket ? "🚀 ROCKET" : "⚡ ASSAULT",
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 32,
            PixelSize = 0.016f,
            Modulate = _pickupColor,
            OutlineModulate = Colors.Black,
            OutlineSize = 6,
            Position = Vector3.Up * 2.2f
        };
        _visual.AddChild(label);

        _glowLight = new OmniLight3D
        {
            LightColor = _pickupColor,
            LightEnergy = 0.8f,
            OmniRange = 8.0f,
            Position = Vector3.Up * 1.2f
        };
        _visual.AddChild(_glowLight);

        BodyEntered += OnBodyEntered;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (!_isAvailable)
        {
            // Only the server runs the respawn clock; every other peer waits for the broadcast,
            // so two peers cannot each consume or revive the same crate.
            if (IsAuthority)
            {
                _respawnTimer -= dt;
                if (_respawnTimer <= 0.0f)
                    PublishAvailability(true);
            }
            return;
        }

        if (_visual != null)
        {
            _visual.RotateY(dt * 2.2f);
        }
    }

    private bool IsAuthority => !Multiplayer.HasMultiplayerPeer() || Multiplayer.IsServer();

    /// <summary>
    /// Availability is authority-owned state. The server decides who arms up and when the
    /// crate returns; clients only render the result, so a crate cannot be claimed twice.
    /// </summary>
    private void PublishAvailability(bool available)
    {
        if (Multiplayer.HasMultiplayerPeer())
            Rpc(nameof(SetAvailabilityRpc), available);
        else
            SetAvailability(available);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SetAvailabilityRpc(bool available)
    {
        SetAvailability(available);
    }

    private void SetAvailability(bool available)
    {
        _isAvailable = available;
        if (_visual != null)
            _visual.Visible = available;
        SetDeferred(PropertyName.Monitoring, available);
        _respawnTimer = available ? 0.0f : RespawnTimeSeconds;
    }

    private void OnBodyEntered(Node body)
    {
        if (!_isAvailable || !IsAuthority)
            return;

        if (body is Kart kart)
        {
            int peerId = kart.OwnerPeerId;
            var currentWeapon = GameManager.Instance?.GetPlayerWeapon(peerId);
            if (currentWeapon != null && currentWeapon.Class == WeaponType && !currentWeapon.IsDepleted)
            {
                // Already carrying max ammo of this weapon
                return;
            }

            int ammo = WeaponType == GameManager.WeaponClass.Rocket ? 3 : 20;
            var newWeapon = new GameManager.Weapon
            {
                Class = WeaponType,
                Ammo = ammo
            };
            GameManager.Instance?.SetPlayerWeapon(peerId, newWeapon);

            if (kart.IsLocalPlayer)
                AudioManager.Instance?.PlayLocal(AudioManager.Cue.WeaponPickup);
            else
                AudioManager.Instance?.PlayWorld(AudioManager.Cue.WeaponPickup, GlobalPosition);

            string weaponName = WeaponType == GameManager.WeaponClass.Rocket ? "ROCKETS" : "ASSAULT CANNON";
            kart.TriggerSpeechBubble($"ARMED: {weaponName} [{ammo}]");

            PublishAvailability(false);
        }
    }
}
