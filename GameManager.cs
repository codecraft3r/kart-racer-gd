using Godot;
using System.Collections.Generic;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    private const string KartScenePath = "res://kart.tscn";
    private const string DepotPosition = "res://Depot"; // placeholder marker later

    private readonly Dictionary<long, Node> _playerKarts = new();
    private readonly Dictionary<long, PlayerState> _playerStates = new();

    public override void _Ready()
    {
        Instance = this;

        if (Multiplayer.IsServer())
        {
            Multiplayer.PeerConnected += SpawnPlayer;
            Multiplayer.PeerDisconnected += RemovePlayer;
        }
    }

    // Simple server-authoritative match timer (Phase 1 extension)
    private double _matchTime = 300; // 5 minutes default
    public bool MatchActive { get; private set; } = false;

    public void StartMatch(double durationSeconds = 300)
    {
        if (!Multiplayer.IsServer()) return;
        _matchTime = durationSeconds;
        MatchActive = true;
        GD.Print($"Match started: {durationSeconds}s");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer() || !MatchActive) return;
        _matchTime -= delta;
        if (_matchTime <= 0)
        {
            EndMatch();
        }
    }

    private void EndMatch()
    {
        MatchActive = false;
        GD.Print("Match ended");
        // TODO: Broadcast final scores
    }

    private void SpawnPlayer(long id)
    {
        var kartScene = GD.Load<PackedScene>(KartScenePath);
        var kart = kartScene.Instantiate<RigidBody3D>();

        // Random spawn position on the big platform
        var rng = new RandomNumberGenerator();
        float x = rng.RandfRange(-15, 15);
        float z = rng.RandfRange(-15, 15);
        kart.Position = new Vector3(x, 2, z);

        // Give each player authority over their own kart (for input)
        kart.SetMultiplayerAuthority((int)id);

        // Mark local player
        if (id == Multiplayer.GetUniqueId())
        {
            kart.SetScript(ResourceLoader.Load<Script>("res://Kart.cs"));
            // The IsLocalPlayer flag is only used client-side for input sending
            ((Kart)kart).IsLocalPlayer = true;
        }

        AddChild(kart);
        _playerKarts[id] = kart;
        _playerStates[id] = new PlayerState();

        GD.Print($"Spawned kart for peer {id}");
    }

    private void RemovePlayer(long id)
    {
        if (_playerKarts.TryGetValue(id, out Node kart))
        {
            kart.QueueFree();
            _playerKarts.Remove(id);
            _playerStates.Remove(id);
            GD.Print($"Removed kart for peer {id}");
        }
    }

    // --- Phase 1 additions: PlayerState + respawn at depot ---

    public void RespawnAtDepot(long id)
    {
        if (!_playerKarts.TryGetValue(id, out Node kartNode) || kartNode is not RigidBody3D kart) return;

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
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
    public void SyncPlayerState(long id, int score, int money, int health)
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
    private readonly Dictionary<long, Dictionary<WeaponClass, Weapon>> _playerLoadouts = new();

    // Distance bias helper (more weapons farther from depot)
    public bool ShouldSpawnWeapon(Vector3 pos, float depotDistThreshold = 30f)
    {
        return pos.Length() > depotDistThreshold;
    }

    // --- Phase 4 scaffolding: Economy stubs ---

    public void AwardPayout(long id, int amount)
    {
        if (_playerStates.TryGetValue(id, out var state))
        {
            state.Money += amount;
            state.Score += amount; // simplistic
        }
    }

    public bool TryPurchaseRepair(long id, int cost)
    {
        if (_playerStates.TryGetValue(id, out var state) && state.Money >= cost)
        {
            state.Money -= cost;
            state.Health = 100;
            return true;
        }
        return false;
    }

    public bool TryPurchaseAmmo(long id, Weapon w, int cost)
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
