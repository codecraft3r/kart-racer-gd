using Godot;

public partial class MultiplayerManager : Node
{
    public static MultiplayerManager Instance { get; private set; }

    public enum ConnectionState
    {
        Offline,
        Hosting,
        Connecting,
        InMatch,
        Disconnected,
        Failed
    }

    public event System.Action<ConnectionState, string> ConnectionStateChanged;

    [Export] public string MatchScenePath = "res://default_3d.tscn";

    private const int Port = 7000;
    private const int MaxClients = 8;
    private const string DefaultIp = "127.0.0.1";
    private bool _signalsConnected;
    private ConnectionState _state = ConnectionState.Offline;
    private string _pendingJoinAddress = DefaultIp;
    private int _syncedSeed = 1337;

    public int SyncedSeed => _syncedSeed;
    public ConnectionState State => _state;

    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            GD.PushWarning("Duplicate MultiplayerManager detected; removing the later instance.");
            QueueFree();
            return;
        }

        Instance = this;
        ConnectSignals();
        ApplyCommandLineNetworkMode();
    }

    public override void _ExitTree()
    {
        DisconnectSignals();
        if (Instance == this)
            Instance = null;
    }

    private void ConnectSignals()
    {
        if (_signalsConnected)
            return;

        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        _signalsConnected = true;
    }

    private void DisconnectSignals()
    {
        if (!_signalsConnected)
            return;

        Multiplayer.PeerConnected -= OnPeerConnected;
        Multiplayer.PeerDisconnected -= OnPeerDisconnected;
        Multiplayer.ConnectedToServer -= OnConnectedToServer;
        Multiplayer.ConnectionFailed -= OnConnectionFailed;
        Multiplayer.ServerDisconnected -= OnServerDisconnected;
        _signalsConnected = false;
    }

    public Error Host()
    {
        _syncedSeed = new System.Random().Next(1, 1000000);
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateServer(Port, MaxClients);
        if (err != Error.Ok) return err;

        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"Hosting on port {Port} with seed {_syncedSeed}");
        SetState(ConnectionState.Hosting, $"Hosting on port {Port}");
        CallDeferred(nameof(LoadMatchScene));
        return Error.Ok;
    }

    public Error Join(string address = DefaultIp)
    {
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateClient(address, Port);
        if (err != Error.Ok) return err;

        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"Joining {address}:{Port}");
        SetState(ConnectionState.Connecting, $"Connecting to {address}:{Port}");
        return Error.Ok;
    }

    public void Disconnect()
    {
        GameManager.Instance?.ResetNetworkSession();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        GD.Print("Disconnected");
        SetState(ConnectionState.Disconnected, "Disconnected");
    }

    private void OnPeerConnected(long id)
    {
        GD.Print($"Peer connected: {id}");
        if (Multiplayer.IsServer())
        {
            RpcId(id, nameof(SyncSeedRpc), _syncedSeed);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncSeedRpc(int seed)
    {
        GD.Print($"Received seed from server: {seed}");
        _syncedSeed = seed;
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"Peer disconnected: {id}");
    }

    private void OnConnectedToServer()
    {
        GD.Print("Connected to server");
        SetState(ConnectionState.InMatch, "Connected");
        LoadMatchScene();
    }

    private void OnConnectionFailed()
    {
        GD.Print("Connection failed");
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        SetState(ConnectionState.Failed, "Connection failed");
    }

    private void OnServerDisconnected()
    {
        GD.Print("Server disconnected");
        GameManager.Instance?.ResetNetworkSession();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        SetState(ConnectionState.Disconnected, "Server disconnected");
    }

    public void LoadMatchScene()
    {
        if (GetTree() == null || string.IsNullOrWhiteSpace(MatchScenePath))
            return;

        if (GetTree().CurrentScene?.SceneFilePath == MatchScenePath)
        {
            if (Multiplayer.IsServer())
                GameManager.Instance?.StartNetworkSession();
            else
                GameManager.Instance?.NotifyServerReadyForMatch();
            SetState(ConnectionState.InMatch, Multiplayer.IsServer() ? "Hosting match" : "Connected");
            return;
        }

        Error err = GetTree().ChangeSceneToFile(MatchScenePath);
        if (err != Error.Ok)
            GD.PushError($"Unable to load match scene {MatchScenePath}: {err}");
        else
            SetState(ConnectionState.InMatch, Multiplayer.IsServer() ? "Hosting match" : "Connected");
    }

    private void SetState(ConnectionState state, string message)
    {
        _state = state;
        ConnectionStateChanged?.Invoke(state, message);
    }

    private void ApplyCommandLineNetworkMode()
    {
        string[] args = MergeCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--server", System.StringComparison.OrdinalIgnoreCase))
            {
                CallDeferred(nameof(RunDedicatedServer));
                return;
            }

            if (arg.Equals("--host", System.StringComparison.OrdinalIgnoreCase) || arg.Equals("--demo-host", System.StringComparison.OrdinalIgnoreCase))
            {
                CallDeferred(nameof(RunCommandLineHost));
                return;
            }

            if (arg.StartsWith("--join=", System.StringComparison.OrdinalIgnoreCase))
            {
                _pendingJoinAddress = arg["--join=".Length..].Trim();
                CallDeferred(nameof(RunCommandLineJoin));
                return;
            }

            if (arg.Equals("--join", System.StringComparison.OrdinalIgnoreCase) || arg.Equals("--demo-join", System.StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    _pendingJoinAddress = args[i + 1].Trim();
                CallDeferred(nameof(RunCommandLineJoin));
                return;
            }
        }
    }

    private static string[] MergeCommandLineArgs()
    {
        var merged = new System.Collections.Generic.List<string>();
        foreach (string arg in OS.GetCmdlineArgs())
            merged.Add(arg);
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (!merged.Contains(arg))
                merged.Add(arg);
        }
        return merged.ToArray();
    }

    public void RunDedicatedServer()
    {
        GD.Print("Dedicated server mode requested.");
        Error err = Host();
        if (err != Error.Ok)
        {
            GD.PushError($"Dedicated server failed to start: {err}");
            GetTree().Quit(1);
        }
    }

    public void RunCommandLineHost()
    {
        GD.Print("Command-line mode: hosting multiplayer match.");
        Error err = Host();
        if (err != Error.Ok)
            GD.PushError($"Command-line host failed: {err}");
    }

    public void RunCommandLineJoin()
    {
        if (string.IsNullOrWhiteSpace(_pendingJoinAddress))
            _pendingJoinAddress = DefaultIp;

        GD.Print($"Command-line mode: joining multiplayer match at {_pendingJoinAddress}.");
        Error err = Join(_pendingJoinAddress);
        if (err != Error.Ok)
            GD.PushError($"Command-line join failed: {err}");
    }
}
