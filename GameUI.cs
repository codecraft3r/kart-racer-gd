using Godot;
using System;

public partial class GameUI : CanvasLayer
{
    [Export] public NodePath TargetKartPath;

    private Kart _targetKart;
    private Label _speedValueLabel;
    private Label _timeValueLabel;
    private Control _pauseOverlay;
    private Button _resumeButton;
    private Button _restartButton;
    private Button _quitButton;

    private double _elapsedTime = 0.0;
    private bool _isPaused = false;

    public override void _Ready()
    {
        // 1. Get references to HUD and Menu elements
        _speedValueLabel = GetNode<Label>("UIRoot/HUDContainer/Speedometer/MarginContainer/VBoxContainer/SpeedValue");
        _timeValueLabel = GetNode<Label>("UIRoot/HUDContainer/Timer/MarginContainer/TimeValue");
        _pauseOverlay = GetNode<Control>("UIRoot/PauseOverlay");

        _resumeButton = GetNode<Button>("UIRoot/PauseOverlay/CenterContainer/PauseMenu/MarginContainer/VBoxContainer/ResumeButton");
        _restartButton = GetNode<Button>("UIRoot/PauseOverlay/CenterContainer/PauseMenu/MarginContainer/VBoxContainer/RestartButton");
        _quitButton = GetNode<Button>("UIRoot/PauseOverlay/CenterContainer/PauseMenu/MarginContainer/VBoxContainer/QuitButton");

        // 2. Connect button signals
        _resumeButton.Pressed += OnResumePressed;
        _restartButton.Pressed += OnRestartPressed;
        _quitButton.Pressed += OnQuitPressed;

        // 3. Resolve the target Kart reference
        if (TargetKartPath != null && !TargetKartPath.IsEmpty)
        {
            _targetKart = GetNodeOrNull<Kart>(TargetKartPath);
        }

        // Default: hide the pause menu overlay
        _pauseOverlay.Visible = false;
        
        // Ensure this UI node can process while the game is paused
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        // 1. Update speedometer from Kart telemetry
        if (_targetKart != null)
        {
            // Round to nearest integer for display
            int speedKmh = Mathf.RoundToInt(_targetKart.CurrentSpeedKmh);
            _speedValueLabel.Text = speedKmh.ToString();
        }
        else
        {
            _speedValueLabel.Text = "0";
        }

        // 2. Update gameplay timer
        if (!GetTree().Paused)
        {
            _elapsedTime += delta;
            FormatAndDisplayTime();
        }

        // 3. Check for pause/unpause input
        if (Input.IsActionJustPressed("ui_cancel"))
        {
            TogglePause();
        }
    }

    private void FormatAndDisplayTime()
    {
        int minutes = (int)(_elapsedTime / 60);
        int seconds = (int)(_elapsedTime % 60);
        int milliseconds = (int)((_elapsedTime - (int)_elapsedTime) * 100);

        _timeValueLabel.Text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
    }

    public void TogglePause()
    {
        _isPaused = !_isPaused;
        GetTree().Paused = _isPaused;
        _pauseOverlay.Visible = _isPaused;

        if (_isPaused)
        {
            // Focus the resume button for controller navigation support
            _resumeButton.GrabFocus();
        }
    }

    private void OnResumePressed()
    {
        if (GetTree().Paused)
        {
            TogglePause();
        }
    }

    private void OnRestartPressed()
    {
        // Unpause before restarting the scene
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}
