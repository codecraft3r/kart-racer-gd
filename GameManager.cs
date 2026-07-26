using Godot;
using System.Collections.Generic;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    [Export] public int SoloAiCount { get; set; } = 2;

    public event System.Action<int, Vector3> LocalPlayerSpawned;

    private const string KartScenePath = "res://kart.tscn";
    private const double SnapshotInterval = 1.0 / 20.0;

    private readonly Dictionary<int, Kart> _playerKarts = new();
    private readonly Dictionary<int, PlayerState> _playerStates = new();
    private Kart _pendingLocalKart;
    private double _snapshotAccumulator;
    private bool _multiplayerSignalsConnected;
    private readonly List<Kart> _aiKarts = new();

    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            GD.PushWarning("Duplicate GameManager detected; removing the later instance.");
            QueueFree();
            return;
        }

        Instance = this;
        ConnectMultiplayerSignals();

        if (HasActiveNetworkPeer())
        {
            ConfigureSinglePlayerKartForNetwork(true);

            if (Multiplayer.IsServer())
                CallDeferred(nameof(StartNetworkSession));
            else
                CallDeferred(nameof(NotifyServerReadyForMatch));
        }
    }

    public override void _ExitTree()
    {
        DisconnectMultiplayerSignals();
        if (Instance == this)
            Instance = null;
    }

    public void StartNetworkSession()
    {
        if (!Multiplayer.IsServer())
            return;

        ConfigureSinglePlayerKartForNetwork(true);
        SpawnPlayerForPeer(Multiplayer.GetUniqueId());

        foreach (int peerId in Multiplayer.GetPeers())
            SendExistingPlayersTo(peerId);

        TaxiMode.Instance?.StartMatch();
    }

    public void ContinueSoloSession()
    {
        if (TaxiMode.Instance?.Phase != TaxiMode.MatchPhase.Intermission)
            return;

        int nextShiftNumber = TaxiMode.Instance.ShiftNumber + 1;
        int targetRivalCount = Mathf.Clamp(SoloAiCount + ((nextShiftNumber - 1) / 2), 0, 6);
        while (_aiKarts.Count < targetRivalCount)
            SpawnSoloRival(_aiKarts.Count);

        int slot = 0;
        foreach (int id in GetRegisteredPlayerIds())
        {
            Kart kart = GetKart(id);
            if (GodotObject.IsInstanceValid(kart))
            {
                kart.ClearPassenger();
                kart.SetBoardingProgress(0.0f);
                kart.ResetForRun(GetSpawnTransform(slot));
            }
            slot++;
        }

        TaxiMode.Instance.ContinueEndlessRun();
    }

    public void ResetNetworkSession()
    {
        TaxiMode.Instance?.ResetMatch();
        Kart sceneKart = GetParent()?.GetNodeOrNull<Kart>("Kart");

        foreach (Kart kart in _playerKarts.Values)
        {
            if (GodotObject.IsInstanceValid(kart) && kart != sceneKart)
                kart.QueueFree();
        }

        foreach (Kart aiKart in _aiKarts)
        {
            if (GodotObject.IsInstanceValid(aiKart))
                aiKart.QueueFree();
        }

        _playerKarts.Clear();
        _playerStates.Clear();
        _aiKarts.Clear();
        _pendingLocalKart = null;
        _snapshotAccumulator = 0.0;
        ConfigureSinglePlayerKartForNetwork(false);
    }

    public void StartSoloSession()
    {
        ResetSoloSession();

        var localKart = GetParent()?.GetNodeOrNull<Kart>("Kart");
        if (localKart != null)
        {
            _playerKarts[1] = localKart;
            _playerStates[1] = new PlayerState();
            localKart.OwnerPeerId = 1;
            localKart.IsAI = false;
            localKart.IsLocalPlayer = true;
            localKart.UseLocalInput = true;
            localKart.Visible = true;
            localKart.Freeze = false;
            localKart.ProcessMode = ProcessModeEnum.Inherit;
            localKart.EnsureLocalPlayerFeatures();
            localKart.ConfigureIdentityGlow(new Color(0.0f, 0.86f, 1.0f));
            localKart.ResetForRun(GetSpawnTransform(0));
        }

        int aiCount = Mathf.Clamp(SoloAiCount, 0, 6);
        for (int i = 0; i < aiCount; i++)
            SpawnSoloRival(i);

        TaxiMode.Instance?.StartEndlessRun();
    }

    private void SpawnSoloRival(int rivalIndex)
    {
        int aiId = 100 + rivalIndex;
        if (_playerKarts.ContainsKey(aiId))
            return;

        var kartScene = GD.Load<PackedScene>(KartScenePath);
        var aiKart = kartScene.Instantiate<Kart>();
        aiKart.Name = aiId.ToString();
        aiKart.OwnerPeerId = aiId;
        aiKart.IsAI = true;
        aiKart.UseLocalInput = false;
        aiKart.IsLocalPlayer = false;
        aiKart.AddChild(new KartAIController());

        GetParent()?.AddChild(aiKart);
        aiKart.ConfigureIdentityGlow(rivalIndex % 2 == 0 ? new Color(1.0f, 0.02f, 0.48f) : new Color(1.0f, 0.68f, 0.08f));
        aiKart.ResetForRun(GetSpawnTransform(rivalIndex + 1));
        _playerKarts[aiId] = aiKart;
        _playerStates[aiId] = new PlayerState();
        _aiKarts.Add(aiKart);
    }

    public void ResetSoloSession()
    {
        TaxiMode.Instance?.ResetMatch();

        foreach (var aiKart in _aiKarts)
        {
            if (GodotObject.IsInstanceValid(aiKart))
            {
                aiKart.GetParent()?.RemoveChild(aiKart);
                aiKart.QueueFree();
            }
        }
        _aiKarts.Clear();

        _playerKarts.Clear();
        _playerStates.Clear();

        var localKart = GetParent()?.GetNodeOrNull<Kart>("Kart");
        if (localKart != null)
        {
            _playerKarts[1] = localKart;
            _playerStates[1] = new PlayerState();
            localKart.ClearPassenger();
            localKart.SetBoardingProgress(0.0f);
            localKart.SetControlsEnabled(true);
        }
    }

    public Kart GetKart(int id)
    {
        return _playerKarts.TryGetValue(id, out Kart kart) && GodotObject.IsInstanceValid(kart) ? kart : null;
    }

    public int[] GetRegisteredPlayerIds()
    {
        var ids = new int[_playerKarts.Count];
        _playerKarts.Keys.CopyTo(ids, 0);
        System.Array.Sort(ids);
        return ids;
    }

    public int GetRegisteredPlayerCount() => _playerKarts.Count;

    public void SetAllKartControlsEnabled(bool enabled)
    {
        foreach (Kart kart in _playerKarts.Values)
        {
            if (GodotObject.IsInstanceValid(kart))
                kart.SetControlsEnabled(enabled);
        }
    }

    private Transform3D GetSpawnTransform(int slot)
    {
        if (TrackBuilder.Instance != null)
            return TrackBuilder.Instance.GetSpawnTransform(slot);

        return new Transform3D(Basis.Identity, new Vector3((slot - 1) * 3.4f, 0.65f, -12.0f));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer())
            return;

        if (HasActiveNetworkPeer())
            SendKartSnapshots(delta);
    }

    private void ConnectMultiplayerSignals()
    {
        if (_multiplayerSignalsConnected)
            return;

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        _multiplayerSignalsConnected = true;
    }

    private void DisconnectMultiplayerSignals()
    {
        if (!_multiplayerSignalsConnected)
            return;

        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        _multiplayerSignalsConnected = false;
    }

    private void OnPeerConnected(long id)
    {
        if (Multiplayer.IsServer())
            GD.Print($"Peer {id} connected; waiting for match-scene ready signal.");
    }

    private void OnPeerDisconnected(long id)
    {
        TaxiMode.Instance?.RemovePlayer((int)id);
        if (Multiplayer.IsServer())
            RemovePlayerForPeer((int)id);
    }

    public void NotifyServerReadyForMatch()
    {
        if (HasActiveNetworkPeer() && !Multiplayer.IsServer())
            RpcId(1, nameof(ClientReadyForMatchRpc));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ClientReadyForMatchRpc()
    {
        if (!Multiplayer.IsServer())
            return;

        int senderId = Multiplayer.GetRemoteSenderId();
        if (senderId <= 0)
            return;

        SendExistingPlayersTo(senderId);
        SpawnPlayerForPeer(senderId);
        TaxiMode.Instance?.RegisterPlayer(senderId);
        TaxiMode.Instance?.SyncToPeer(senderId);
    }

    private void SpawnPlayerForPeer(int id)
    {
        if (!Multiplayer.IsServer())
            return;

        if (_playerKarts.TryGetValue(id, out Kart existingKart))
        {
            RpcId(id, nameof(SpawnPlayerRpc), id, existingKart.GlobalPosition);
            return;
        }

        Rpc(nameof(SpawnPlayerRpc), id, PickSpawnPosition());
    }

    private void SendExistingPlayersTo(int targetPeerId)
    {
        foreach (KeyValuePair<int, Kart> entry in _playerKarts)
            RpcId(targetPeerId, nameof(SpawnPlayerRpc), entry.Key, entry.Value.GlobalPosition);
    }

    private Vector3 PickSpawnPosition()
    {
        var rng = new RandomNumberGenerator();
        rng.Randomize();
        float x = rng.RandfRange(-15, 15);
        float z = rng.RandfRange(-15, 15);
        return new Vector3(x, 2, z);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnPlayerRpc(int id, Vector3 position)
    {
        if (_playerKarts.ContainsKey(id))
            return;

        var kartScene = GD.Load<PackedScene>(KartScenePath);
        var kart = kartScene.Instantiate<Kart>();

        kart.Name = id.ToString();
        kart.OwnerPeerId = id;
        kart.Position = position;
        kart.SetMultiplayerAuthority(1);

        bool hasNetworkPeer = HasActiveNetworkPeer();
        bool isLocalPlayer = id == Multiplayer.GetUniqueId();
        kart.IsLocalPlayer = isLocalPlayer;
        kart.UseLocalInput = !hasNetworkPeer || (Multiplayer.IsServer() && isLocalPlayer);
        // The server owns every simulated rigid body. Clients keep replicas frozen,
        // including their local kart; local processing remains active to send input.
        kart.Freeze = hasNetworkPeer && !Multiplayer.IsServer();

        AddChild(kart, true);
        _playerKarts[id] = kart;
        if (!_playerStates.ContainsKey(id))
            _playerStates[id] = new PlayerState();

        if (hasNetworkPeer)
            ConfigureSinglePlayerKartForNetwork(true);

        if (isLocalPlayer)
        {
            QueueLocalViewRetarget(kart);
            LocalPlayerSpawned?.Invoke(id, position);
        }

        GD.Print($"Spawned kart for peer {id}");
    }

    private void RemovePlayerForPeer(int id)
    {
        if (Multiplayer.IsServer())
            Rpc(nameof(RemovePlayerRpc), id);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RemovePlayerRpc(int id)
    {
        if (_playerKarts.TryGetValue(id, out Kart kart))
        {
            kart.QueueFree();
            _playerKarts.Remove(id);
            _playerStates.Remove(id);
            GD.Print($"Removed kart for peer {id}");
        }
    }

    private void RetargetLocalView(Kart kart)
    {
        Node parent = GetParent();
        parent?.GetNodeOrNull<TrackCamera>("Camera3D")?.SetTarget(kart);

        RetroNeonCabShell shell = parent?.GetNodeOrNull<RetroNeonCabShell>("RetroNeonCabShell");
        if (shell != null)
        {
            shell.SetKart(kart);
            if (HasActiveNetworkPeer())
                shell.StartRun();
        }
    }

    private void QueueLocalViewRetarget(Kart kart)
    {
        _pendingLocalKart = kart;
        CallDeferred(nameof(ApplyPendingLocalViewTarget));
    }

    public void ApplyPendingLocalViewTarget()
    {
        if (_pendingLocalKart == null || !GodotObject.IsInstanceValid(_pendingLocalKart))
        {
            _pendingLocalKart = null;
            return;
        }

        RetargetLocalView(_pendingLocalKart);
        _pendingLocalKart = null;
    }

    private void SendKartSnapshots(double delta)
    {
        _snapshotAccumulator += delta;
        if (_snapshotAccumulator < SnapshotInterval)
            return;

        _snapshotAccumulator = 0.0;
        foreach (KeyValuePair<int, Kart> entry in _playerKarts)
        {
            Kart kart = entry.Value;
            if (kart == null || !GodotObject.IsInstanceValid(kart))
                continue;

            Rpc(
                nameof(ApplyKartSnapshotRpc),
                entry.Key,
                kart.GlobalPosition,
                kart.Rotation,
                kart.LinearVelocity
            );
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void ApplyKartSnapshotRpc(int id, Vector3 position, Vector3 rotation, Vector3 velocity)
    {
        if (Multiplayer.IsServer())
            return;

        if (_playerKarts.TryGetValue(id, out Kart kart) && kart != null && GodotObject.IsInstanceValid(kart))
            kart.ApplyNetworkSnapshot(position, rotation, velocity);
    }

    private bool HasActiveNetworkPeer()
    {
        return Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;
    }

    private void ConfigureSinglePlayerKartForNetwork(bool networked)
    {
        Kart sceneKart = GetParent()?.GetNodeOrNull<Kart>("Kart");
        if (sceneKart == null || sceneKart.GetParent() == this)
            return;

        sceneKart.UseLocalInput = !networked;
        sceneKart.IsLocalPlayer = !networked;
        sceneKart.Visible = !networked;
        sceneKart.Freeze = networked;
        sceneKart.ProcessMode = networked ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
        if (networked)
        {
            sceneKart.LinearVelocity = Vector3.Zero;
            sceneKart.AngularVelocity = Vector3.Zero;
        }
    }

    // --- Phase 1 additions: PlayerState + respawn at depot ---

    public void RespawnAtDepot(int id)
    {
        if (!_playerKarts.TryGetValue(id, out Kart kart)) return;

        int spawnSlot = id >= 100 ? id - 99 : 0;
        kart.ResetForRun(GetSpawnTransform(spawnSlot));

        if (_playerStates.TryGetValue(id, out var state))
        {
            state.Health = 100;
            state.Money = Mathf.Max(0, state.Money - 50);
            SyncPlayerState(id, state.Score, state.Money, state.Health);
        }

        TaxiMode.Instance?.ClearActiveFare(id);
        kart.BroadcastRespawnAudio();
        GD.Print($"Respawned peer {id} at depot with penalty.");
    }

    public int GetPlayerHealth(int id)
    {
        return _playerStates.TryGetValue(id, out var state) ? state.Health : 100;
    }

    public int GetPlayerScore(int id)
    {
        return _playerStates.TryGetValue(id, out var state) ? state.Score : 0;
    }

    public int GetPlayerMoney(int id)
    {
        return _playerStates.TryGetValue(id, out var state) ? state.Money : 0;
    }

    public void ApplyVehicleDamage(int id, int damage)
    {
        if (_playerStates.TryGetValue(id, out var state))
        {
            // Suppress damage while a repair is in progress: the kart is locked
            // in the bay and all HP changes (including this would-be reduction)
            // are deferred until the repair completes. Leaving the bay early
            // cancels the repair and the kart takes damage normally again.
            if (TrackBuilder.Instance != null && TrackBuilder.Instance.IsPeerBeingRepaired(id))
                return;

            state.Health = Mathf.Max(0, state.Health - damage);
            SyncPlayerState(id, state.Score, state.Money, state.Health);

            if (state.Health <= 0)
            {
                _playerKarts.GetValueOrDefault(id)?.BroadcastDestroyedAudio();
                RespawnAtDepot(id);
            }
        }
    }

    public void NotifyBailout(int id)
    {
        if (_playerStates.TryGetValue(id, out var state))
        {
            SyncPlayerState(id, state.Score, state.Money, state.Health);
        }
        _playerKarts.GetValueOrDefault(id)?.BroadcastPassengerBailoutAudio();
        GD.Print($"Passenger bailed out for peer {id}!");
    }

    public void AwardFarePayout(int id)
    {
        if (!_playerKarts.TryGetValue(id, out Kart kart) || !kart.ActivePassenger.HasValue) return;

        var passenger = kart.ActivePassenger.Value;

        int basePayout = 50;
        if (passenger.Distance == CustomerDistance.Moderate)
            basePayout = 150;
        else if (passenger.Distance == CustomerDistance.Far)
            basePayout = 300;

        int groupSize = passenger.GroupSize;
        int scoreYield = basePayout * groupSize;

        float panicFactor = kart.PanicMeter / 100.0f;
        int damagePenalty = Mathf.RoundToInt(scoreYield * 0.4f * panicFactor);
        int finalPayout = Mathf.Max(10, scoreYield - damagePenalty);

        if (_playerStates.TryGetValue(id, out var state))
        {
            state.Money += finalPayout;
            state.Score += finalPayout;
            SyncPlayerState(id, state.Score, state.Money, state.Health);

            TaxiMode.Instance?.AddCashScore(id, finalPayout);
        }

        kart.BroadcastFareCompletedAudio();
        GD.Print($"Peer {id} completed taxi run! Payout: {finalPayout} (Base: {scoreYield}, Penalty: {damagePenalty})");
    }

    public class PlayerState
    {
        public int Score;
        public int Money;
        public int Health = 100;
    }

    // Replicate key state to clients (simple RPC broadcast for now)
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
    public void SyncPlayerState(int id, int score, int money, int health)
    {
        if (!_playerStates.ContainsKey(id))
            _playerStates[id] = new PlayerState();

        var state = _playerStates[id];
        state.Score = score;
        state.Money = money;
        state.Health = health;
    }

    // --- Phase 2 scaffolding: Pickup zone + customer data structures ---

    public enum CustomerDistance { Near, Moderate, Far }
    public enum CustomerWealth { Low, Medium, High }

    public struct CustomerData
    {
        public CustomerDistance Distance;
        public CustomerWealth Wealth;
        public int MaxAcceptableDamage;
        public int GroupSize;
        public float LoadTime; // 5-10s
    }

    // --- Phase 3 scaffolding: Weapon classes + inventory stubs ---

    public enum WeaponClass { Rocket, Assault, Special }

    public class Weapon
    {
        public WeaponClass Class;
        public int Ammo;
        public bool IsDepleted => Ammo <= 0;
    }

    // Server-tracked per-player loadout (max 1 per class)
    private readonly Dictionary<int, Dictionary<WeaponClass, Weapon>> _playerLoadouts = new();

    // Distance bias helper (more weapons farther from depot)
    public bool ShouldSpawnWeapon(Vector3 pos, float depotDistThreshold = 30f)
    {
        return pos.Length() > depotDistThreshold;
    }

    // --- Phase 4 scaffolding: Economy stubs ---

    public void AwardPayout(int id, int amount)
    {
        if (_playerStates.TryGetValue(id, out var state))
        {
            state.Money += amount;
            state.Score += amount;
            SyncPlayerState(id, state.Score, state.Money, state.Health);
        }
    }

    public bool TryPurchaseRepair(int id, int cost)
    {
        if (_playerStates.TryGetValue(id, out var state) && state.Health < 100 && state.Money >= cost)
        {
            state.Money -= cost;
            state.Health = 100;
            SyncPlayerState(id, state.Score, state.Money, state.Health);
            return true;
        }
        return false;
    }

    public bool TryPurchaseAmmo(int id, Weapon w, int cost)
    {
        if (_playerStates.TryGetValue(id, out var state) && state.Money >= cost && w != null)
        {
            state.Money -= cost;
            w.Ammo = 10;
            SyncPlayerState(id, state.Score, state.Money, state.Health);
            return true;
        }
        return false;
    }
}
