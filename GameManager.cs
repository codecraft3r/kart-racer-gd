using Godot;
using System.Collections.Generic;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    public event System.Action<int, Vector3> LocalPlayerSpawned;

    private const string KartScenePath = "res://kart.tscn";
    private const string DepotPosition = "res://Depot"; // placeholder marker later
    private const double SnapshotInterval = 1.0 / 20.0;

    private readonly Dictionary<int, Kart> _playerKarts = new();
    private readonly Dictionary<int, PlayerState> _playerStates = new();
    private Kart _pendingLocalKart;
    private double _snapshotAccumulator;
    private bool _multiplayerSignalsConnected;

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

    // Simple server-authoritative match timer (Phase 1 extension)
    private double _matchTime = 300; // 5 minutes default
    public bool MatchActive { get; private set; } = false;

    public void StartNetworkSession()
    {
        if (!Multiplayer.IsServer())
            return;

        ConfigureSinglePlayerKartForNetwork(true);
        SpawnPlayerForPeer(Multiplayer.GetUniqueId());

        foreach (int peerId in Multiplayer.GetPeers())
            SendExistingPlayersTo(peerId);

        CheckpointRushMode.Instance?.StartMatch();
    }

    public void ResetNetworkSession()
    {
        foreach (Kart kart in _playerKarts.Values)
            kart.QueueFree();

        _playerKarts.Clear();
        _playerStates.Clear();
        _pendingLocalKart = null;
        _snapshotAccumulator = 0.0;
        ConfigureSinglePlayerKartForNetwork(false);
        MatchActive = false;
    }

    public void StartMatch(double durationSeconds = 300)
    {
        if (!Multiplayer.IsServer()) return;
        _matchTime = durationSeconds;
        MatchActive = true;
        GD.Print($"Match started: {durationSeconds}s");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer())
            return;

        if (MatchActive)
        {
            _matchTime -= delta;
            if (_matchTime <= 0)
                EndMatch();
        }

        if (HasActiveNetworkPeer())
            SendKartSnapshots(delta);
    }

    private void EndMatch()
    {
        MatchActive = false;
        GD.Print("Match ended");
        // TODO: Broadcast final scores
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
        CheckpointRushMode.Instance?.RemovePlayer((int)id);
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
        CheckpointRushMode.Instance?.RegisterPlayer(senderId);
        CheckpointRushMode.Instance?.SyncToPeer(senderId);
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

        // TODO: Replace with actual depot marker position from scene
        kart.Position = new Vector3(0, 5, 0);
        kart.LinearVelocity = Vector3.Zero;

        // Reset health/score logic here later
        GD.Print($"Respawned peer {id} at depot");
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
        if (_playerStates.TryGetValue(id, out var state))
        {
            state.Score = score;
            state.Money = money;
            state.Health = health;
        }
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

    // Stub: Server will manage active pickup zones
    private readonly List<Node> _pickupZones = new();

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
            state.Score += amount; // simplistic
        }
    }

    public bool TryPurchaseRepair(int id, int cost)
    {
        if (_playerStates.TryGetValue(id, out var state) && state.Money >= cost)
        {
            state.Money -= cost;
            state.Health = 100;
            return true;
        }
        return false;
    }

    public bool TryPurchaseAmmo(int id, Weapon w, int cost)
    {
        if (_playerStates.TryGetValue(id, out var state) && state.Money >= cost && w != null)
        {
            state.Money -= cost;
            w.Ammo = 10; // reset
            return true;
        }
        return false;
    }
}
