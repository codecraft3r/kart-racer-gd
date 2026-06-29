using Godot;

public partial class MultiplayerLobby : Control
{
    [Export] public NodePath StatusLabelPath;
    private Label _status;

    public override void _Ready()
    {
        _status = GetNode<Label>(StatusLabelPath);

        GetNode<Button>("VBox/HBox/HostButton").Pressed += OnHostPressed;
        GetNode<Button>("VBox/HBox/JoinButton").Pressed += OnJoinPressed;
        GetNode<Button>("VBox/DisconnectButton").Pressed += OnDisconnectPressed;
    }

    private void OnHostPressed()
    {
        Error err = MultiplayerManager.Instance.Host();
        _status.Text = err == Error.Ok ? "Hosting..." : $"Error: {err}";
    }

    private void OnJoinPressed()
    {
        Error err = MultiplayerManager.Instance.Join();
        _status.Text = err == Error.Ok ? "Connecting..." : $"Error: {err}";
    }

    private void OnDisconnectPressed()
    {
        MultiplayerManager.Instance.Disconnect();
        _status.Text = "Disconnected";
    }
}
