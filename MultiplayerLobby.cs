using Godot;
using System;

public partial class MultiplayerLobby : Control
{
    [Export] public NodePath StatusLabelPath;
    [Export] public NodePath AddressFieldPath;
    private Label _status;
    private LineEdit _addressField;
    private string _pendingJoinAddress = "127.0.0.1";

    public override void _Ready()
    {
        _status = GetNode<Label>(StatusLabelPath);
        _addressField = GetNodeOrNull<LineEdit>(AddressFieldPath);

        GetNode<Button>("VBox/HBox/HostButton").Pressed += OnHostPressed;
        GetNode<Button>("VBox/HBox/JoinButton").Pressed += OnJoinPressed;
        GetNode<Button>("VBox/DisconnectButton").Pressed += OnDisconnectPressed;

        ApplyCommandLineDemoMode();
    }

    private void OnHostPressed()
    {
        if (MultiplayerManager.Instance == null)
        {
            _status.Text = "Multiplayer manager missing";
            return;
        }

        Error err = MultiplayerManager.Instance.Host();
        _status.Text = err == Error.Ok ? "Hosting..." : $"Error: {err}";
    }

    private void OnJoinPressed()
    {
        if (MultiplayerManager.Instance == null)
        {
            _status.Text = "Multiplayer manager missing";
            return;
        }

        string address = _addressField?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(address))
            address = "127.0.0.1";

        Error err = MultiplayerManager.Instance.Join(address);
        _status.Text = err == Error.Ok ? "Connecting..." : $"Error: {err}";
    }

    private void OnDisconnectPressed()
    {
        if (MultiplayerManager.Instance == null)
        {
            _status.Text = "Multiplayer manager missing";
            return;
        }

        MultiplayerManager.Instance.Disconnect();
        _status.Text = "Disconnected";
    }

    private void ApplyCommandLineDemoMode()
    {
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--host", StringComparison.OrdinalIgnoreCase) || arg.Equals("--demo-host", StringComparison.OrdinalIgnoreCase))
            {
                CallDeferred(nameof(RunCommandLineHost));
                return;
            }

            if (arg.StartsWith("--join=", StringComparison.OrdinalIgnoreCase))
            {
                _pendingJoinAddress = arg["--join=".Length..].Trim();
                CallDeferred(nameof(RunCommandLineJoin));
                return;
            }

            if (arg.Equals("--join", StringComparison.OrdinalIgnoreCase) || arg.Equals("--demo-join", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                    _pendingJoinAddress = args[i + 1].Trim();
                CallDeferred(nameof(RunCommandLineJoin));
                return;
            }
        }
    }

    public void RunCommandLineHost()
    {
        GD.Print("Command-line demo mode: hosting multiplayer match.");
        OnHostPressed();
    }

    public void RunCommandLineJoin()
    {
        if (string.IsNullOrWhiteSpace(_pendingJoinAddress))
            _pendingJoinAddress = "127.0.0.1";

        if (_addressField != null)
            _addressField.Text = _pendingJoinAddress;

        GD.Print($"Command-line demo mode: joining multiplayer match at {_pendingJoinAddress}.");
        OnJoinPressed();
    }
}
