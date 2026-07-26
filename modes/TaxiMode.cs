using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class TaxiMode : Node3D
{
    public enum ObjectiveKind
    {
        Pickup,
        Dropoff
    }

    /// <summary>
    /// The one authoritative UI/world-marker objective for a kart.  Keeping
    /// the target and its presentation colour together prevents the HUD and
    /// the world beacons from disagreeing about the current fare state.
    /// </summary>
    public readonly struct ObjectiveTarget
    {
        public ObjectiveKind Kind { get; }
        public Vector3 WorldPosition { get; }
        public float Distance { get; }
        public Color Color { get; }

        public ObjectiveTarget(ObjectiveKind kind, Vector3 worldPosition, float distance, Color color)
        {
            Kind = kind;
            WorldPosition = worldPosition;
            Distance = distance;
            Color = color;
        }
    }

    public enum MatchPhase
    {
        Idle,
        Countdown,
        Active,
        Finished,
        Intermission
    }

    public static TaxiMode Instance { get; private set; }

    public event Action<int, int, int> ScoreboardChanged; // peerId, score, rank
    public event Action<int, Vector3> CheckpointChanged;  // index, position (reused for active destination)
    public event Action<double, bool, int> MatchStateChanged; // timeRemaining, matchActive, winnerPeerId

    [Export] public double MatchDurationSeconds = 180.0;
    [Export] public double CountdownSeconds = 3.0;
    [Export] public int WinningCashTarget = 750;
    [Export] public int EndlessQuotaStep = 250;

    public Vector3 ActiveDestination { get; private set; } = Vector3.Zero;

    private readonly Dictionary<int, int> _scores = new(); // peerId -> cash earned
    private readonly List<PickupZone> _activeZones = new();
    private const int MaxActiveCustomers = 5;
    private int _pickupZoneCounter = 0;
    private double _timeRemaining;
    private double _countdownRemaining;
    private bool _matchActive;
    private int _winnerPeerId;
    private MatchPhase _phase = MatchPhase.Idle;
    private bool _endlessRunActive;
    private int _shiftNumber = 1;
    private int _currentCashQuota;
    private int _totalRunCash;
    private int _lastShiftCash;

    // Track active drop-off destinations per player: peerId -> destination position
    private readonly Dictionary<int, Vector3> _playerDestinations = new();
    // Track active drop-off area nodes per player: peerId -> Area3D node
    private readonly Dictionary<int, Area3D> _playerDropoffAreas = new();
    private readonly Dictionary<int, Kart> _settlingDropoffs = new();
    private readonly Dictionary<int, float> _dropoffSettleSeconds = new();
    private const float DropoffSettleDuration = 0.5f;
    private const float DropoffSettleSpeed = 2.2f;

    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
        _timeRemaining = MatchDurationSeconds;
        PublishLocalEvents();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer())
            return;

        if (_phase == MatchPhase.Countdown)
        {
            _countdownRemaining = Math.Max(0.0, _countdownRemaining - delta);
            if (_countdownRemaining <= 0.0)
                BeginActiveMatch();
            else
                BroadcastMatchState();
            return;
        }

        if (_phase != MatchPhase.Active)
            return;

        UpdateDropoffSettles((float)delta);

        _timeRemaining = Math.Max(0.0, _timeRemaining - delta);
        if (_timeRemaining <= 0.0)
        {
            if (_endlessRunActive)
                EndEndlessRun();
            else
                EndMatch(FindLeader());
        }
        else
            BroadcastMatchState();
    }

    public override void _Process(double delta)
    {
        UpdateDropoffMarkerVisuals();
    }

    public void StartEndlessRun()
    {
        if (!Multiplayer.IsServer())
            return;

        _endlessRunActive = true;
        _shiftNumber = 1;
        _currentCashQuota = WinningCashTarget;
        _totalRunCash = 0;
        _lastShiftCash = 0;
        StartMatch();
    }

    public void ContinueEndlessRun()
    {
        if (!Multiplayer.IsServer() || !_endlessRunActive || _phase != MatchPhase.Intermission)
            return;

        _shiftNumber++;
        _currentCashQuota = WinningCashTarget + ((_shiftNumber - 1) * EndlessQuotaStep);
        StartMatch();
    }

    public void StartMatch()
    {
        if (!Multiplayer.IsServer())
            return;

        _scores.Clear();
        _playerDestinations.Clear();
        ClearDropoffAreas();
        ClearPickupZones();

        foreach (int peerId in GetMatchPlayerIds())
            _scores[peerId] = 0;

        _timeRemaining = MatchDurationSeconds;
        _countdownRemaining = Math.Max(0.0, CountdownSeconds);
        _winnerPeerId = 0;
        _matchActive = false;
        _phase = _countdownRemaining > 0.0 ? MatchPhase.Countdown : MatchPhase.Active;

        SpawnPickupZones();
        GameManager.Instance?.SetAllKartControlsEnabled(_phase == MatchPhase.Active);
        BroadcastFullState();

        if (_phase == MatchPhase.Active)
            BeginActiveMatch();
    }

    public void ResetMatch()
    {
        _matchActive = false;
        _phase = MatchPhase.Idle;
        _winnerPeerId = 0;
        _timeRemaining = MatchDurationSeconds;
        _countdownRemaining = 0.0;
        _endlessRunActive = false;
        _shiftNumber = 1;
        _currentCashQuota = WinningCashTarget;
        _totalRunCash = 0;
        _lastShiftCash = 0;
        _scores.Clear();
        _playerDestinations.Clear();
        ClearDropoffAreas();
        ClearPickupZones();
        ActiveDestination = Vector3.Zero;
        GameManager.Instance?.SetAllKartControlsEnabled(true);
        PublishLocalEvents();
    }

    private void BeginActiveMatch()
    {
        _countdownRemaining = 0.0;
        _matchActive = true;
        _phase = MatchPhase.Active;
        GameManager.Instance?.SetAllKartControlsEnabled(true);
        BroadcastFullState();
    }

    public void RegisterPlayer(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        if (!_scores.ContainsKey(peerId))
            _scores[peerId] = 0;

        BroadcastFullState();
    }

    public void RemovePlayer(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        _scores.Remove(peerId);
        _playerDestinations.Remove(peerId);
        if (_playerDropoffAreas.TryGetValue(peerId, out Area3D area) && IsInstanceValid(area))
        {
            area.QueueFree();
        }
        _playerDropoffAreas.Remove(peerId);

        BroadcastFullState();
    }

    public void SyncToPeer(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        if (peerId == Multiplayer.GetUniqueId())
            return;

        int[] peerIds = _scores.Keys.OrderBy(id => id).ToArray();
        int[] scores = peerIds.Select(id => _scores[id]).ToArray();
        
        // Sync active destination if any
        Vector3 dest = _playerDestinations.TryGetValue(peerId, out Vector3 value) ? value : Vector3.Zero;

        RpcId(peerId, nameof(SyncFullStateRpc), _timeRemaining, _matchActive, _winnerPeerId, (int)_phase, _countdownRemaining, dest, peerIds, scores);
    }

    public IReadOnlyDictionary<int, int> Scores => _scores;
    public bool MatchActive => _matchActive;
    public double TimeRemaining => _timeRemaining;
    public double CountdownRemaining => _countdownRemaining;
    public MatchPhase Phase => _phase;
    public int WinnerPeerId => _winnerPeerId;
    public bool EndlessRunActive => _endlessRunActive;
    public int ShiftNumber => _shiftNumber;
    public int CurrentCashQuota => _endlessRunActive ? _currentCashQuota : WinningCashTarget;
    public int TotalRunCash => _totalRunCash;
    public int LastShiftCash => _lastShiftCash;

    public int GetPhaseValue() => (int)_phase;
    public int GetWinnerPeerId() => _winnerPeerId;
    public int GetShiftNumber() => _shiftNumber;
    public int GetCurrentCashQuota() => CurrentCashQuota;
    public int GetTotalRunCash() => _totalRunCash;

    public bool TryGetObjectiveForKart(Kart kart, out ObjectiveTarget target)
    {
        target = default;
        if (kart == null || !GodotObject.IsInstanceValid(kart))
            return false;

        int peerId = kart.OwnerPeerId;
        if (kart.ActivePassenger.HasValue)
        {
            Vector3 destination = GetPlayerDestination(peerId);
            if (destination == Vector3.Zero)
                destination = ActiveDestination;
            if (destination == Vector3.Zero)
                return false;

            target = new ObjectiveTarget(
                ObjectiveKind.Dropoff,
                destination,
                kart.GlobalPosition.DistanceTo(destination),
                WealthColor(kart.ActivePassenger.Value.Wealth));
            return true;
        }

        int health = GameManager.Instance?.GetPlayerHealth(peerId) ?? 100;
        PickupZone nearest = null;
        float nearestDistance = float.MaxValue;
        foreach (PickupZone zone in _activeZones)
        {
            if (!GodotObject.IsInstanceValid(zone) || zone.IsQueuedForDeletion())
                continue;
            if (health < 100 - zone.MaxAcceptableDamage)
                continue;

            float distance = kart.GlobalPosition.DistanceTo(zone.GlobalPosition);
            if (distance < nearestDistance)
            {
                nearest = zone;
                nearestDistance = distance;
            }
        }

        if (nearest == null)
            return false;

        target = new ObjectiveTarget(ObjectiveKind.Pickup, nearest.GlobalPosition, nearestDistance, WealthColor(nearest.Wealth));
        return true;
    }

    public static Color WealthColor(GameManager.CustomerWealth wealth)
    {
        return wealth switch
        {
            GameManager.CustomerWealth.Medium => new Color(0.10f, 0.86f, 0.95f, 1.0f),
            GameManager.CustomerWealth.High => new Color(0.93f, 0.16f, 0.50f, 1.0f),
            _ => new Color(0.96f, 0.72f, 0.18f, 1.0f)
        };
    }

    private void SpawnPickupZones()
    {
        if (TrackBuilder.Instance == null)
        {
            GD.PushWarning("TaxiMode: TrackBuilder.Instance is null, cannot spawn pickup zones!");
            return;
        }

        _activeZones.RemoveAll(z => !IsInstanceValid(z) || z.IsQueuedForDeletion());

        int spawned = 0;
        while (_activeZones.Count < MaxActiveCustomers)
        {
            if (!SpawnSinglePickupZone())
                break;
            spawned++;
        }

        GD.Print($"TaxiMode: Initialized {spawned} pickup zones. Total active: {_activeZones.Count}");
    }

    private bool SpawnSinglePickupZone()
    {
        var intersections = TrackBuilder.Instance.IntersectionPositions;
        if (intersections.Count == 0) return false;

        var available = new System.Collections.Generic.List<Vector3>();
        foreach (Vector3 pos in intersections)
        {
            if (pos.Length() < 35.0f) continue;

            bool occupied = false;
            foreach (var zone in _activeZones)
            {
                if (IsInstanceValid(zone) && !zone.IsQueuedForDeletion() && zone.GlobalPosition.DistanceSquaredTo(pos) < 1.0f)
                {
                    occupied = true;
                    break;
                }
            }
            if (occupied) continue;

            foreach (var dest in _playerDestinations.Values)
            {
                if (dest.DistanceSquaredTo(pos) < 1.0f)
                {
                    occupied = true;
                    break;
                }
            }
            if (occupied) continue;

            available.Add(pos);
        }

        if (available.Count == 0) return false;

        Vector3 spawnPos = available[GD.RandRange(0, available.Count - 1)];
        float dist = spawnPos.Length();
        GameManager.CustomerDistance customerDist;
        GameManager.CustomerWealth customerWealth;
        int maxDmg;
        int groupSize = GD.RandRange(1, 3);
        float loadTime = 1.0f + groupSize * 0.5f;

        if (dist < 90.0f)
        {
            customerDist = GameManager.CustomerDistance.Near;
            customerWealth = GameManager.CustomerWealth.Low;
            maxDmg = 80;
        }
        else if (dist < 185.0f)
        {
            customerDist = GameManager.CustomerDistance.Moderate;
            customerWealth = GameManager.CustomerWealth.Medium;
            maxDmg = 50;
        }
        else
        {
            customerDist = GameManager.CustomerDistance.Far;
            customerWealth = GameManager.CustomerWealth.High;
            maxDmg = 30;
        }

        var newZone = new PickupZone
        {
            Name = $"PickupZone_{_pickupZoneCounter++}",
            Distance = customerDist,
            Wealth = customerWealth,
            MaxAcceptableDamage = maxDmg,
            GroupSize = groupSize,
            LoadTime = loadTime,
            Position = spawnPos + Vector3.Up * 0.1f
        };

        AddChild(newZone);
        _activeZones.Add(newZone);
        return true;
    }

    private void ClearPickupZones()
    {
        foreach (var zone in _activeZones)
        {
            if (IsInstanceValid(zone))
                zone.QueueFree();
        }
        _activeZones.Clear();
    }

    private void ClearDropoffAreas()
    {
        foreach (var area in _playerDropoffAreas.Values)
        {
            if (IsInstanceValid(area))
                area.QueueFree();
        }
        _playerDropoffAreas.Clear();
    }

    private void UpdateDropoffMarkerVisuals()
    {
        int localPeerId = Multiplayer.HasMultiplayerPeer() ? Multiplayer.GetUniqueId() : 1;
        Kart localKart = GameManager.Instance?.GetKart(localPeerId);
        if (localKart == null || !GodotObject.IsInstanceValid(localKart))
            return;

        foreach (KeyValuePair<int, Area3D> entry in _playerDropoffAreas)
        {
            Area3D area = entry.Value;
            if (!GodotObject.IsInstanceValid(area))
                continue;
            Node3D visual = area.GetNodeOrNull<Node3D>("Visual");
            if (visual == null)
                continue;

            ObjectiveTarget target = default;
            bool localObjective = entry.Key == localPeerId && TryGetObjectiveForKart(localKart, out target) &&
                target.Kind == ObjectiveKind.Dropoff && target.WorldPosition.DistanceSquaredTo(area.GlobalPosition) < 0.01f;
            visual.Visible = localObjective;
            if (!localObjective)
                continue;

            float distance = localKart.GlobalPosition.DistanceTo(area.GlobalPosition);
            float alpha = distance <= 6.0f ? 0.12f : distance <= 10.0f ? 0.18f : 0.30f;
            foreach (Node child in visual.GetChildren())
            {
                if (child is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D material)
                {
                    material.AlbedoColor = new Color(target.Color.R, target.Color.G, target.Color.B, alpha);
                    material.Emission = target.Color * (0.25f + alpha);
                }
                else if (child is OmniLight3D light)
                    light.LightEnergy = 0.45f;
            }
        }
    }

    // Called by PickupZone when a player successfully boards a passenger
    public void OnPassengerBoarded(int peerId, GameManager.CustomerData customer)
    {
        if (!Multiplayer.IsServer() || _phase != MatchPhase.Active)
            return;

        // Choose a random intersection for drop-off destination
        Vector3 dest = PickRandomDestination(peerId, customer.Distance);
        _playerDestinations[peerId] = dest;

        // Create Drop-off Beacon at destination
        SpawnDropoffBeacon(peerId, dest, customer.Wealth);

        NotifyDestination(peerId, dest);

        GD.Print($"TaxiMode: Peer {peerId} boarded passenger group of {customer.GroupSize}. Destination selected at {dest}");

        // Cleanup destroyed zones and spawn a new one to maintain the pool
        _activeZones.RemoveAll(z => !IsInstanceValid(z) || z.IsQueuedForDeletion());
        while (_activeZones.Count < MaxActiveCustomers)
        {
            if (!SpawnSinglePickupZone()) break;
        }
    }

    private Vector3 PickRandomDestination(int peerId, GameManager.CustomerDistance distanceType)
    {
        if (TrackBuilder.Instance == null || TrackBuilder.Instance.IntersectionPositions.Count == 0)
            return new Vector3(0, 0.5f, 0);

        var playerKart = GameManager.Instance?.GetKart(peerId);
        Vector3 currentPos = playerKart != null ? playerKart.GlobalPosition : Vector3.Zero;

        // Filter intersections based on distance bounds
        float minRange = 40.0f;
        float maxRange = 100.0f;

        switch (distanceType)
        {
            case GameManager.CustomerDistance.Near:
                minRange = 35.0f;
                maxRange = 85.0f;
                break;
            case GameManager.CustomerDistance.Moderate:
                minRange = 85.0f;
                maxRange = 175.0f;
                break;
            case GameManager.CustomerDistance.Far:
                minRange = 175.0f;
                maxRange = 500.0f;
                break;
        }

        var candidates = TrackBuilder.Instance.IntersectionPositions
            .Where(pos => {
                float dist = pos.DistanceTo(currentPos);
                return dist >= minRange && dist <= maxRange;
            })
            .ToList();

        if (candidates.Count == 0)
        {
            // Fallback
            candidates = TrackBuilder.Instance.IntersectionPositions.ToList();
        }

        int index = GD.RandRange(0, candidates.Count - 1);
        return candidates[index];
    }

    private void SpawnDropoffBeacon(int peerId, Vector3 position, GameManager.CustomerWealth wealth)
    {
        // Remove existing area for this player if any
        if (_playerDropoffAreas.TryGetValue(peerId, out Area3D oldArea) && IsInstanceValid(oldArea))
        {
            oldArea.QueueFree();
        }

        // Color based on wealth
        Color markerColor = WealthColor(wealth);
        Color beaconColor = new(markerColor.R, markerColor.G, markerColor.B, 0.30f);

        // Create Drop-off Area3D
        var area = new Area3D { Name = $"DropoffArea_{peerId}", Monitoring = true, Monitorable = false };
        var shape = new CollisionShape3D { Name = "CollisionShape" };
        shape.Shape = new CylinderShape3D { Radius = 6.0f, Height = 4.0f };
        area.AddChild(shape);

        // A thin perimeter and brackets retain the interaction radius without
        // painting an opaque disc over the road or the arriving kart.
        var visual = new Node3D { Name = "Visual", Visible = IsLocalPlayer(peerId) };
        area.AddChild(visual);

        var beaconMesh = new TorusMesh
        {
            InnerRadius = 5.55f,
            OuterRadius = 6.0f,
            Rings = 8,
            RingSegments = 32
        };
        var beaconMaterial = new StandardMaterial3D
        {
            AlbedoColor = beaconColor,
            EmissionEnabled = true,
            Emission = markerColor * 0.45f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        var ring = new MeshInstance3D
        {
            Mesh = beaconMesh,
            MaterialOverride = beaconMaterial,
            Position = new Vector3(0, 0.12f, 0)
        };
        visual.AddChild(ring);

        for (int index = 0; index < 4; index++)
        {
            float angle = index * Mathf.Pi * 0.5f;
            Vector3 direction = new(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
            visual.AddChild(new MeshInstance3D
            {
                Name = $"DropoffBracket{index}",
                Mesh = new BoxMesh { Size = new Vector3(index % 2 == 0 ? 0.8f : 2.25f, 0.12f, index % 2 == 0 ? 2.25f : 0.8f) },
                MaterialOverride = beaconMaterial,
                Position = direction * 5.55f + Vector3.Up * 0.14f
            });
        }

        var light = new OmniLight3D
        {
            LightColor = beaconColor,
            LightEnergy = 0.45f,
            OmniRange = 10.0f,
            Position = new Vector3(0, 4.0f, 0)
        };
        visual.AddChild(light);

        var arrow = new HolographicArrow
        {
            Name = "HolographicArrow",
            ArrowColor = markerColor,
            Position = new Vector3(0.0f, 4.0f, 0.0f)
        };
        visual.AddChild(arrow);

        area.BodyEntered += (body) => OnDropoffAreaEntered(body, peerId);
        area.BodyExited += (body) => OnDropoffAreaExited(body, peerId);

        AddChild(area);
        area.GlobalPosition = position;
        _playerDropoffAreas[peerId] = area;
    }

    private void OnDropoffAreaEntered(Node body, int peerId)
    {
        if (!Multiplayer.IsServer() || !_matchActive)
            return;

        if (body is not Kart kart || kart.OwnerPeerId != peerId)
            return;

        // Player arrived: the server confirms a brief, low-speed settle before payout.
        if (kart.ActivePassenger.HasValue)
        {
            _settlingDropoffs[peerId] = kart;
            _dropoffSettleSeconds[peerId] = 0.0f;
        }
    }

    private void OnDropoffAreaExited(Node body, int peerId)
    {
        if (body is Kart kart && kart.OwnerPeerId == peerId)
        {
            _settlingDropoffs.Remove(peerId);
            _dropoffSettleSeconds.Remove(peerId);
        }
    }

    private void UpdateDropoffSettles(float dt)
    {
        foreach (int peerId in _settlingDropoffs.Keys.ToArray())
        {
            Kart kart = _settlingDropoffs[peerId];
            if (!IsInstanceValid(kart) || !kart.ActivePassenger.HasValue)
            {
                _settlingDropoffs.Remove(peerId);
                _dropoffSettleSeconds.Remove(peerId);
                continue;
            }

            if (kart.LinearVelocity.Length() > DropoffSettleSpeed)
            {
                _dropoffSettleSeconds[peerId] = 0.0f;
                continue;
            }

            float settled = _dropoffSettleSeconds.GetValueOrDefault(peerId) + dt;
            _dropoffSettleSeconds[peerId] = settled;
            if (settled >= DropoffSettleDuration)
            {
                GameManager.Instance.AwardFarePayout(peerId);
                ClearActiveFare(peerId);
            }
        }
    }

    public float GetDropoffSettleProgress(int peerId) => Mathf.Clamp(_dropoffSettleSeconds.GetValueOrDefault(peerId) / DropoffSettleDuration, 0.0f, 1.0f);

    public void ClearActiveFare(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        _playerDestinations.Remove(peerId);
        _settlingDropoffs.Remove(peerId);
        _dropoffSettleSeconds.Remove(peerId);

        if (_playerDropoffAreas.TryGetValue(peerId, out Area3D area) && IsInstanceValid(area))
        {
            area.QueueFree();
        }
        _playerDropoffAreas.Remove(peerId);

        NotifyDestination(peerId, Vector3.Zero);

        var kart = GameManager.Instance?.GetKart(peerId);
        if (IsInstanceValid(kart))
        {
            kart.ClearPassenger();
            kart.SetBoardingProgress(0.0f);
        }
    }

    public void AddCashScore(int peerId, int cash)
    {
        if (!Multiplayer.IsServer() || _phase != MatchPhase.Active || cash <= 0)
            return;

        if (!_scores.ContainsKey(peerId))
            _scores[peerId] = 0;

        _scores[peerId] += cash;

        if (_endlessRunActive)
        {
            if (peerId == 1)
            {
                _totalRunCash += cash;
                if (_scores[peerId] >= _currentCashQuota)
                    CompleteEndlessShift();
            }
        }
        else if (_scores[peerId] >= WinningCashTarget)
        {
            EndMatch(peerId);
        }

        BroadcastFullState();
    }

    private void CompleteEndlessShift()
    {
        _matchActive = false;
        _phase = MatchPhase.Intermission;
        _winnerPeerId = 1;
        _lastShiftCash = GetScore(1);
        ClearDropoffAreas();
        ClearPickupZones();
        GameManager.Instance?.SetAllKartControlsEnabled(false);
        BroadcastMatchState();
    }

    private void EndEndlessRun()
    {
        _lastShiftCash = GetScore(1);
        EndMatch(FindLeader());
    }

    private void EndMatch(int winnerPeerId)
    {
        _matchActive = false;
        _phase = MatchPhase.Finished;
        _winnerPeerId = winnerPeerId;
        _timeRemaining = Math.Max(0.0, _timeRemaining);
        ClearDropoffAreas();
        ClearPickupZones();
        GameManager.Instance?.SetAllKartControlsEnabled(false);
        BroadcastMatchState();
    }

    private int FindLeader()
    {
        if (_scores.Count == 0)
            return 0;

        return _scores.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).First().Key;
    }

    private IEnumerable<int> GetConnectedPeerIds()
    {
        yield return Multiplayer.GetUniqueId();
        foreach (int peerId in Multiplayer.GetPeers())
            yield return peerId;
    }

    private IEnumerable<int> GetMatchPlayerIds()
    {
        int[] registered = GameManager.Instance?.GetRegisteredPlayerIds();
        if (registered != null && registered.Length > 0)
        {
            foreach (int id in registered)
                yield return id;
            yield break;
        }

        foreach (int id in GetConnectedPeerIds())
            yield return id;
    }

    private void BroadcastFullState()
    {
        int[] peerIds = _scores.Keys.OrderBy(id => id).ToArray();
        int[] scores = peerIds.Select(id => _scores[id]).ToArray();
        
        foreach (int id in GetConnectedPeerIds())
        {
            if (id == Multiplayer.GetUniqueId())
                continue;
            Vector3 dest = _playerDestinations.TryGetValue(id, out Vector3 val) ? val : Vector3.Zero;
            RpcId(id, nameof(SyncFullStateRpc), _timeRemaining, _matchActive, _winnerPeerId, (int)_phase, _countdownRemaining, dest, peerIds, scores);
        }

        PublishLocalEvents();
    }

    private void BroadcastMatchState()
    {
        foreach (int id in Multiplayer.GetPeers())
            RpcId(id, nameof(SyncMatchStateRpc), _timeRemaining, _matchActive, _winnerPeerId, (int)_phase, _countdownRemaining);

        PublishLocalEvents();
    }

    private bool IsLocalPlayer(int peerId)
    {
        return peerId == Multiplayer.GetUniqueId();
    }

    private void NotifyDestination(int peerId, Vector3 destination)
    {
        if (IsLocalPlayer(peerId))
        {
            SetDestinationRpc(destination);
            return;
        }

        if (Multiplayer.GetPeers().Contains(peerId))
            RpcId(peerId, nameof(SetDestinationRpc), destination);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SetDestinationRpc(Vector3 destination)
    {
        ActiveDestination = destination;
        CheckpointChanged?.Invoke(0, destination);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncFullStateRpc(double timeRemaining, bool matchActive, int winnerPeerId, int phase, double countdownRemaining, Vector3 destination, int[] peerIds, int[] scores)
    {
        _timeRemaining = timeRemaining;
        _matchActive = matchActive;
        _winnerPeerId = winnerPeerId;
        _phase = (MatchPhase)phase;
        _countdownRemaining = countdownRemaining;
        _scores.Clear();

        int count = Math.Min(peerIds.Length, scores.Length);
        for (int i = 0; i < count; i++)
            _scores[peerIds[i]] = scores[i];

        ActiveDestination = destination;
        CheckpointChanged?.Invoke(0, destination);
        PublishLocalEvents();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SyncMatchStateRpc(double timeRemaining, bool matchActive, int winnerPeerId, int phase, double countdownRemaining)
    {
        _timeRemaining = timeRemaining;
        _matchActive = matchActive;
        _winnerPeerId = winnerPeerId;
        _phase = (MatchPhase)phase;
        _countdownRemaining = countdownRemaining;
        MatchStateChanged?.Invoke(_timeRemaining, _matchActive, _winnerPeerId);
    }

    private void PublishLocalEvents()
    {
        MatchStateChanged?.Invoke(_timeRemaining, _matchActive, _winnerPeerId);
        foreach (KeyValuePair<int, int> entry in _scores)
            ScoreboardChanged?.Invoke(entry.Key, entry.Value, GetRank(entry.Key));
    }

    public Vector3 GetPlayerDestination(int peerId)
    {
        return _playerDestinations.TryGetValue(peerId, out Vector3 dest) ? dest : Vector3.Zero;
    }

    public Vector3 GetNearestPickupPosition(Vector3 from)
    {
        Vector3 nearest = Vector3.Zero;
        float nearestDistance = float.MaxValue;
        foreach (PickupZone zone in _activeZones)
        {
            if (!IsInstanceValid(zone) || zone.IsQueuedForDeletion())
                continue;

            float distance = from.DistanceSquaredTo(zone.GlobalPosition);
            if (distance < nearestDistance)
            {
                nearest = zone.GlobalPosition;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    public int GetActivePickupCount()
    {
        return _activeZones.Count(zone => IsInstanceValid(zone) && !zone.IsQueuedForDeletion());
    }

    public int GetScore(int peerId)
    {
        return _scores.TryGetValue(peerId, out int score) ? score : 0;
    }

    public int GetRank(int peerId)
    {
        int rank = 1;
        int score = GetScore(peerId);
        foreach (KeyValuePair<int, int> entry in _scores)
        {
            if (entry.Value > score)
                rank++;
        }
        return rank;
    }
}
