using Godot;
using System.Collections.Generic;

/// <summary>
/// Drive-through economy-based repair shop from the Tactical Taxis GDD.
/// Karts that come to a complete stop inside the Area3D are disabled for
/// `RepairDuration` seconds and then billed a flat + per-HP fee that restores
/// their vehicle to full health via <see cref="GameManager.TryPurchaseRepair"/>.
///
/// Repair is refused while the kart is carrying a passenger so a driver can't
/// duck the bail-out damage penalty by driving past the depot.
///
/// Server-authoritative; solo runs through the IsServer() branch.
/// </summary>
public partial class RepairShop : Area3D
{
    [Export] public float RepairDuration = 3.0f;
    [Export] public int BaseServiceFee = 25;
    [Export] public int PerMissingHealthCost = 1;
    [Export] public int MaxRepairCost = 200;
    [Export] public float StopSpeedThreshold = 0.8f;

    private const float PromptHideGracePeriod = 0.35f;

    private readonly Dictionary<int, ActiveRepair> _activeRepairs = new();
    private CollisionShape3D _collisionShape;
    private MeshInstance3D _progressBarFill;
    private MeshInstance3D _progressBarBacking;
    private Label3D _statusLabel;

    private struct ActiveRepair
    {
        public float Elapsed;
        public int Cost;
        public int QuotedHealth;
        public bool PassengerOnEntry;
    }

    public override void _Ready()
    {
        Monitoring = true;
        Monitorable = false;
        CollisionLayer = 0;
        CollisionMask = 1; // Detect karts (Layer 1, matches PickupZone)

        _collisionShape = new CollisionShape3D { Name = "CollisionShape" };
        // Wide but short box — matches drive-through geometry, not a passenger cylinder.
        _collisionShape.Shape = new BoxShape3D { Size = new Vector3(5.0f, 2.5f, 8.0f) };
        AddChild(_collisionShape);

        BuildVisual();

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void BuildVisual()
    {
        var padMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.04f, 0.10f, 0.14f),
            EmissionEnabled = true,
            Emission = new Color(0.10f, 0.62f, 0.78f),
            Roughness = 0.75f
        };
        var accentMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.0f, 0.92f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.0f, 0.92f, 1.0f) * 0.85f
        };
        var headerMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.78f, 0.20f),
            EmissionEnabled = true,
            Emission = new Color(0.95f, 0.45f, 0.05f) * 0.70f
        };

        AddChild(new MeshInstance3D
        {
            Name = "ShopPad",
            Mesh = new PlaneMesh { Size = new Vector2(11.0f, 16.0f) },
            MaterialOverride = padMaterial,
            Position = Vector3.Up * 0.06f
        });

        for (int side = -1; side <= 1; side += 2)
        {
            AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "ShopPostLeft" : "ShopPostRight",
                Mesh = new BoxMesh { Size = new Vector3(0.30f, 4.6f, 0.30f) },
                MaterialOverride = accentMaterial,
                Position = new Vector3(side * 5.5f, 2.3f, 6.5f)
            });
        }

        AddChild(new MeshInstance3D
        {
            Name = "ShopHeader",
            Mesh = new BoxMesh { Size = new Vector3(11.6f, 0.42f, 0.42f) },
            MaterialOverride = headerMaterial,
            Position = new Vector3(0.0f, 4.55f, 6.5f)
        });

        // Progress bar: backing + fill that scales with repair progress.
        _progressBarBacking = new MeshInstance3D
        {
            Name = "ProgressBarBacking",
            Mesh = new BoxMesh { Size = new Vector3(4.0f, 0.18f, 0.18f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.05f, 0.05f, 0.10f),
                EmissionEnabled = true,
                Emission = new Color(0.02f, 0.02f, 0.05f)
            },
            Position = new Vector3(0.0f, 3.55f, 6.5f),
            Visible = false
        };
        AddChild(_progressBarBacking);

        _progressBarFill = new MeshInstance3D
        {
            Name = "ProgressBarFill",
            Mesh = new BoxMesh { Size = new Vector3(3.9f, 0.16f, 0.16f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 1.0f, 0.45f),
                EmissionEnabled = true,
                Emission = new Color(0.10f, 1.0f, 0.45f) * 0.90f
            },
            Position = new Vector3(0.0f, 3.55f, 6.51f),
            Visible = false
        };
        AddChild(_progressBarFill);

        var font = GD.Load<Font>("res://assets/fonts/VT323-Regular.ttf");
        _statusLabel = new Label3D
        {
            Name = "StatusLabel",
            Text = string.Empty,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.014f,
            Modulate = new Color(0.95f, 0.95f, 0.45f),
            OutlineModulate = Colors.Black,
            Position = new Vector3(0.0f, 5.4f, 6.5f)
        };
        if (font != null)
        {
            _statusLabel.Font = font;
            _statusLabel.FontSize = 44;
        }
        else
        {
            _statusLabel.FontSize = 32;
        }
        AddChild(_statusLabel);
    }

    public override void _Process(double delta)
    {
        if (!Multiplayer.IsServer())
            return;

        float dt = (float)delta;
        bool anyActive = false;

        // Sweep stale entries (kart despawned).
        var stale = new List<int>();
        foreach (var kv in _activeRepairs)
        {
            Kart kart = GameManager.Instance?.GetKart(kv.Key);
            if (kart == null || !GodotObject.IsInstanceValid(kart))
                stale.Add(kv.Key);
        }
        foreach (int id in stale)
            _activeRepairs.Remove(id);

        // Tick active repairs.
        var completed = new List<int>();
        foreach (var kv in _activeRepairs)
        {
            anyActive = true;
            ActiveRepair repair = kv.Value;
            repair.Elapsed += dt;
            _activeRepairs[kv.Key] = repair;

            if (repair.Elapsed >= RepairDuration)
                completed.Add(kv.Key);
        }

        foreach (int id in completed)
        {
            ActiveRepair repair = _activeRepairs[id];
            _activeRepairs.Remove(id);

            Kart kart = GameManager.Instance?.GetKart(id);
            if (kart != null && GodotObject.IsInstanceValid(kart))
            {
                // Bail-out protection: if a passenger boarded mid-repair, refund nothing
                // and refuse to bill. The driver took the repair knowing the rules.
                if (kart.ActivePassenger.HasValue && !repair.PassengerOnEntry)
                {
                    GD.Print($"RepairShop: peer {id} boarded mid-repair; refunding cost.");
                }
                else
                {
                    if (GameManager.Instance != null && GameManager.Instance.TryPurchaseRepair(id, repair.Cost))
                    {
                        kart.BroadcastFareCompletedAudio();
                        kart.TriggerSpeechBubble($"PIT STOP — ${repair.Cost}");
                        GD.Print($"RepairShop: peer {id} repaired for ${repair.Cost}.");
                    }
                    else
                    {
                        kart.TriggerSpeechBubble("REPAIR DENIED — INSUFFICIENT CASH");
                        GD.Print($"RepairShop: peer {id} could not afford ${repair.Cost}.");
                    }
                }

                kart.SetControlsEnabled(true);
            }
        }

        UpdateVisual(anyActive);
    }

    private void UpdateVisual(bool anyActive)
    {
        if (_statusLabel == null) return;

        if (!anyActive)
        {
            // Show a passive prompt when at least one kart is overlapping the zone
            // and is currently stopped + eligible.
            bool anyEligible = false;
            string promptText = string.Empty;
            foreach (var kart in OverlappingKarts)
            {
                if (!IsInstanceValid(kart)) continue;
                if (kart.LinearVelocity.Length() >= StopSpeedThreshold) continue;
                if (kart.ActivePassenger.HasValue) continue;

                int peerId = kart.OwnerPeerId;
                if (GameManager.Instance == null) continue;
                if (GameManager.Instance.GetPlayerHealth(peerId) >= 100) continue;

                int cost = ComputeCost(peerId);
                if (GameManager.Instance.GetPlayerMoney(peerId) < cost) continue;

                anyEligible = true;
                promptText = $"PIT STOP — STOP FOR REPAIR (${cost})";
                break;
            }

            _statusLabel.Text = anyEligible ? promptText : string.Empty;
            _progressBarBacking.Visible = false;
            _progressBarFill.Visible = false;
            return;
        }

        // Show progress for the active repair (single-repair-per-shop assumption).
        foreach (var kv in _activeRepairs)
        {
            float t = Mathf.Clamp(kv.Value.Elapsed / RepairDuration, 0.0f, 1.0f);
            _progressBarBacking.Visible = true;
            _progressBarFill.Visible = true;

            float fullWidth = 3.9f;
            float currentWidth = Mathf.Max(0.001f, fullWidth * t);
            _progressBarFill.Mesh = new BoxMesh
            {
                Size = new Vector3(currentWidth, 0.16f, 0.16f)
            };
            // Re-anchor fill so it grows from the left edge, not the centre.
            float offset = -(fullWidth - currentWidth) * 0.5f;
            _progressBarFill.Position = new Vector3(offset, 3.55f, 6.51f);

            float remaining = Mathf.Max(0.0f, RepairDuration - kv.Value.Elapsed);
            _statusLabel.Text = $"REPAIRING... {remaining:0.0}s";
            return;
        }
    }

    // Public so HUD can read state without subscribing to signals.
    public bool TryGetPromptForLocalKart(out string text, out bool inProgress, out float progress)
    {
        text = string.Empty;
        inProgress = false;
        progress = 0.0f;

        if (GameManager.Instance == null) return false;
        Kart localKart = GameManager.Instance.GetKart(1);
        if (localKart == null || !GodotObject.IsInstanceValid(localKart)) return false;
        if (!IsInstanceValid(this)) return false;

        if (_activeRepairs.TryGetValue(1, out ActiveRepair active))
        {
            inProgress = true;
            progress = Mathf.Clamp(active.Elapsed / RepairDuration, 0.0f, 1.0f);
            text = $"REPAIRING... {Mathf.Max(0.0f, RepairDuration - active.Elapsed):0.0}s";
            return true;
        }

        // Not actively repairing — check eligibility for the prompt.
        if (localKart.LinearVelocity.Length() >= StopSpeedThreshold) return false;
        if (localKart.ActivePassenger.HasValue) return false;
        if (GameManager.Instance.GetPlayerHealth(1) >= 100) return false;

        int cost = ComputeCost(1);
        if (GameManager.Instance.GetPlayerMoney(1) < cost) return false;

        text = $"PIT STOP — STOP FOR REPAIR (${cost})";
        return true;
    }

    private readonly HashSet<Kart> OverlappingKarts = new();

    private void OnBodyEntered(Node body)
    {
        if (body is Kart kart && !OverlappingKarts.Contains(kart))
        {
            OverlappingKarts.Add(kart);
            kart.PlayPickupEnterAudio();
            GD.Print($"RepairShop: Kart {kart.Name} entered zone.");
        }
    }

    private void OnBodyExited(Node body)
    {
        if (body is Kart kart)
        {
            OverlappingKarts.Remove(kart);
            _activeRepairs.Remove(kart.OwnerPeerId);
            Kart liveKart = GameManager.Instance?.GetKart(kart.OwnerPeerId);
            if (liveKart != null && GodotObject.IsInstanceValid(liveKart))
                liveKart.SetControlsEnabled(true);
            GD.Print($"RepairShop: Kart {kart.Name} exited zone.");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer()) return;

        float dt = (float)delta;

        foreach (Kart kart in OverlappingKarts)
        {
            if (!IsInstanceValid(kart)) continue;

            int peerId = kart.OwnerPeerId;

            // A kart already in an active repair: just keep ticking — done in _Process.
            if (_activeRepairs.ContainsKey(peerId)) continue;

            // Need: stopped, no passenger, damaged, affordable.
            if (kart.LinearVelocity.Length() >= StopSpeedThreshold) continue;
            if (kart.ActivePassenger.HasValue) continue;
            if (GameManager.Instance == null) continue;

            int health = GameManager.Instance.GetPlayerHealth(peerId);
            if (health >= 100) continue;

            int cost = ComputeCost(peerId);
            if (GameManager.Instance.GetPlayerMoney(peerId) < cost) continue;

            // Start the repair.
            kart.SetControlsEnabled(false);
            kart.ClearInput();
            kart.TriggerSpeechBubble($"HOLD STILL — ${cost}");
            _activeRepairs[peerId] = new ActiveRepair
            {
                Elapsed = 0.0f,
                Cost = cost,
                QuotedHealth = health,
                PassengerOnEntry = false
            };
            GD.Print($"RepairShop: peer {peerId} repair started for ${cost}.");
        }
    }

    private int ComputeCost(int peerId)
    {
        if (GameManager.Instance == null) return 0;
        int health = GameManager.Instance.GetPlayerHealth(peerId);
        int missing = Mathf.Max(0, 100 - health);
        int raw = BaseServiceFee + missing * PerMissingHealthCost;
        return Mathf.Min(raw, MaxRepairCost);
    }
}
