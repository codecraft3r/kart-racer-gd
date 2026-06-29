using Godot;
using System.Collections.Generic;

public partial class GameManager : Node
{
    public static GameManager Instance { get; private set; }

    private const string KartScenePath = "res://kart.tscn";

    private readonly Dictionary<long, Node> _playerKarts = new();

    public override void _Ready()
    {
        Instance = this;

        if (Multiplayer.IsServer())
        {
            Multiplayer.PeerConnected += SpawnPlayer;
            Multiplayer.PeerDisconnected += RemovePlayer;
        }
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

        GD.Print($"Spawned kart for peer {id}");
    }

    private void RemovePlayer(long id)
    {
        if (_playerKarts.TryGetValue(id, out Node kart))
        {
            kart.QueueFree();
            _playerKarts.Remove(id);
            GD.Print($"Removed kart for peer {id}");
        }
    }
}
