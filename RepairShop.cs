using Godot;
using System.Collections.Generic;

/// <summary>
/// Drive-through economy-based repair shop from the Tactical Taxis GDD.
/// Sits on a city block and bridges two parallel streets as a single one-lane
/// straight connector. The first kart that comes to a complete stop inside the
/// bay is locked in for <see cref="RepairDuration"/> seconds and then billed a
/// flat + per-HP fee that restores Health to 100 via <see cref="GameManager.TryPurchaseRepair"/>.
///
/// Single bay: only one kart can be repaired at a time. A second kart that
/// enters while a repair is in progress is shown a "BUSY" prompt and ignored.
///
/// Repair is refused while a kart is carrying a passenger so a driver can't
/// duck the bail-out damage penalty by driving through the depot.
///
/// Server-authoritative; solo runs through the IsServer() branch.
/// </summary>
public partial class RepairShop : Area3D
{
    [Export] public float RepairDuration = 3.0f;
    [Export] public int BaseServiceFee = 25;
    [Export] public int PerMissingHealthCost = 1;
    [Export] public int MaxRepairCost = 200;
    // Slightly forgiving stop threshold: a slow roll counts as stopped so
    // a kart easing into the bay doesn't have to be perfectly motionless.
    [Export] public float StopSpeedThreshold = 2.5f;

    /// <summary>Width of the shop's interior lane, perpendicular to the driveway. ~2/3 of a street.</summary>
    [Export] public float LaneWidth = 6.0f;
    /// <summary>Length of the shop's interior lane along the driveway axis.</summary>
    [Export] public float DrivewayLength = 24.0f;

    private const float ProgressBarHeight = 4.4f;

    private struct ActiveRepair
    {
        public int PeerId;
        public float Elapsed;
        public int Cost;
        public bool PassengerOnEntry;
    }

    private readonly HashSet<Kart> _overlappingKarts = new();
    private ActiveRepair? _currentRepair;
    private bool _kartsChanged;

    private CollisionShape3D _collisionShape;
    private MeshInstance3D _roofBeam;
    private MeshInstance3D _roofSign;
    private MeshInstance3D _progressBarBacking;
    private MeshInstance3D _progressBarFill;
    private Label3D _statusLabel;

    public override void _Ready()
    {
        Monitoring = true;
        Monitorable = false;
        CollisionLayer = 0;
        CollisionMask = 1; // Detect karts (Layer 1, matches PickupZone)

        _collisionShape = new CollisionShape3D { Name = "CollisionShape" };
        // Driveway runs along the shop's local +Z axis; width along X.
        _collisionShape.Shape = new BoxShape3D
        {
            Size = new Vector3(LaneWidth, 2.8f, DrivewayLength)
        };
        _collisionShape.Position = new Vector3(0.0f, 1.4f, 0.0f);
        AddChild(_collisionShape);

        BuildVisual();

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
    }

    private void BuildVisual()
    {
        var groundMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.04f, 0.10f, 0.14f),
            EmissionEnabled = true,
            Emission = new Color(0.06f, 0.48f, 0.62f),
            Roughness = 0.85f
        };
        var postMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.05f, 0.10f),
            EmissionEnabled = true,
            Emission = new Color(0.02f, 0.02f, 0.04f),
            Roughness = 0.55f,
            Metallic = 0.6f
        };
        var accentMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.0f, 0.92f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.0f, 0.92f, 1.0f) * 0.90f
        };
        var signMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.95f, 0.78f, 0.20f),
            EmissionEnabled = true,
            Emission = new Color(0.95f, 0.45f, 0.05f) * 0.85f
        };
        var progressMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.10f, 1.0f, 0.45f),
            EmissionEnabled = true,
            Emission = new Color(0.10f, 1.0f, 0.45f) * 0.90f
        };
        var progressBackingMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.05f, 0.10f),
            EmissionEnabled = true,
            Emission = new Color(0.02f, 0.02f, 0.05f)
        };

        // Ground strip showing the lane.
        AddChild(new MeshInstance3D
        {
            Name = "ShopLane",
            Mesh = new PlaneMesh
            {
                Size = new Vector2(LaneWidth, DrivewayLength),
                Orientation = PlaneMesh.OrientationEnum.Y
            },
            MaterialOverride = groundMaterial,
            Position = new Vector3(0.0f, 0.04f, 0.0f)
        });

        // Two canopy posts straddling the lane on -X and +X. Span the full driveway length.
        float postHeight = 4.8f;
        for (int side = -1; side <= 1; side += 2)
        {
            AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "CanopyPostLeft" : "CanopyPostRight",
                Mesh = new BoxMesh { Size = new Vector3(0.42f, postHeight, DrivewayLength * 0.98f) },
                MaterialOverride = postMaterial,
                Position = new Vector3(side * (LaneWidth * 0.5f + 0.18f), postHeight * 0.5f, 0.0f)
            });
        }

        // Roof beam across the lane at the midpoint. Holds the progress bar.
        float beamLength = LaneWidth + 0.6f;
        _roofBeam = new MeshInstance3D
        {
            Name = "RoofBeam",
            Mesh = new BoxMesh { Size = new Vector3(beamLength, 0.30f, 0.30f) },
            MaterialOverride = accentMaterial,
            Position = new Vector3(0.0f, ProgressBarHeight, 0.0f)
        };
        AddChild(_roofBeam);

        // Sign on the roof beam — visible from any approach direction since we
        // make the Label3D billboarded.
        _roofSign = new MeshInstance3D
        {
            Name = "SignPanel",
            Mesh = new BoxMesh { Size = new Vector3(beamLength * 0.9f, 0.6f, 0.10f) },
            MaterialOverride = signMaterial,
            Position = new Vector3(0.0f, ProgressBarHeight + 0.45f, 0.0f)
        };
        AddChild(_roofSign);

        // Progress bar (backing + fill) attached to the underside of the roof beam.
        float barWidth = Mathf.Min(LaneWidth * 0.8f, 5.0f);
        _progressBarBacking = new MeshInstance3D
        {
            Name = "ProgressBarBacking",
            Mesh = new BoxMesh { Size = new Vector3(barWidth, 0.16f, 0.16f) },
            MaterialOverride = progressBackingMaterial,
            Position = new Vector3(0.0f, ProgressBarHeight - 0.25f, 0.0f),
            Visible = false
        };
        AddChild(_progressBarBacking);

        _progressBarFill = new MeshInstance3D
        {
            Name = "ProgressBarFill",
            Mesh = new BoxMesh { Size = new Vector3(barWidth * 0.98f, 0.14f, 0.14f) },
            MaterialOverride = progressMaterial,
            Position = new Vector3(0.0f, ProgressBarHeight - 0.25f, 0.01f),
            Visible = false
        };
        AddChild(_progressBarFill);

        // Floating status label — billboarded so any approach direction reads it.
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
            Position = new Vector3(0.0f, ProgressBarHeight + 1.4f, 0.0f)
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

        // Sweep stale entries (kart despawned mid-repair).
        if (_currentRepair.HasValue)
        {
            int peerId = _currentRepair.Value.PeerId;
            Kart kart = GameManager.Instance?.GetKart(peerId);
            if (kart == null || !GodotObject.IsInstanceValid(kart))
            {
                _currentRepair = null;
            }
        }

        // Tick the active repair.
        bool bayBusy = false;
        if (_currentRepair.HasValue)
        {
            bayBusy = true;
            ActiveRepair r = _currentRepair.Value;
            r.Elapsed += dt;
            _currentRepair = r;

            if (r.Elapsed >= RepairDuration)
            {
                CompleteRepair();
            }
        }

        UpdateVisual(bayBusy);
    }

    private void CompleteRepair()
    {
        if (!_currentRepair.HasValue) return;
        ActiveRepair repair = _currentRepair.Value;
        _currentRepair = null;

        Kart kart = GameManager.Instance?.GetKart(repair.PeerId);
        if (kart == null || !GodotObject.IsInstanceValid(kart))
            return;

        // Bail-out protection: if a passenger boarded mid-repair, refuse to bill.
        if (kart.ActivePassenger.HasValue && !repair.PassengerOnEntry)
        {
            GD.Print($"RepairShop: peer {repair.PeerId} boarded mid-repair; refunding cost.");
        }
        else if (GameManager.Instance != null && GameManager.Instance.TryPurchaseRepair(repair.PeerId, repair.Cost))
        {
            kart.BroadcastFareCompletedAudio();
            kart.TriggerSpeechBubble($"PIT STOP — ${repair.Cost}");
            GD.Print($"RepairShop: peer {repair.PeerId} repaired for ${repair.Cost}.");
        }
        else
        {
            kart.TriggerSpeechBubble("REPAIR DENIED — INSUFFICIENT CASH");
            GD.Print($"RepairShop: peer {repair.PeerId} could not afford ${repair.Cost}.");
        }

        kart.SetControlsEnabled(true);
    }

    private string BuildPromptForKart(Kart kart)
    {
        if (GameManager.Instance == null) return string.Empty;
        int peerId = kart.OwnerPeerId;

        if (kart.ActivePassenger.HasValue) return "DROP OFF PASSENGER FIRST";
        if (GameManager.Instance.GetPlayerHealth(peerId) >= 100) return "ALREADY AT FULL HEALTH";

        int cost = ComputeCost(peerId);
        if (GameManager.Instance.GetPlayerMoney(peerId) < cost) return $"NEED ${cost} TO REPAIR";
        if (kart.LinearVelocity.Length() >= StopSpeedThreshold) return "STOP TO REPAIR";

        return $"PIT STOP — REPAIR FOR ${cost}";
    }

    private void UpdateVisual(bool bayBusy)
    {
        if (_statusLabel == null) return;

        if (!bayBusy)
        {
            // Show a passive prompt for any overlapping kart. Pick the first
            // non-local kart if any (so AI karts get visible feedback), else
            // the local one. Keeps the same message set as the HUD pill so
            // the player learns the rules from both sources.
            string promptText = string.Empty;
            Kart fallbackForLocal = null;
            foreach (var kart in _overlappingKarts)
            {
                if (!IsInstanceValid(kart)) continue;
                promptText = BuildPromptForKart(kart);
                if (!string.IsNullOrEmpty(promptText)) break;
                if (kart.OwnerPeerId == 1) fallbackForLocal = kart;
            }
            if (string.IsNullOrEmpty(promptText) && fallbackForLocal != null)
                promptText = BuildPromptForKart(fallbackForLocal);

            _statusLabel.Text = promptText;
            _progressBarBacking.Visible = false;
            _progressBarFill.Visible = false;
            return;
        }

        // Bay busy — show progress.
        ActiveRepair r = _currentRepair!.Value;
        float t = Mathf.Clamp(r.Elapsed / RepairDuration, 0.0f, 1.0f);

        _progressBarBacking.Visible = true;
        _progressBarFill.Visible = true;

        float fullWidth = _progressBarBacking.Mesh is BoxMesh backingBox ? backingBox.Size.X : 4.0f;
        float currentWidth = Mathf.Max(0.001f, fullWidth * t);
        _progressBarFill.Mesh = new BoxMesh
        {
            Size = new Vector3(currentWidth, 0.14f, 0.14f)
        };
        float offset = -(fullWidth - currentWidth) * 0.5f;
        _progressBarFill.Position = new Vector3(offset, _progressBarBacking.Position.Y, _progressBarBacking.Position.Z + 0.01f);

        float remaining = Mathf.Max(0.0f, RepairDuration - r.Elapsed);
        _statusLabel.Text = $"REPAIRING... {remaining:0.0}s";
    }

    /// <summary>
    /// Reads this shop's prompt state for the local player (peer 1).
    /// Always returns a prompt whenever the local kart is overlapping the
    /// shop's collision box, so the player can see what's required of them.
    /// </summary>
    public bool TryGetPromptForLocalKart(out string text, out bool inProgress, out float progress)
    {
        text = string.Empty;
        inProgress = false;
        progress = 0.0f;

        if (GameManager.Instance == null) return false;
        Kart localKart = GameManager.Instance.GetKart(1);
        if (localKart == null || !GodotObject.IsInstanceValid(localKart)) return false;
        if (!IsInstanceValid(this)) return false;
        if (!_overlappingKarts.Contains(localKart)) return false;

        // Active repair for the local kart?
        if (_currentRepair.HasValue && _currentRepair.Value.PeerId == 1)
        {
            inProgress = true;
            progress = Mathf.Clamp(_currentRepair.Value.Elapsed / RepairDuration, 0.0f, 1.0f);
            text = $"REPAIRING... {Mathf.Max(0.0f, RepairDuration - _currentRepair.Value.Elapsed):0.0}s";
            return true;
        }

        // Bay busy for someone else.
        if (_currentRepair.HasValue)
        {
            text = "BAY OCCUPIED — WAIT";
            return true;
        }

        // Bay idle — give feedback based on what's blocking repair.
        if (localKart.ActivePassenger.HasValue)
        {
            text = "DROP OFF PASSENGER FIRST";
            return true;
        }
        if (GameManager.Instance.GetPlayerHealth(1) >= 100)
        {
            text = "ALREADY AT FULL HEALTH";
            return true;
        }
        int cost = ComputeCost(1);
        if (GameManager.Instance.GetPlayerMoney(1) < cost)
        {
            text = $"NEED ${cost} TO REPAIR";
            return true;
        }
        if (localKart.LinearVelocity.Length() >= StopSpeedThreshold)
        {
            text = "STOP TO REPAIR";
            return true;
        }

        text = $"PIT STOP — REPAIR FOR ${cost}";
        return true;
    }

    private void OnBodyEntered(Node body)
    {
        if (body is Kart kart && !_overlappingKarts.Contains(kart))
        {
            _overlappingKarts.Add(kart);
            kart.PlayPickupEnterAudio();
            GD.Print($"RepairShop({Name}): Kart {kart.Name} entered zone.");
        }
    }

    private void OnBodyExited(Node body)
    {
        if (body is Kart kart)
        {
            _overlappingKarts.Remove(kart);

            // If this kart was being repaired, abort cleanly without billing.
            if (_currentRepair.HasValue && _currentRepair.Value.PeerId == kart.OwnerPeerId)
            {
                _currentRepair = null;
                Kart liveKart = GameManager.Instance?.GetKart(kart.OwnerPeerId);
                if (liveKart != null && GodotObject.IsInstanceValid(liveKart))
                    liveKart.SetControlsEnabled(true);
                GD.Print($"RepairShop({Name}): Kart {kart.Name} left mid-repair; cancelled.");
            }
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer()) return;

        // Already repairing — don't accept new work.
        if (_currentRepair.HasValue) return;

        float dt = (float)delta;

        foreach (Kart kart in _overlappingKarts)
        {
            if (!IsInstanceValid(kart)) continue;

            // Need: stopped, no passenger, damaged, affordable.
            if (kart.LinearVelocity.Length() >= StopSpeedThreshold) continue;
            if (kart.ActivePassenger.HasValue) continue;
            if (GameManager.Instance == null) continue;

            int peerId = kart.OwnerPeerId;
            int health = GameManager.Instance.GetPlayerHealth(peerId);
            if (health >= 100) continue;

            int cost = ComputeCost(peerId);
            if (GameManager.Instance.GetPlayerMoney(peerId) < cost) continue;

            // Start the repair.
            kart.SetControlsEnabled(false);
            kart.ClearInput();
            kart.TriggerSpeechBubble($"HOLD STILL — ${cost}");
            _currentRepair = new ActiveRepair
            {
                PeerId = peerId,
                Elapsed = 0.0f,
                Cost = cost,
                PassengerOnEntry = false
            };
            GD.Print($"RepairShop({Name}): peer {peerId} repair started for ${cost}.");
            break; // single bay
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
