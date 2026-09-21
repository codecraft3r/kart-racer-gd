using Godot;
using System;

/// <summary>
/// Race HUD + pause for the retro-neon taxi. Reads Kart and EndlessRoadMode
/// telemetry only; never writes gameplay state. Keeps the existing node paths
/// for SpeedValue/TimeValue/PauseOverlay stable so older scenes keep working.
/// </summary>
public partial class GameUI : CanvasLayer
{
    [Export] public NodePath TargetKartPath;

    private Kart _targetKart;
    private PanelContainer _speedPanel;
    private Label _speedValueLabel;
    private Label _stateLabel;
    private ProgressBar _driftBar;
    private Label _timeValueLabel;
    private PanelContainer _boostPanel;
    private ProgressBar _boostBar;
    private Control _pauseOverlay;
    private Button _resumeButton;
    private Button _restartButton;
    private Button _quitButton;

    private static readonly Color NeonCyan = new Color(0.13f, 0.59f, 0.95f);
    private static readonly Color NeonCyanBright = new Color(0.0f, 0.94f, 1.0f);
    private static readonly Color NeonPink = new Color(1.0f, 0.0f, 0.50f);
    private static readonly Color NeonAmber = new Color(0.96f, 0.77f, 0.32f);
    private static readonly Color SoftWhite = new Color(1.0f, 1.0f, 1.0f, 0.82f);

    private double _elapsedTime = 0.0;
    private bool _isPaused = false;
    private Tween _pulseTween;
    private bool _wasBoosting = false;
    private string _lastState = string.Empty;

    public override void _Ready()
    {
        // Existing paths stay fixed; new widgets are optional so a stale
        // scene keeps the speed + timer working.
        _speedPanel = GetNodeOrNull<PanelContainer>("UIRoot/HUDContainer/Speedometer");
        _speedValueLabel = GetNodeOrNull<Label>("UIRoot/HUDContainer/Speedometer/MarginContainer/VBoxContainer/SpeedValue");
        _stateLabel = GetNodeOrNull<Label>("UIRoot/HUDContainer/Speedometer/MarginContainer/VBoxContainer/StateLabel");
        _driftBar = GetNodeOrNull<ProgressBar>("UIRoot/HUDContainer/Speedometer/MarginContainer/VBoxContainer/DriftBar");
        _timeValueLabel = GetNodeOrNull<Label>("UIRoot/HUDContainer/Timer/MarginContainer/TimeValue");
        _boostPanel = GetNodeOrNull<PanelContainer>("UIRoot/HUDContainer/BoostPanel");
        _boostBar = GetNodeOrNull<ProgressBar>("UIRoot/HUDContainer/BoostPanel/MarginContainer/VBoxContainer/BoostBar");
        _pauseOverlay = GetNodeOrNull<Control>("UIRoot/PauseOverlay");

        _resumeButton = GetNodeOrNull<Button>("UIRoot/PauseOverlay/CenterContainer/PauseMenu/MarginContainer/VBoxContainer/ResumeButton");
        _restartButton = GetNodeOrNull<Button>("UIRoot/PauseOverlay/CenterContainer/PauseMenu/MarginContainer/VBoxContainer/RestartButton");
        _quitButton = GetNodeOrNull<Button>("UIRoot/PauseOverlay/CenterContainer/PauseMenu/MarginContainer/VBoxContainer/QuitButton");

        if (_resumeButton != null)
            _resumeButton.Pressed += OnResumePressed;
        if (_restartButton != null)
            _restartButton.Pressed += OnRestartPressed;
        if (_quitButton != null)
            _quitButton.Pressed += OnQuitPressed;
        WirePauseFocusNeighbors();

        if (TargetKartPath != null && !TargetKartPath.IsEmpty)
        {
            _targetKart = GetNodeOrNull<Kart>(TargetKartPath);
        }
        // Standalone scene fallback: the kart usually sits next to the UI.
        if (_targetKart == null)
        {
            _targetKart = GetNodeOrNull<Kart>("../Kart");
        }

        if (_pauseOverlay != null)
            _pauseOverlay.Visible = false;

        if (_driftBar != null)
        {
            _driftBar.ShowPercentage = false;
            _driftBar.MinValue = 0.0;
            _driftBar.MaxValue = _targetKart != null ? _targetKart.DriftMaxChargeTime : 1.8;
            _driftBar.Value = 0.0;
        }
        if (_boostBar != null)
        {
            _boostBar.ShowPercentage = false;
            _boostBar.MinValue = 0.0;
            _boostBar.MaxValue = 1.0;
            _boostBar.Value = 1.0;
        }

        // Ensure this UI node can process while the game is paused
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        UpdateSpeedometer();
        UpdateStateWidgets();

        // Update gameplay timer
        if (!GetTree().Paused)
        {
            _elapsedTime += delta;
            FormatAndDisplayTime();
        }

        // Pause/unpause input (ui_cancel covers ESC + pad start/back).
        if (Input.IsActionJustPressed("ui_cancel"))
        {
            TogglePause();
        }
    }

    private void UpdateSpeedometer()
    {
        if (_speedValueLabel == null)
            return;
        if (_targetKart != null)
        {
            int speedKmh = Mathf.RoundToInt(_targetKart.CurrentSpeedKmh);
            _speedValueLabel.Text = speedKmh.ToString();
        }
        else
        {
            _speedValueLabel.Text = "0";
        }
    }

    private void UpdateStateWidgets()
    {
        bool boosting = EndlessRoadMode.Instance != null && EndlessRoadMode.Instance.IsBoosting;
        Kart.DriftPhase phase = _targetKart != null ? _targetKart.CurrentDriftPhase : Kart.DriftPhase.None;
        float driftCharge = _targetKart != null ? _targetKart.DriftCharge : 0.0f;
        float driftMax = _targetKart != null ? Mathf.Max(0.01f, _targetKart.DriftMaxChargeTime) : 1.8f;
        bool braking = _targetKart != null && _targetKart.BrakeInputActive;

        string state;
        Color accent;
        if (boosting)
        {
            state = "BOOST";
            accent = NeonPink;
        }
        else if (phase == Kart.DriftPhase.Holding)
        {
            state = "DRIFT";
            accent = NeonCyanBright;
        }
        else if (phase == Kart.DriftPhase.Initiate)
        {
            state = "DRIFT...";
            accent = NeonCyanBright;
        }
        else if (braking)
        {
            state = "BRAKE";
            accent = NeonAmber;
        }
        else
        {
            state = "CRUISE";
            accent = SoftWhite;
        }

        if (_stateLabel != null && !state.Equals(_lastState, StringComparison.Ordinal))
        {
            _lastState = state;
            _stateLabel.Text = state;
            _stateLabel.AddThemeColorOverride("font_color", accent);
        }
        if (_speedValueLabel != null)
        {
            // Speed stays cyan normally; boost flips it pink so the state
            // reads at a glance without adding another widget.
            Color speedColor = boosting ? NeonPink : (phase != Kart.DriftPhase.None ? NeonCyanBright : NeonCyan);
            _speedValueLabel.AddThemeColorOverride("font_color", speedColor);
        }
        if (_driftBar != null)
        {
            _driftBar.MaxValue = driftMax;
            _driftBar.Value = Mathf.Clamp(driftCharge, 0.0f, driftMax);
            // Keep layout stable; dim the bar when not drifting.
            _driftBar.Modulate = phase != Kart.DriftPhase.None ? new Color(1, 1, 1, 1) : new Color(1, 1, 1, 0.45f);
        }
        if (_boostBar != null && EndlessRoadMode.Instance != null)
        {
            float max = Mathf.Max(0.01f, EndlessRoadMode.Instance.Settings != null ? EndlessRoadMode.Instance.Settings.BoostMaxCharge : 1.0f);
            _boostBar.MaxValue = max;
            _boostBar.Value = Mathf.Clamp(EndlessRoadMode.Instance.Boost, 0.0f, max);
        }
        if (_boostPanel != null)
        {
            // Hide the boost cluster only when there is no mode to read;
            // otherwise keep it pinned so layout does not jump mid-run.
            _boostPanel.Visible = EndlessRoadMode.Instance != null || _targetKart != null;
        }

        if (boosting && !_wasBoosting)
            PulseSpeedPanel();
        else if (!boosting && _wasBoosting)
            ResetSpeedPanelScale();
        _wasBoosting = boosting;
    }

    private void PulseSpeedPanel()
    {
        if (_speedPanel == null || AccessibilitySettings.ReducedMotion)
            return;
        if (_pulseTween != null && _pulseTween.IsValid())
            _pulseTween.Kill();
        _speedPanel.PivotOffset = _speedPanel.Size / 2.0f;
        _pulseTween = CreateTween();
        _pulseTween.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        _pulseTween.TweenProperty(_speedPanel, "scale", new Vector2(1.06f, 1.06f), 0.12);
        _pulseTween.TweenProperty(_speedPanel, "scale", Vector2.One, 0.18);
    }

    private void ResetSpeedPanelScale()
    {
        if (_pulseTween != null && _pulseTween.IsValid())
            _pulseTween.Kill();
        if (_speedPanel != null)
            _speedPanel.Scale = Vector2.One;
    }

    private void FormatAndDisplayTime()
    {
        if (_timeValueLabel == null)
            return;
        int minutes = (int)(_elapsedTime / 60);
        int seconds = (int)(_elapsedTime % 60);
        int milliseconds = (int)((_elapsedTime - (int)_elapsedTime) * 100);

        _timeValueLabel.Text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
    }

    private void WirePauseFocusNeighbors()
    {
        if (_resumeButton == null || _restartButton == null || _quitButton == null)
            return;
        // Explicit cycle so keyboard + d-pad never dead-ends.
        _resumeButton.FocusNeighborTop = _quitButton.GetPath();
        _resumeButton.FocusNeighborBottom = _restartButton.GetPath();
        _restartButton.FocusNeighborTop = _resumeButton.GetPath();
        _restartButton.FocusNeighborBottom = _quitButton.GetPath();
        _quitButton.FocusNeighborTop = _restartButton.GetPath();
        _quitButton.FocusNeighborBottom = _resumeButton.GetPath();
        _resumeButton.FocusNeighborLeft = _resumeButton.FocusNeighborTop;
        _resumeButton.FocusNeighborRight = _resumeButton.FocusNeighborBottom;
        _restartButton.FocusNeighborLeft = _restartButton.FocusNeighborTop;
        _restartButton.FocusNeighborRight = _restartButton.FocusNeighborBottom;
        _quitButton.FocusNeighborLeft = _quitButton.FocusNeighborTop;
        _quitButton.FocusNeighborRight = _quitButton.FocusNeighborBottom;
    }

    public void TogglePause()
    {
        _isPaused = !_isPaused;
        GetTree().Paused = _isPaused;
        if (_pauseOverlay != null)
            _pauseOverlay.Visible = _isPaused;

        if (_isPaused)
        {
            // Focus the resume button for controller navigation support
            if (_resumeButton != null)
                _resumeButton.GrabFocus();
        }
        else
        {
            GetViewport()?.GuiReleaseFocus();
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
        _isPaused = false;
        GetTree().ReloadCurrentScene();
    }

    private void OnQuitPressed()
    {
        GetTree().Quit();
    }
}
