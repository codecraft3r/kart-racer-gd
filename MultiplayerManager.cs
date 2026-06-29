using Godot;

public partial class MultiplayerManager : Node
{
    public static MultiplayerManager Instance { get; private set; }

    private const int Port = 7000;
    private const int MaxClients = 8;
    private const string DefaultIp = "127.0.0.1";

    public override void _Ready()
    {
        Instance = this;
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailed;
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public Error Host()
    {
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateServer(Port, MaxClients);
        if (err != Error.Ok) return err;

        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"Hosting on port {Port}");
        return Error.Ok;
    }

    public Error Join(string address = DefaultIp)
    {
        var peer = new ENetMultiplayerPeer();
        Error err = peer.CreateClient(address, Port);
        if (err != Error.Ok) return err;

        Multiplayer.MultiplayerPeer = peer;
        GD.Print($"Joining {address}:{Port}");
        return Error.Ok;
    }

    public void Disconnect()
    {
        Multiplayer.MultiplayerPeer = null;
        GD.Print("Disconnected");
    }

    private void OnPeerConnected(long id)
    {
        GD.Print($"Peer connected: {id}");
    }

    private void OnPeerDisconnected(long id)
    {
        GD.Print($"Peer disconnected: {id}");
    }

    private void OnConnectedToServer()
    {
        GD.Print("Connected to server");
    }

    private void OnConnectionFailed()
    {
        GD.Print("Connection failed");
        Multiplayer.MultiplayerPeer = null;
    }

    private void OnServerDisconnected()
    {
        GD.Print("Server disconnected");
        Multiplayer.MultiplayerPeer = null;
    }
}
