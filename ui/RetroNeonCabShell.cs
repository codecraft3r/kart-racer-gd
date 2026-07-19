using Godot;
using System;
using System.Collections.Generic;

public partial class RetroNeonCabShell : CanvasLayer
{
    [Export] public NodePath KartPath { get; set; } = "../Kart";
    [Export] public NodePath PostProcessMeshPath { get; set; } = "../Camera3D/MeshInstance3D";
    [Export] public int DefaultPixelation { get; set; } = 4;

    private const string VersionText = "v1.86 - PAIN TAXI EDITION";
    private const int PitRepairCost = 100;

    private enum ShellScreen
    {
        Main,
        Multiplayer,
        Gameplay,
        Paused,
        Settings,
        Credits,
        Results
    }

    private Control _shellRoot;
    private Control _crtWarp;
    private Control _mainMenuScreen;
    private Control _multiplayerScreen;
    private Control _gameplayScreen;
    private Control _pauseScreen;
    private Control _settingsScreen;
    private Control _creditsScreen;
    private Control _resultsScreen;
    private RetroScanlineOverlay _scanlineOverlay;
    private RetroVignetteOverlay _crtOverlay;

    private Label _scoreLabel;
    private Label _boostLabel;
    private RetroSpeedometer _speedometer;
    private Label _timerLabel;
    private Label _rankLabel;
    private double _playTime;
    private Button _hostButton;
    private Button _joinButton;
    private Label _pauseRivalsLabel;
    private Label _checkpointLabel;
    private Label _statusLabel;
    private ProgressBar _panicBar;
    private Label _stopLabel;
    private Label _countdownLabel;
    private Label _objectiveLabel;
    private Label _resultTitleLabel;
    
    private bool _wasBoarding = false;
    private float _goLabelAlpha = 0.0f;

    /// <summary>
    /// Centralised kart-control prompt priority. Each frame every subsystem
    /// (pickup zone, repair shop, ...) registers its desired prompt via
    /// <see cref="RequestKartPrompt"/>; the highest-priority one renders on
    /// the shared _stopLabel. Higher enum value = higher priority.
    /// </summary>
    public enum KartPrompt { None = 0, Stop = 1, Wait = 2, Go = 3 }
    private KartPrompt _activePromptKind = KartPrompt.None;
    private Color _activePromptTint = Colors.White;
    private string _activePromptText = string.Empty;
    // Time the current prompt was first requested, used for the GO fade.
    private ulong _activePromptStartedMs;

    // Tracks the previous frame's repair-shop-in-progress state so we can
    // emit a one-shot GO when the repair finishes (without that we'd just
    // flip back to STOP the frame after completion).
    private bool _wasRepairInProgress;

    /// <summary>
    /// Request a kart-control prompt for this frame. Higher-priority prompts
    /// replace lower-priority ones (Go > Wait > Stop). Equal-priority prompts
    /// keep the first one (don't flicker between callers).
    /// </summary>
    public void RequestKartPrompt(KartPrompt kind, Color tint, string text)
    {
        if ((int)kind <= (int)KartPrompt.None) return;
        if ((int)kind < (int)_activePromptKind) return;
        if ((int)kind > (int)_activePromptKind)
        {
            _activePromptKind = kind;
            _activePromptTint = tint;
            _activePromptText = text;
            _activePromptStartedMs = Time.GetTicksMsec();
        }
        else if (_activePromptText != text)
        {
            // Same priority but different caller (e.g. WAIT from repair shop
            // vs WAIT from pickup zone); keep existing text to avoid flicker.
        }
    }
    private Label _resultSummaryLabel;
    private Label _resultStandingsLabel;
    private Button _resultPrimaryButton;
    private Button _pitRepairButton;
    private Label _connectionStatusLabel;
    private LineEdit _joinAddressField;
    private LineEdit _playerNameField;
    private Label _driftMetersLabel;
    private Label _pauseSpeedLabel;
    private Label _pauseDriftLabel;
    private Label _volumeLabel;
    private Label _pixelLabel;
    private Button _audioButton;
    private Button _scanlineButton;
    private Button _crtButton;
    private readonly Dictionary<int, Button> _pixelButtons = new();

    private FontFile _fontBody;
    private FontFile _fontPixel;
    private FontFile _fontOrbitron;
    private FontFile _fontScript;
    private Kart _kart;
    private ShaderMaterial _postProcessMaterial;
    private Transform3D _kartInitialTransform;
    private bool _hasKartInitialTransform;
    private bool _pendingStartRun;
    private bool _modeEventsWired;

    private ShellScreen _currentScreen = ShellScreen.Main;
    private ShellScreen _previousScreen = ShellScreen.Main;
    private int _pixelationFactor;
    private bool _audioEnabled = true;
    private bool _scanlinesEnabled = true;
    private bool _crtEnabled = true;
    private double _score;
    private double _driftMeters;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _pixelationFactor = Mathf.Clamp(DefaultPixelation, 1, 16);

        LoadFonts();
        ResolveSceneReferences();
        BuildShell();
        WireNetworkEvents();
        SetPixelation(_pixelationFactor);
        SetScanlinesEnabled(true);
        SetCrtEnabled(true);
        ShowScreen(ShellScreen.Main);

        if (_pendingStartRun)
            CallDeferred(nameof(StartRun));

        GetViewport().SizeChanged += UpdateCrtTransform;
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(GetViewport()))
            GetViewport().SizeChanged -= UpdateCrtTransform;

        if (GetTree() != null)
            GetTree().Paused = false;

        if (_modeEventsWired && TaxiMode.Instance != null)
        {
            TaxiMode.Instance.MatchStateChanged -= OnMatchStateChanged;
            TaxiMode.Instance.ScoreboardChanged -= OnScoreboardChanged;
            TaxiMode.Instance.CheckpointChanged -= OnCheckpointChanged;
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event.IsActionPressed("ui_cancel"))
        {
            AudioManager.Instance?.PlayUiBack();
            if (_currentScreen == ShellScreen.Gameplay)
                TogglePause();
            else if (_currentScreen == ShellScreen.Paused)
                ResumeRun();
            else if (_currentScreen == ShellScreen.Settings)
                CloseSettings();
            else if (_currentScreen == ShellScreen.Credits)
                CloseCredits();
            else if (_currentScreen == ShellScreen.Multiplayer)
                ShowScreen(ShellScreen.Main);
            else if (_currentScreen == ShellScreen.Results)
                ExitToMainMenu();

            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        WireModeEvents();

        if (_currentScreen != ShellScreen.Gameplay)
            return;

        float speed = GetKartSpeedMetersPerSecond();
        if (!IsNetworked() && TaxiMode.Instance?.Phase == TaxiMode.MatchPhase.Active)
        {
            _score += delta * (45.0 + speed * 6.0);
            _playTime += delta;
        }
        _driftMeters += speed * delta;

        UpdateGameplayStats(speed);
    }

    public void StartRun()
    {
        if (IsShellReady() == false)
        {
            _pendingStartRun = true;
            return;
        }

        _pendingStartRun = false;
        _score = 0.0;
        _playTime = 0.0;
        _driftMeters = 0.0;
        WireModeEvents();
        if (_objectiveLabel != null && TaxiMode.Instance != null)
            _objectiveLabel.Text = $"SHIFT 1  //  QUOTA ${TaxiMode.Instance.WinningCashTarget}";
        UpdateGameplayStats(GetKartSpeedMetersPerSecond());
        ShowScreen(ShellScreen.Gameplay);

        if (!IsNetworked())
        {
            GameManager.Instance?.StartSoloSession();
        }
    }

    public void HostRush()
    {
        if (MultiplayerManager.Instance == null)
        {
            SetConnectionStatus("MULTIPLAYER MANAGER MISSING", true);
            return;
        }

        Error err = MultiplayerManager.Instance.Host();
        SetConnectionStatus(err == Error.Ok ? "HOSTING RUSH..." : $"HOST ERROR: {err}", err != Error.Ok);
    }

    public void JoinRush()
    {
        if (MultiplayerManager.Instance == null)
        {
            SetConnectionStatus("MULTIPLAYER MANAGER MISSING", true);
            return;
        }

        string address = _joinAddressField?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(address))
            address = "127.0.0.1";

        Error err = MultiplayerManager.Instance.Join(address);
        SetConnectionStatus(err == Error.Ok ? $"CONNECTING TO {address}..." : $"JOIN ERROR: {err}", err != Error.Ok);
    }

    public void SetKart(Kart kart)
    {
        if (kart == null || !GodotObject.IsInstanceValid(kart))
        {
            _kart = null;
            _hasKartInitialTransform = false;
            return;
        }

        _kart = kart;
        _hasKartInitialTransform = true;
        _kartInitialTransform = _kart.GlobalTransform;
    }

    public void TogglePause()
    {
        if (_currentScreen == ShellScreen.Gameplay)
        {
            UpdatePauseStats();
            ShowScreen(ShellScreen.Paused);
        }
        else if (_currentScreen == ShellScreen.Paused)
        {
            ResumeRun();
        }
    }

    public void ResumeRun()
    {
        ShowScreen(ShellScreen.Gameplay);
    }

    public void RestartRun()
    {
        ResetKart();
        StartRun();
    }

    public void AdvanceOrRestartRun()
    {
        if (!IsNetworked() && TaxiMode.Instance?.Phase == TaxiMode.MatchPhase.Intermission)
        {
            ShowScreen(ShellScreen.Gameplay);
            GameManager.Instance?.ContinueSoloSession();
            return;
        }

        RestartRun();
    }

    public void BuyPitRepair()
    {
        if (IsNetworked() || TaxiMode.Instance?.Phase != TaxiMode.MatchPhase.Intermission)
            return;

        if (GameManager.Instance?.TryPurchaseRepair(1, PitRepairCost) == true)
            AudioManager.Instance?.PlayUiConfirm();

        RefreshPitRepairButton();
    }

    public void ExitToMainMenu()
    {
        if (IsNetworked())
            MultiplayerManager.Instance?.Disconnect();
        else
            GameManager.Instance?.ResetSoloSession();
        ResetKart();
        ShowScreen(ShellScreen.Main);
    }

    public void OpenSettings(string fromScreen)
    {
        _previousScreen = fromScreen.Equals("pause", StringComparison.OrdinalIgnoreCase) || _currentScreen == ShellScreen.Paused
            ? ShellScreen.Paused
            : ShellScreen.Main;
        ShowScreen(ShellScreen.Settings);
    }

    public void CloseSettings()
    {
        ShowScreen(_previousScreen == ShellScreen.Paused ? ShellScreen.Paused : ShellScreen.Main);
    }

    public void OpenCredits()
    {
        ShowScreen(ShellScreen.Credits);
    }

    public void CloseCredits()
    {
        ShowScreen(ShellScreen.Main);
    }

    public void SetPixelation(int factor)
    {
        _pixelationFactor = Mathf.Clamp(factor, 1, 16);
        _postProcessMaterial?.SetShaderParameter("pixel_size", _pixelationFactor);

        if (_pixelLabel != null)
            _pixelLabel.Text = PixelationLabel(_pixelationFactor);

        foreach (KeyValuePair<int, Button> entry in _pixelButtons)
        {
            bool active = entry.Key == _pixelationFactor;
            ApplyPixelButtonStyle(entry.Value, active);
        }
    }

    public void SetScanlinesEnabled(bool enabled)
    {
        _scanlinesEnabled = enabled;
        if (_scanlineOverlay != null)
            _scanlineOverlay.Visible = enabled;
        if (_scanlineButton != null)
        {
            _scanlineButton.Text = enabled ? "ON" : "OFF";
            ApplyPixelButtonStyle(_scanlineButton, enabled);
        }
    }

    public void SetCrtEnabled(bool enabled)
    {
        _crtEnabled = enabled;
        if (_crtOverlay != null)
            _crtOverlay.Visible = enabled;
        if (_crtButton != null)
        {
            _crtButton.Text = enabled ? "ON" : "OFF";
            ApplyPixelButtonStyle(_crtButton, enabled);
        }
        UpdateCrtTransform();
    }

    private void LoadFonts()
    {
        _fontBody = LoadDynamicFont("res://assets/fonts/VT323-Regular.ttf");
        _fontPixel = LoadDynamicFont("res://assets/fonts/PressStart2P-Regular.ttf");
        _fontOrbitron = LoadDynamicFont("res://assets/fonts/Orbitron-wght.ttf");
        _fontScript = LoadDynamicFont("res://assets/fonts/MrDafoe-Regular.ttf");
    }

    private FontFile LoadDynamicFont(string path)
    {
        FontFile font = new();
        Error err = font.LoadDynamicFont(path);
        if (err != Error.Ok)
        {
            GD.PushWarning($"Unable to load UI font {path}: {err}");
            return null;
        }

        return font;
    }

    private void ResolveSceneReferences()
    {
        _kart = GetNodeOrNull<Kart>(KartPath);
        if (_kart != null)
        {
            _kartInitialTransform = _kart.GlobalTransform;
            _hasKartInitialTransform = true;
        }

        MeshInstance3D postProcessMesh = GetNodeOrNull<MeshInstance3D>(PostProcessMeshPath);
        _postProcessMaterial = postProcessMesh?.MaterialOverride as ShaderMaterial;
    }

    private void BuildShell()
    {
        _shellRoot = FullRectControl("ShellRoot");
        AddChild(_shellRoot);

        _crtWarp = FullRectControl("CrtScreenWarp");
        _shellRoot.AddChild(_crtWarp);

        _mainMenuScreen = BuildMainMenuScreen();
        _multiplayerScreen = BuildMultiplayerScreen();
        _gameplayScreen = BuildGameplayScreen();
        _pauseScreen = BuildPauseScreen();
        _settingsScreen = BuildSettingsScreen();
        _creditsScreen = BuildCreditsScreen();
        _resultsScreen = BuildResultsScreen();

        _crtWarp.AddChild(_mainMenuScreen);
        _crtWarp.AddChild(_multiplayerScreen);
        _crtWarp.AddChild(_gameplayScreen);
        _crtWarp.AddChild(_pauseScreen);
        _crtWarp.AddChild(_settingsScreen);
        _crtWarp.AddChild(_creditsScreen);
        _crtWarp.AddChild(_resultsScreen);

        _audioButton = MakePixelButton("FX: ON", true, 112.0f, 34.0f);
        _audioButton.Name = "AudioToggleButton";
        AnchorTopRight(_audioButton, 16.0f, 16.0f, 116.0f, 36.0f);
        _audioButton.Pressed += ToggleAudio;
        _shellRoot.AddChild(_audioButton);

        _scanlineOverlay = new RetroScanlineOverlay { Name = "ScanlinesLayer" };
        ConfigureFullRect(_scanlineOverlay);
        _shellRoot.AddChild(_scanlineOverlay);

        _crtOverlay = new RetroVignetteOverlay { Name = "CrtVignetteOverlay" };
        ConfigureFullRect(_crtOverlay);
        _shellRoot.AddChild(_crtOverlay);
    }

    private Control BuildMainMenuScreen()
    {
        Control screen = FullRectControl("MainMenuScreen");
        screen.MouseFilter = Control.MouseFilterEnum.Stop;

        ColorRect bg = FullRectColor("MainBackground", Hex("0b0214"));
        screen.AddChild(bg);

        ColorRect headerGradient = new()
        {
            Name = "HeaderGradient",
            Color = Hex("10031f"),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        headerGradient.AnchorRight = 1.0f;
        headerGradient.AnchorBottom = 0.52f;
        screen.AddChild(headerGradient);

        RetroSunControl sun = new() { Name = "NeonSun" };
        AnchorCenter(sun, 320.0f, 320.0f, 0.0f, -22.0f);
        screen.AddChild(sun);

        RetroGridBackground grid = new() { Name = "NeonGrid" };
        ConfigureFullRect(grid);
        screen.AddChild(grid);

        VBoxContainer titleStack = new()
        {
            Name = "TitleStack",
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        titleStack.AnchorLeft = 0.5f;
        titleStack.AnchorRight = 0.5f;
        titleStack.AnchorTop = 0.08f;
        titleStack.AnchorBottom = 0.44f;
        titleStack.OffsetLeft = -440.0f;
        titleStack.OffsetRight = 440.0f;
        titleStack.AddThemeConstantOverride("separation", -8);
        screen.AddChild(titleStack);

        Label scriptLabel = MakeLabel("PAIN", _fontScript, 88, Hex("ff2d7a"), HorizontalAlignment.Center);
        scriptLabel.Name = "EightiesLabel";
        scriptLabel.RotationDegrees = -10.0f;
        scriptLabel.AddThemeColorOverride("font_shadow_color", Hex("ff0055"));
        scriptLabel.AddThemeConstantOverride("shadow_offset_x", 4);
        scriptLabel.AddThemeConstantOverride("shadow_offset_y", 4);
        titleStack.AddChild(scriptLabel);

        Label title = MakeLabel("TAXI", _fontOrbitron, 82, Hex("00f0ff"), HorizontalAlignment.Center);
        title.Name = "ChromeOverdriveTitle";
        title.AddThemeColorOverride("font_outline_color", Hex("003366"));
        title.AddThemeConstantOverride("outline_size", 4);
        title.AddThemeColorOverride("font_shadow_color", Hex("04020a"));
        title.AddThemeConstantOverride("shadow_offset_x", 5);
        title.AddThemeConstantOverride("shadow_offset_y", 5);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        titleStack.AddChild(title);

        Label subtitle = MakeLabel("DOWNTOWN NEVER SLEEPS", _fontBody, 30, Hex("f5c451"), HorizontalAlignment.Center);
        subtitle.Name = "Subtitle";
        subtitle.AddThemeColorOverride("font_outline_color", Hex("10031f"));
        subtitle.AddThemeConstantOverride("outline_size", 6);
        titleStack.AddChild(subtitle);

        VBoxContainer menuButtons = new()
        {
            Name = "MainMenuButtons",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        menuButtons.AnchorLeft = 0.5f;
        menuButtons.AnchorRight = 0.5f;
        menuButtons.AnchorTop = 1.0f;
        menuButtons.AnchorBottom = 1.0f;
        menuButtons.OffsetLeft = -190.0f;
        menuButtons.OffsetTop = -318.0f;
        menuButtons.OffsetRight = 190.0f;
        menuButtons.OffsetBottom = -70.0f;
        menuButtons.AddThemeConstantOverride("separation", 14);
        screen.AddChild(menuButtons);

        Button start = MakePixelButton("START DOWNTOWN SHIFT", true, 360.0f, 58.0f);
        start.Name = "StartRunButton";
        start.Pressed += StartRun;
        menuButtons.AddChild(start);

        Button multiplayer = MakePixelButton("MULTIPLAYER", true, 360.0f, 58.0f);
        multiplayer.Name = "MultiplayerButton";
        multiplayer.Pressed += () => ShowScreen(ShellScreen.Multiplayer);
        menuButtons.AddChild(multiplayer);

        Button settings = MakePixelButton("SETTINGS", false, 360.0f, 58.0f);
        settings.Name = "MainSettingsButton";
        settings.Pressed += () => OpenSettings("main");
        menuButtons.AddChild(settings);

        Button credits = MakePixelButton("CREDITS", false, 360.0f, 58.0f);
        credits.Name = "CreditsButton";
        credits.Pressed += OpenCredits;
        menuButtons.AddChild(credits);

        Label version = MakeLabel(VersionText, _fontBody, 18, Hex("8c89a0"), HorizontalAlignment.Center);
        version.Name = "VersionLabel";
        menuButtons.AddChild(version);

        return screen;
    }

    private Control BuildMultiplayerScreen()
    {
        Control screen = FullRectControl("MultiplayerScreen");
        screen.MouseFilter = Control.MouseFilterEnum.Stop;
        screen.AddChild(FullRectColor("MultiplayerBackdrop", new Color(0.035f, 0.012f, 0.067f, 0.94f)));

        RetroGridBackground grid = new() { Name = "MultiplayerNeonGrid", Modulate = new Color(1, 1, 1, 0.18f) };
        ConfigureFullRect(grid);
        screen.AddChild(grid);

        PanelContainer panel = MakePanel("MultiplayerPanel", 590.0f, 520.0f);
        AnchorCenter(panel, 590.0f, 520.0f);
        screen.AddChild(panel);

        VBoxContainer stack = new()
        {
            Name = "MultiplayerStack",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        stack.AddThemeConstantOverride("separation", 14);
        panel.AddChild(stack);

        stack.AddChild(MakeLabel("PAIN TAXI MULTIPLAYER", _fontBody, 42, Hex("00f0ff"), HorizontalAlignment.Center));
        stack.AddChild(MakeLabel("HOST OR JOIN A UDP 7000 ENET MATCH", _fontBody, 20, Hex("ff007f"), HorizontalAlignment.Center));

        _playerNameField = MakeLineEdit("Driver name", "PLAYER");
        stack.AddChild(WrapDarkBox("PlayerNameBox", _playerNameField));

        _joinAddressField = MakeLineEdit("Server address", "127.0.0.1");
        stack.AddChild(WrapDarkBox("JoinAddressBox", _joinAddressField));

        HBoxContainer row = new()
        {
            Name = "MultiplayerButtonRow",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        row.AddThemeConstantOverride("separation", 12);

        _hostButton = MakePixelButton("HOST RUSH", true, 250.0f, 52.0f);
        _hostButton.Name = "HostRushButton";
        _hostButton.Pressed += HostRush;
        row.AddChild(_hostButton);

        _joinButton = MakePixelButton("JOIN RUSH", true, 250.0f, 52.0f);
        _joinButton.Name = "JoinRushButton";
        _joinButton.Pressed += JoinRush;
        row.AddChild(_joinButton);
        stack.AddChild(row);

        Button back = MakePixelButton("BACK", false, 520.0f, 48.0f);
        back.Name = "BackToMainButton";
        back.Pressed += () => ShowScreen(ShellScreen.Main);
        stack.AddChild(back);

        _connectionStatusLabel = MakeLabel("OFFLINE", _fontBody, 23, Hex("fcd34d"), HorizontalAlignment.Center);
        stack.AddChild(WrapDarkBox("ConnectionStatusBox", _connectionStatusLabel));

        return screen;
    }

    private Control BuildGameplayScreen()
    {
        Control screen = FullRectControl("GameplayScreen");
        screen.MouseFilter = Control.MouseFilterEnum.Ignore;

        HBoxContainer hudRow = new()
        {
            Name = "HudTopLeft",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        hudRow.AnchorLeft = 0.0f;
        hudRow.AnchorTop = 0.0f;
        hudRow.OffsetLeft = 12.0f;
        hudRow.OffsetTop = 12.0f;
        hudRow.OffsetRight = 902.0f;
        hudRow.OffsetBottom = 56.0f;
        hudRow.AddThemeConstantOverride("separation", 6);
        screen.AddChild(hudRow);

        _scoreLabel = MakeLabel("CASH: $0", _fontBody, 21, Colors.White, HorizontalAlignment.Center);
        hudRow.AddChild(WrapPill("ScorePill", _scoreLabel, Hex("f5c451"), 118.0f));

        _boostLabel = MakeLabel("HP: 100%", _fontBody, 21, Colors.White, HorizontalAlignment.Center);
        hudRow.AddChild(WrapPill("BoostPill", _boostLabel, Hex("ed3b8b"), 104.0f));

        _timerLabel = MakeLabel("TIME: SOLO", _fontBody, 21, Colors.White, HorizontalAlignment.Center);
        hudRow.AddChild(WrapPill("TimerPill", _timerLabel, Hex("f5c451"), 128.0f));

        _rankLabel = MakeLabel("RANK: SOLO", _fontBody, 21, Colors.White, HorizontalAlignment.Center);
        hudRow.AddChild(WrapPill("RankPill", _rankLabel, Hex("35e7f2"), 112.0f));

        _checkpointLabel = MakeLabel("FARE: SEARCHING...", _fontBody, 21, Colors.White, HorizontalAlignment.Center);
        hudRow.AddChild(WrapPill("CheckpointPill", _checkpointLabel, Hex("ed3b8b"), 190.0f));

        VBoxContainer statusContainer = new VBoxContainer();
        statusContainer.AddThemeConstantOverride("separation", 2);
        
        _panicBar = new ProgressBar
        {
            Name = "PanicBar",
            MinValue = 0.0,
            MaxValue = 100.0,
            Value = 0.0,
            CustomMinimumSize = new Vector2(170.0f, 10.0f),
            ShowPercentage = false
        };
        var bgStyle = new StyleBoxFlat { BgColor = Hex("111122"), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        var fgStyle = new StyleBoxFlat { BgColor = Hex("00ff00"), CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        _panicBar.AddThemeStyleboxOverride("background", bgStyle);
        _panicBar.AddThemeStyleboxOverride("fill", fgStyle);
        _panicBar.Visible = false; // Hide by default until hired
        statusContainer.AddChild(_panicBar);

        _statusLabel = MakeLabel("STATUS: VACANT", _fontBody, 21, Hex("00ff00"), HorizontalAlignment.Center);
        statusContainer.AddChild(WrapPill("StatusPill", _statusLabel, Hex("111122"), 170.0f));

        statusContainer.AnchorLeft = 0.5f;
        statusContainer.AnchorRight = 0.5f;
        statusContainer.AnchorTop = 1.0f;
        statusContainer.AnchorBottom = 1.0f;
        statusContainer.OffsetLeft = -85.0f;
        statusContainer.OffsetRight = 85.0f;
        statusContainer.OffsetTop = -80.0f;
        statusContainer.OffsetBottom = -20.0f;
        screen.AddChild(statusContainer);

        _stopLabel = MakeLabel(">> STOP <<", _fontOrbitron, 48, Hex("ff0055"), HorizontalAlignment.Center);
        _stopLabel.AnchorLeft = 0.5f;
        _stopLabel.AnchorRight = 0.5f;
        _stopLabel.AnchorTop = 0.33f;
        _stopLabel.AnchorBottom = 0.33f;
        _stopLabel.OffsetLeft = -200.0f;
        _stopLabel.OffsetRight = 200.0f;
        _stopLabel.OffsetTop = -30.0f;
        _stopLabel.OffsetBottom = 30.0f;
        _stopLabel.Visible = false;
        screen.AddChild(_stopLabel);

        Label shiftTag = MakeLabel("DOWNTOWN SHIFT // CH 86", _fontPixel, 10, Hex("9ba8d8"), HorizontalAlignment.Left);
        shiftTag.Name = "ShiftTag";
        shiftTag.AnchorLeft = 0.0f;
        shiftTag.AnchorTop = 0.0f;
        shiftTag.OffsetLeft = 16.0f;
        shiftTag.OffsetTop = 61.0f;
        shiftTag.OffsetRight = 290.0f;
        shiftTag.OffsetBottom = 81.0f;
        shiftTag.AddThemeColorOverride("font_outline_color", Hex("090717"));
        shiftTag.AddThemeConstantOverride("outline_size", 4);
        screen.AddChild(shiftTag);

        Button pause = MakePixelButton("PAUSE [ESC]", false, 148.0f, 40.0f);
        pause.Name = "PauseButton";
        AnchorTopRight(pause, 28.0f, 12.0f, 148.0f, 40.0f);
        pause.Pressed += TogglePause;
        screen.AddChild(pause);

        _countdownLabel = MakeLabel("", _fontOrbitron, 112, Hex("fcd34d"), HorizontalAlignment.Center);
        _countdownLabel.Name = "CountdownLabel";
        _countdownLabel.AnchorLeft = 0.5f;
        _countdownLabel.AnchorRight = 0.5f;
        _countdownLabel.AnchorTop = 0.5f;
        _countdownLabel.AnchorBottom = 0.5f;
        _countdownLabel.OffsetLeft = -180.0f;
        _countdownLabel.OffsetRight = 180.0f;
        _countdownLabel.OffsetTop = -90.0f;
        _countdownLabel.OffsetBottom = 90.0f;
        _countdownLabel.AddThemeColorOverride("font_outline_color", Hex("0b0214"));
        _countdownLabel.AddThemeConstantOverride("outline_size", 10);
        _countdownLabel.Visible = false;
        screen.AddChild(_countdownLabel);

        // objectivePanel and _objectiveLabel removed by request

        _speedometer = new RetroSpeedometer(_fontBody, _fontPixel) { Name = "SpeedometerGauge" };
        _speedometer.AnchorLeft = 1.0f;
        _speedometer.AnchorRight = 1.0f;
        _speedometer.AnchorTop = 1.0f;
        _speedometer.AnchorBottom = 1.0f;
        _speedometer.OffsetLeft = -202.0f;
        _speedometer.OffsetRight = -18.0f;
        _speedometer.OffsetTop = -202.0f;
        _speedometer.OffsetBottom = -18.0f;
        screen.AddChild(_speedometer);

        return screen;
    }

    private Control BuildPauseScreen()
    {
        Control screen = FullRectControl("PauseScreen");
        screen.MouseFilter = Control.MouseFilterEnum.Stop;

        RetroCheckerboardOverlay checkerboard = new() { Name = "CheckerboardDeadspace" };
        ConfigureFullRect(checkerboard);
        screen.AddChild(checkerboard);

        PanelContainer panel = MakePanel("PausePanel", 454.0f, 420.0f);
        AnchorCenter(panel, 454.0f, 420.0f);
        screen.AddChild(panel);

        VBoxContainer stack = new()
        {
            Name = "PauseStack",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        stack.AddThemeConstantOverride("separation", 14);
        panel.AddChild(stack);

        Label title = MakeLabel("- GAME PAUSED -", _fontBody, 45, Hex("ff007f"), HorizontalAlignment.Center);
        title.AddThemeColorOverride("font_shadow_color", Colors.Black);
        title.AddThemeConstantOverride("shadow_offset_x", 2);
        title.AddThemeConstantOverride("shadow_offset_y", 2);
        stack.AddChild(title);

        ColorRect divider = new() { Name = "PauseDivider", Color = Hex("00f0ff"), CustomMinimumSize = new Vector2(0, 3) };
        stack.AddChild(divider);

        HBoxContainer stats = new()
        {
            Name = "PauseStats",
            CustomMinimumSize = new Vector2(0, 78),
            Alignment = BoxContainer.AlignmentMode.Center
        };
        stats.AddThemeConstantOverride("separation", 18);
        stack.AddChild(WrapDarkBox("PauseStatsBox", stats));

        _pauseDriftLabel = AddStat(stats, "DRIFT METERS", "0m", Hex("7bb374"));
        _pauseSpeedLabel = AddStat(stats, "CURRENT SPEED", "000 MPH", Hex("fcd34d"));
        _pauseRivalsLabel = AddStat(stats, "RIVALS OUT", "00 / 01", Hex("ff007f"));

        Button resume = MakePixelButton("RESUME RUN", true, 360.0f, 48.0f);
        resume.Name = "ResumeButton";
        resume.Pressed += ResumeRun;
        stack.AddChild(resume);

        Button settings = MakePixelButton("SETTINGS", false, 360.0f, 48.0f);
        settings.Name = "PauseSettingsButton";
        settings.Pressed += () => OpenSettings("pause");
        stack.AddChild(settings);

        Button restart = MakePixelButton("RESTART", false, 360.0f, 48.0f);
        restart.Name = "RestartButton";
        restart.Pressed += RestartRun;
        stack.AddChild(restart);

        Button quit = MakePixelButton("QUIT TO MAIN", false, 360.0f, 48.0f);
        quit.Name = "QuitToMainButton";
        quit.AddThemeColorOverride("font_color", new Color(1.0f, 0.62f, 0.62f));
        quit.Pressed += ExitToMainMenu;
        stack.AddChild(quit);

        Label hint = MakeLabel("PRESS [ESC] AGAIN TO RESUME DIRECTLY", _fontBody, 17, Hex("5a576a"), HorizontalAlignment.Center);
        stack.AddChild(hint);

        return screen;
    }

    private Control BuildResultsScreen()
    {
        Control screen = FullRectControl("ResultsScreen");
        screen.MouseFilter = Control.MouseFilterEnum.Stop;
        screen.AddChild(FullRectColor("ResultsBackdrop", new Color(0.035f, 0.012f, 0.067f, 0.97f)));

        RetroGridBackground grid = new() { Name = "ResultsNeonGrid", Modulate = new Color(1, 1, 1, 0.3f) };
        ConfigureFullRect(grid);
        screen.AddChild(grid);

        PanelContainer panel = MakePanel("ResultsPanel", 600.0f, 470.0f);
        AnchorCenter(panel, 600.0f, 470.0f);
        screen.AddChild(panel);

        VBoxContainer stack = new()
        {
            Name = "ResultsStack",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        stack.AddThemeConstantOverride("separation", 18);
        panel.AddChild(stack);

        _resultTitleLabel = MakeLabel("SHIFT COMPLETE", _fontOrbitron, 48, Hex("fcd34d"), HorizontalAlignment.Center);
        _resultTitleLabel.Name = "ResultTitleLabel";
        stack.AddChild(_resultTitleLabel);

        _resultSummaryLabel = MakeLabel("DOWNTOWN SHIFT COMPLETE", _fontBody, 28, Colors.White, HorizontalAlignment.Center);
        _resultSummaryLabel.Name = "ResultSummaryLabel";
        stack.AddChild(_resultSummaryLabel);

        _resultStandingsLabel = MakeLabel("CASH: $0  •  RANK: 1/3", _fontBody, 30, Hex("00f0ff"), HorizontalAlignment.Center);
        _resultStandingsLabel.Name = "ResultStandingsLabel";
        stack.AddChild(WrapDarkBox("ResultStandingsBox", _resultStandingsLabel));

        Label hint = MakeLabel("THE CITY IS READY FOR ANOTHER SHIFT", _fontPixel, 13, Hex("ff007f"), HorizontalAlignment.Center);
        stack.AddChild(hint);

        _pitRepairButton = MakePixelButton("REPAIR TAXI - $100", false, 380.0f, 48.0f);
        _pitRepairButton.Name = "PitRepairButton";
        _pitRepairButton.Pressed += BuyPitRepair;
        stack.AddChild(_pitRepairButton);

        _resultPrimaryButton = MakePixelButton("RUN IT AGAIN", true, 380.0f, 54.0f);
        _resultPrimaryButton.Name = "ResultRestartButton";
        _resultPrimaryButton.Pressed += AdvanceOrRestartRun;
        stack.AddChild(_resultPrimaryButton);

        Button main = MakePixelButton("BACK TO MAIN", false, 380.0f, 50.0f);
        main.Name = "ResultMainButton";
        main.Pressed += ExitToMainMenu;
        stack.AddChild(main);

        return screen;
    }

    private Control BuildSettingsScreen()
    {
        Control screen = FullRectControl("SettingsScreen");
        screen.MouseFilter = Control.MouseFilterEnum.Stop;
        screen.AddChild(FullRectColor("SettingsBackdrop", new Color(0.035f, 0.012f, 0.067f, 0.96f)));

        RetroGridBackground grid = new() { Name = "SettingsNeonGrid", Modulate = new Color(1, 1, 1, 0.20f) };
        ConfigureFullRect(grid);
        screen.AddChild(grid);

        PanelContainer panel = MakePanel("SettingsPanel", 560.0f, 470.0f);
        AnchorCenter(panel, 560.0f, 470.0f);
        screen.AddChild(panel);

        VBoxContainer stack = new()
        {
            Name = "SettingsStack",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        stack.AddThemeConstantOverride("separation", 13);
        panel.AddChild(stack);

        Label title = MakeLabel("SYSTEM CONFIG", _fontBody, 40, Hex("00f0ff"), HorizontalAlignment.Center);
        stack.AddChild(title);

        Label subtitle = MakeLabel("RETRO SIMULATION ADJUSTMENTS", _fontBody, 20, Hex("ff007f"), HorizontalAlignment.Center);
        stack.AddChild(subtitle);

        HBoxContainer volumeHeader = new();
        volumeHeader.AddChild(MakeLabel("MASTER AUDIO VOLUME", _fontBody, 24, Hex("efeff5"), HorizontalAlignment.Left));
        _volumeLabel = MakeLabel("80%", _fontBody, 24, Hex("7bb374"), HorizontalAlignment.Right);
        _volumeLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        volumeHeader.AddChild(_volumeLabel);
        stack.AddChild(WrapDarkBox("VolumeBox", MakeSettingStack(volumeHeader, MakeVolumeSlider())));

        HBoxContainer pixelHeader = new();
        pixelHeader.AddChild(MakeLabel("GAME PIXELATION FACTOR", _fontBody, 24, Hex("efeff5"), HorizontalAlignment.Left));
        _pixelLabel = MakeLabel(PixelationLabel(_pixelationFactor), _fontBody, 24, Hex("ff2d7a"), HorizontalAlignment.Right);
        _pixelLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        pixelHeader.AddChild(_pixelLabel);

        GridContainer pixelGrid = new()
        {
            Name = "PixelationGrid",
            Columns = 4
        };
        pixelGrid.AddThemeConstantOverride("h_separation", 8);
        pixelGrid.AddThemeConstantOverride("v_separation", 8);
        AddPixelButton(pixelGrid, "OFF", 1);
        AddPixelButton(pixelGrid, "LOW", 2);
        AddPixelButton(pixelGrid, "MID", 4);
        AddPixelButton(pixelGrid, "MAX", 8);
        stack.AddChild(WrapDarkBox("PixelationBox", MakeSettingStack(pixelHeader, pixelGrid)));

        GridContainer toggles = new()
        {
            Name = "MonitorToggles",
            Columns = 2
        };
        toggles.AddThemeConstantOverride("h_separation", 12);
        toggles.AddThemeConstantOverride("v_separation", 12);
        _crtButton = AddToggleBox(toggles, "CRT BULGE FILTER", () => SetCrtEnabled(!_crtEnabled));
        _scanlineButton = AddToggleBox(toggles, "SCANLINE GRID", () => SetScanlinesEnabled(!_scanlinesEnabled));
        stack.AddChild(toggles);

        Button save = MakePixelButton("SAVE AND APPLY", true, 430.0f, 52.0f);
        save.Name = "SaveApplyButton";
        save.Pressed += CloseSettings;
        stack.AddChild(save);

        return screen;
    }

    private Control BuildCreditsScreen()
    {
        Control screen = FullRectControl("CreditsScreen");
        screen.MouseFilter = Control.MouseFilterEnum.Stop;
        screen.AddChild(FullRectColor("CreditsBackdrop", new Color(0.035f, 0.012f, 0.067f, 0.96f)));

        RetroGridBackground grid = new() { Name = "CreditsNeonGrid", Modulate = new Color(1, 1, 1, 0.20f) };
        ConfigureFullRect(grid);
        screen.AddChild(grid);

        PanelContainer panel = MakePanel("CreditsPanel", 454.0f, 430.0f);
        AnchorCenter(panel, 454.0f, 430.0f);
        screen.AddChild(panel);

        VBoxContainer stack = new()
        {
            Name = "CreditsStack",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        stack.AddThemeConstantOverride("separation", 14);
        panel.AddChild(stack);

        Label title = MakeLabel("CAB CREW", _fontBody, 42, Hex("ff007f"), HorizontalAlignment.Center);
        stack.AddChild(title);

        ScrollContainer scroll = new()
        {
            Name = "CreditsScroll",
            CustomMinimumSize = new Vector2(0, 230)
        };
        VBoxContainer crew = new()
        {
            Name = "CreditsList",
            Alignment = BoxContainer.AlignmentMode.Center
        };
        crew.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(crew);
        AddCredit(crew, "GAME DIRECTOR", "Pixel Taxi Driver");
        AddCredit(crew, "UI DESIGNER", "Synthwave Artist");
        AddCredit(crew, "MUSIC & SYNTH", "Original PAIN TAXI soundtrack");
        AddCredit(crew, "SOUND EFFECTS", "Kenney • rubberduck • OpenGameArt contributors");
        AddCredit(crew, "TIRE SKID", "audible-edge (Tom Haigh), CC BY 3.0");
        AddCredit(crew, "SPECIAL THANKS", "Based on Retro 80's Mood Board Assets");
        stack.AddChild(WrapDarkBox("CreditsScrollBox", scroll));

        Button back = MakePixelButton("BACK TO RUNS", false, 360.0f, 52.0f);
        back.Name = "BackToRunsButton";
        back.Pressed += CloseCredits;
        stack.AddChild(back);

        return screen;
    }

    private void ShowScreen(ShellScreen screen)
    {
        if (IsShellReady() == false)
            return;

        _currentScreen = screen;

        _mainMenuScreen.Visible = screen == ShellScreen.Main;
        _multiplayerScreen.Visible = screen == ShellScreen.Multiplayer;
        _gameplayScreen.Visible = screen == ShellScreen.Gameplay || screen == ShellScreen.Paused || (screen == ShellScreen.Settings && _previousScreen == ShellScreen.Paused);
        _pauseScreen.Visible = screen == ShellScreen.Paused || (screen == ShellScreen.Settings && _previousScreen == ShellScreen.Paused);
        _settingsScreen.Visible = screen == ShellScreen.Settings;
        _creditsScreen.Visible = screen == ShellScreen.Credits;
        _resultsScreen.Visible = screen == ShellScreen.Results;
        _audioButton.Visible = screen == ShellScreen.Main || screen == ShellScreen.Multiplayer || screen == ShellScreen.Settings || screen == ShellScreen.Credits;

        bool gameplayAudio = screen == ShellScreen.Gameplay ||
            screen == ShellScreen.Paused ||
            (screen == ShellScreen.Settings && _previousScreen == ShellScreen.Paused);
        AudioManager.Instance?.SetGameplayMix(gameplayAudio);
        if (AudioManager.Instance != null)
        {
            AudioManager.MusicContext musicContext = screen == ShellScreen.Multiplayer
                ? AudioManager.MusicContext.Lobby
                : gameplayAudio
                    ? AudioManager.MusicContext.Gameplay
                    : screen == ShellScreen.Results
                        ? AudioManager.MusicContext.Results
                        : AudioManager.MusicContext.Menu;
            AudioManager.Instance.SetMusicContext(musicContext);
        }

        bool gameplayActive = screen == ShellScreen.Gameplay;
        if (GetTree() != null)
            GetTree().Paused = !gameplayActive;

        if (screen == ShellScreen.Main)
            FocusFirstButton(_mainMenuScreen);
        else if (screen == ShellScreen.Multiplayer)
            FocusFirstButton(_multiplayerScreen);
        else if (screen == ShellScreen.Paused)
            FocusFirstButton(_pauseScreen);
        else if (screen == ShellScreen.Settings)
            FocusFirstButton(_settingsScreen);
        else if (screen == ShellScreen.Credits)
            FocusFirstButton(_creditsScreen);
        else if (screen == ShellScreen.Results)
            FocusFirstButton(_resultsScreen);
    }

    private void UpdateGameplayStats(float speedMetersPerSecond)
    {
        int mph = Mathf.RoundToInt(speedMetersPerSecond * 2.23694f);

        int peerId = IsNetworked() ? Multiplayer.GetUniqueId() : 1;
        int cash = GameManager.Instance != null ? GameManager.Instance.GetPlayerMoney(peerId) : 0;
        int health = GameManager.Instance != null ? GameManager.Instance.GetPlayerHealth(peerId) : 100;
        TaxiMode mode = TaxiMode.Instance;

        if (_scoreLabel != null)
        {
            int shiftCash = mode?.GetScore(peerId) ?? 0;
            _scoreLabel.Text = mode?.EndlessRunActive == true
                ? $"QUOTA: ${shiftCash}/${mode.CurrentCashQuota}"
                : $"CASH: ${cash}";
        }
        if (_boostLabel != null)
            _boostLabel.Text = $"HP: {health}%";
        if (_speedometer != null)
        {
            _speedometer.CurrentSpeed = mph;
            _speedometer.QueueRedraw();
        }
        if (_timerLabel != null)
        {
            if (mode?.Phase == TaxiMode.MatchPhase.Countdown)
            {
                _timerLabel.Text = $"START: {Mathf.Max(1, Mathf.CeilToInt((float)mode.CountdownRemaining))}";
            }
            else if (mode?.Phase == TaxiMode.MatchPhase.Active)
            {
                int totalSeconds = Mathf.Max(0, Mathf.CeilToInt((float)mode.TimeRemaining));
                int minutes = totalSeconds / 60;
                int seconds = totalSeconds % 60;
                _timerLabel.Text = $"TIME: {minutes:00}:{seconds:00}";
            }
            else if (mode?.Phase == TaxiMode.MatchPhase.Finished || mode?.Phase == TaxiMode.MatchPhase.Intermission)
                _timerLabel.Text = "TIME: DONE";
            else
                _timerLabel.Text = "TIME: --:--";
        }
        if (_rankLabel != null && mode != null)
            _rankLabel.Text = $"RANK: {mode.GetRank(peerId)}/{Mathf.Max(1, mode.Scores.Count)}";

        if (_kart != null && GodotObject.IsInstanceValid(_kart))
        {
            bool isBoarding = _kart.BoardingProgress > 0.0f;
            if (_wasBoarding && !isBoarding && _kart.ActivePassenger.HasValue)
            {
                // Boarding just completed — kick the GO fade.
                RequestKartPrompt(KartPrompt.Go, new Color(0.0f, 1.0f, 0.0f), ">> GO <<");
            }
            _wasBoarding = isBoarding;

            // Clear any stale prompt state from the previous frame so each
            // subsystem can re-register its own. Subsystems below call
            // RequestKartPrompt with their desired priority.
            _activePromptKind = KartPrompt.None;

            if (_kart.ActivePassenger.HasValue)
            {
                if (_goLabelAlpha > 0.0f)
                {
                    RequestKartPrompt(KartPrompt.Go, new Color(0.0f, 1.0f, 0.0f, _goLabelAlpha), ">> GO <<");
                    if (_kart.LinearVelocity.Length() > 2.0f)
                        _goLabelAlpha -= (float)GetProcessDeltaTime() * 1.5f;
                }

                var passenger = _kart.ActivePassenger.Value;
                int panic = Mathf.RoundToInt(_kart.PanicMeter);
                Vector3 destination = mode?.GetPlayerDestination(peerId) ?? Vector3.Zero;
                if (destination == Vector3.Zero && mode != null)
                    destination = mode.ActiveDestination;
                int distance = destination == Vector3.Zero ? 0 : Mathf.RoundToInt(_kart.GlobalPosition.DistanceTo(destination));

                if (_checkpointLabel != null)
                    _checkpointLabel.Text = $"DROPOFF: {distance}m";
                if (_objectiveLabel != null)
                    _objectiveLabel.Text = "DELIVER THE FARE  //  KEEP PANIC BELOW 100%";

                if (_driftMetersLabel != null)
                {
                    string distStr = passenger.Distance.ToString().ToUpperInvariant();
                    string wealthStr = new string('$', (int)passenger.Wealth + 1);
                    _driftMetersLabel.Text = $"FARE: {distStr} ({wealthStr})";
                }

                if (_statusLabel != null)
                {
                    _statusLabel.Text = "STATUS: HIRED";
                    _statusLabel.AddThemeColorOverride("font_color", Hex("ff0055"));
                }

                if (_panicBar != null)
                {
                    _panicBar.Visible = true;
                    _panicBar.Value = _kart.PanicMeter;
                    Color panicColor = new Color(0, 1, 0).Lerp(new Color(1, 0, 0), _kart.PanicMeter / 100.0f);
                    if (_panicBar.HasThemeStyleboxOverride("fill"))
                    {
                        var fillStyle = _panicBar.GetThemeStylebox("fill") as StyleBoxFlat;
                        if (fillStyle != null)
                        {
                            fillStyle.BgColor = panicColor;
                        }
                    }
                }
            }
            else
            {
                if (_checkpointLabel != null)
                {
                    Vector3 pickup = mode?.GetNearestPickupPosition(_kart.GlobalPosition) ?? Vector3.Zero;
                    int distance = pickup == Vector3.Zero ? 0 : Mathf.RoundToInt(_kart.GlobalPosition.DistanceTo(pickup));
                    _checkpointLabel.Text = pickup == Vector3.Zero ? "FARE: SEARCHING..." : $"PICKUP: {distance}m";

                    if (distance > 0 && distance <= 5 && _kart.LinearVelocity.Length() >= 0.8f && _kart.BoardingProgress == 0.0f)
                    {
                        Color flashTint = (Time.GetTicksMsec() % 500 < 250) ? Colors.White : Hex("ff0055");
                        RequestKartPrompt(KartPrompt.Stop, flashTint, ">> STOP <<");
                    }
                }

                if (_objectiveLabel != null && mode != null)
                    _objectiveLabel.Text = mode.EndlessRunActive
                        ? $"SHIFT {mode.ShiftNumber}  //  QUOTA ${mode.CurrentCashQuota}"
                        : $"EARN ${mode.WinningCashTarget}  //  STOP IN A GLOWING PICKUP ZONE";

                if (_driftMetersLabel != null)
                    _driftMetersLabel.Text = "NO PASSENGER";

                if (_statusLabel != null)
                {
                    if (_kart.BoardingProgress > 0.0f)
                    {
                        int boardingPercent = Mathf.RoundToInt(_kart.BoardingProgress * 100.0f);
                        _statusLabel.Text = $"LOADING: {boardingPercent}%";
                        _statusLabel.AddThemeColorOverride("font_color", Hex("f5c451"));

                        Color waitTint = (Time.GetTicksMsec() % 500 < 250) ? Colors.White : Hex("f5c451");
                        RequestKartPrompt(KartPrompt.Wait, waitTint, ">> WAIT <<");
                    }
                    else
                    {
                        _statusLabel.Text = "STATUS: VACANT";
                        _statusLabel.AddThemeColorOverride("font_color", Hex("00ff00"));
                    }
                }
                
                if (_panicBar != null)
                {
                    _panicBar.Visible = false;
                }
            }
        }
        else
        {
            if (_checkpointLabel != null && !IsNetworked())
                _checkpointLabel.Text = "CHECKPOINT: --";
            if (_driftMetersLabel != null)
                _driftMetersLabel.Text = $"{Mathf.RoundToInt((float)_driftMeters):N0}m";
        }

        UpdateRepairKartPrompt();
        RenderActiveKartPrompt();
    }

    private void UpdateRepairKartPrompt()
    {
        if (_currentScreen != ShellScreen.Gameplay) return;
        if (_kart == null || !GodotObject.IsInstanceValid(_kart)) return;

        RepairShop shop = TrackBuilder.Instance?.GetNearestRepairShop(_kart.GlobalPosition);
        if (shop == null || !GodotObject.IsInstanceValid(shop))
        {
            _wasRepairInProgress = false;
            return;
        }

        if (!shop.TryGetPromptForLocalKart(out _, out bool inProgress, out _))
        {
            _wasRepairInProgress = false;
            return;
        }

        // Detect the transition from in-progress -> done and emit a one-shot
        // GO prompt. Higher priority than STOP so it wins on the frame of
        // completion; the standard GO fade animation takes it from there.
        if (_wasRepairInProgress && !inProgress)
        {
            RequestKartPrompt(KartPrompt.Go, new Color(0.0f, 1.0f, 0.0f), ">> GO <<");
            _goLabelAlpha = 1.0f;
        }
        _wasRepairInProgress = inProgress;

        // Only emit prompts when the shop is actively doing something:
        //   WAIT while the repair timer ticks, GO on the frame of completion.
        // No STOP prompt — the shop stays silent until it's actually working,
        // so the prompt isn't noisy while the player is just driving in.
        if (inProgress)
        {
            Color waitTint = (Time.GetTicksMsec() % 500 < 250) ? Colors.White : Hex("f5c451");
            RequestKartPrompt(KartPrompt.Wait, waitTint, ">> WAIT <<");
        }
    }

    /// <summary>
    /// Single point that writes to _stopLabel. Reads the highest-priority
    /// prompt registered this frame and applies it. Subsystems call
    /// <see cref="RequestKartPrompt"/> during UpdateGameplayStats; this is the
    /// final step.
    /// </summary>
    private void RenderActiveKartPrompt()
    {
        if (_stopLabel == null) return;

        if (_activePromptKind == KartPrompt.None)
        {
            _stopLabel.Visible = false;
            return;
        }

        _stopLabel.Visible = true;
        _stopLabel.Text = _activePromptText;
        _stopLabel.Modulate = _activePromptTint;
    }

    private void UpdatePauseStats()
    {
        int mph = Mathf.RoundToInt(GetKartSpeedMetersPerSecond() * 2.23694f);
        if (_pauseDriftLabel != null)
            _pauseDriftLabel.Text = $"{Mathf.RoundToInt((float)_driftMeters):N0}m";
        if (_pauseSpeedLabel != null)
            _pauseSpeedLabel.Text = $"{mph:000} MPH";
        if (_pauseRivalsLabel != null)
        {
            if (IsNetworked())
            {
                int rivals = Multiplayer.GetPeers().Length;
                _pauseRivalsLabel.Text = $"{rivals:02} / {rivals + 1:02}";
            }
            else
            {
                int drivers = Mathf.Max(1, GameManager.Instance?.GetRegisteredPlayerCount() ?? 1);
                _pauseRivalsLabel.Text = $"{drivers - 1:00} / {drivers:00}";
            }
        }
    }

    private float GetKartSpeedMetersPerSecond()
    {
        if (_kart == null)
            return 0.0f;

        if (GodotObject.IsInstanceValid(_kart) == false)
        {
            _kart = null;
            _hasKartInitialTransform = false;
            return 0.0f;
        }

        return _kart.LinearVelocity.Length();
    }

    private void ResetKart()
    {
        if (_kart == null || !_hasKartInitialTransform || GodotObject.IsInstanceValid(_kart) == false)
            return;

        _kart.GlobalTransform = _kartInitialTransform;
        _kart.LinearVelocity = Vector3.Zero;
        _kart.AngularVelocity = Vector3.Zero;
        _score = 0.0;
        _driftMeters = 0.0;
    }

    private bool IsShellReady()
    {
        return _mainMenuScreen != null &&
            _multiplayerScreen != null &&
            _gameplayScreen != null &&
            _pauseScreen != null &&
            _settingsScreen != null &&
            _creditsScreen != null &&
            _resultsScreen != null;
    }

    private void ToggleAudio()
    {
        _audioEnabled = !_audioEnabled;
        int busIndex = AudioServer.GetBusIndex("Master");
        if (busIndex >= 0)
            AudioServer.SetBusMute(busIndex, !_audioEnabled);

        if (_audioEnabled)
            AudioManager.Instance?.PlayUiToggle();

        _audioButton.Text = _audioEnabled ? "FX: ON" : "FX: OFF";
        ApplyPixelButtonStyle(_audioButton, _audioEnabled);
    }

    private void WireNetworkEvents()
    {
        if (MultiplayerManager.Instance == null)
            return;

        MultiplayerManager.Instance.ConnectionStateChanged += OnConnectionStateChanged;
    }

    private void WireModeEvents()
    {
        if (_modeEventsWired || TaxiMode.Instance == null)
            return;

        TaxiMode.Instance.MatchStateChanged += OnMatchStateChanged;
        TaxiMode.Instance.ScoreboardChanged += OnScoreboardChanged;
        TaxiMode.Instance.CheckpointChanged += OnCheckpointChanged;
        _modeEventsWired = true;
    }

    private void OnConnectionStateChanged(MultiplayerManager.ConnectionState state, string message)
    {
        SetConnectionStatus(message?.ToUpperInvariant() ?? state.ToString().ToUpperInvariant(), state == MultiplayerManager.ConnectionState.Failed);

        if (state == MultiplayerManager.ConnectionState.InMatch || state == MultiplayerManager.ConnectionState.Hosting)
        {
            StartRun();
            SetMultiplayerButtonsDisabled(false);
        }
        else if (state == MultiplayerManager.ConnectionState.Disconnected || state == MultiplayerManager.ConnectionState.Failed)
        {
            ShowScreen(ShellScreen.Multiplayer);
            SetMultiplayerButtonsDisabled(false);
        }
        else if (state == MultiplayerManager.ConnectionState.Connecting)
        {
            SetMultiplayerButtonsDisabled(true);
        }
    }

    private void SetMultiplayerButtonsDisabled(bool disabled)
    {
        if (_hostButton != null)
            _hostButton.Disabled = disabled;
        if (_joinButton != null)
            _joinButton.Disabled = disabled;
    }

    private void OnMatchStateChanged(double timeRemaining, bool matchActive, int winnerPeerId)
    {
        TaxiMode mode = TaxiMode.Instance;
        int totalSeconds = Mathf.Max(0, Mathf.CeilToInt((float)timeRemaining));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;

        if (_countdownLabel != null)
        {
            bool countingDown = mode?.Phase == TaxiMode.MatchPhase.Countdown;
            _countdownLabel.Visible = countingDown;
            _countdownLabel.Text = countingDown
                ? Mathf.Max(1, Mathf.CeilToInt((float)mode.CountdownRemaining)).ToString()
                : "";
        }

        if (_timerLabel != null && mode?.Phase == TaxiMode.MatchPhase.Active)
            _timerLabel.Text = $"TIME: {minutes:00}:{seconds:00}";

        if ((mode?.Phase == TaxiMode.MatchPhase.Finished || mode?.Phase == TaxiMode.MatchPhase.Intermission) && _currentScreen != ShellScreen.Results)
            ShowResults(winnerPeerId);
    }

    private void ShowResults(int winnerPeerId)
    {
        TaxiMode mode = TaxiMode.Instance;
        int localPeerId = IsNetworked() ? Multiplayer.GetUniqueId() : 1;
        int score = mode?.GetScore(localPeerId) ?? 0;
        bool shiftCleared = mode?.Phase == TaxiMode.MatchPhase.Intermission;

        if (_resultTitleLabel != null)
        {
            _resultTitleLabel.Text = shiftCleared ? "SHIFT CLEARED!" : "RUN OVER";
            _resultTitleLabel.AddThemeColorOverride("font_color", shiftCleared ? Hex("fcd34d") : Hex("ff007f"));
        }

        if (_resultSummaryLabel != null)
            _resultSummaryLabel.Text = shiftCleared
                ? $"SHIFT {mode.ShiftNumber} QUOTA CRUSHED"
                : $"MISSED THE ${mode?.CurrentCashQuota ?? 0} QUOTA";
        if (_resultStandingsLabel != null)
            _resultStandingsLabel.Text = shiftCleared
                ? $"SHIFT: ${score}  •  BANK: ${GameManager.Instance?.GetPlayerMoney(localPeerId) ?? 0}  •  NEXT: ${mode.CurrentCashQuota + mode.EndlessQuotaStep}"
                : $"TOTAL: ${mode?.TotalRunCash ?? 0}  •  SHIFTS: {Mathf.Max(0, (mode?.ShiftNumber ?? 1) - 1)}";
        if (_resultPrimaryButton != null)
            _resultPrimaryButton.Text = shiftCleared ? "NEXT SHIFT" : "RUN IT AGAIN";
        RefreshPitRepairButton();

        ShowScreen(ShellScreen.Results);
    }

    private void RefreshPitRepairButton()
    {
        if (_pitRepairButton == null)
            return;

        bool availableAtPit = !IsNetworked() && TaxiMode.Instance?.Phase == TaxiMode.MatchPhase.Intermission;
        _pitRepairButton.Visible = availableAtPit;
        if (!availableAtPit)
            return;

        int health = GameManager.Instance?.GetPlayerHealth(1) ?? 100;
        int bank = GameManager.Instance?.GetPlayerMoney(1) ?? 0;
        _pitRepairButton.Disabled = health >= 100 || bank < PitRepairCost;
        _pitRepairButton.Text = health >= 100
            ? "TAXI AT FULL HP"
            : bank < PitRepairCost
                ? $"REPAIR NEEDS ${PitRepairCost}"
                : $"REPAIR TAXI - ${PitRepairCost}";
    }

    private void OnScoreboardChanged(int peerId, int score, int rank)
    {
        int uniqueId = IsNetworked() ? Multiplayer.GetUniqueId() : 1;
        if (peerId != uniqueId)
            return;

        if (_scoreLabel != null)
            _scoreLabel.Text = $"CASH: ${score}";
        if (_rankLabel != null)
            _rankLabel.Text = $"RANK: {rank}";
    }

    private void OnCheckpointChanged(int index, Vector3 position)
    {
        // Handled dynamically in UpdateGameplayStats every frame
    }

    private void SetConnectionStatus(string text, bool isError)
    {
        if (_connectionStatusLabel == null)
            return;

        _connectionStatusLabel.Text = text;
        _connectionStatusLabel.AddThemeColorOverride("font_color", isError ? new Color(1.0f, 0.62f, 0.62f) : Hex("fcd34d"));
    }

    private bool IsNetworked()
    {
        return Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;
    }

    private void OnVolumeValueChanged(double value)
    {
        float normalized = Mathf.Clamp((float)value / 100.0f, 0.0f, 1.0f);
        int busIndex = AudioServer.GetBusIndex("Master");
        if (busIndex >= 0)
            AudioServer.SetBusVolumeDb(busIndex, normalized <= 0.001f ? -80.0f : Mathf.LinearToDb(normalized));

        if (_volumeLabel != null)
            _volumeLabel.Text = $"{Mathf.RoundToInt((float)value)}%";
    }

    private HSlider MakeVolumeSlider()
    {
        HSlider slider = new()
        {
            Name = "VolumeSlider",
            MinValue = 0.0,
            MaxValue = 100.0,
            Step = 1.0,
            Value = 80.0,
            CustomMinimumSize = new Vector2(0, 28)
        };
        slider.ValueChanged += OnVolumeValueChanged;
        return slider;
    }

    private void AddPixelButton(GridContainer grid, string text, int factor)
    {
        Button button = MakePixelButton(text, factor == _pixelationFactor, 92.0f, 36.0f);
        button.Name = $"Pixelation{text}Button";
        button.Pressed += () => SetPixelation(factor);
        _pixelButtons[factor] = button;
        grid.AddChild(button);
    }

    private Button AddToggleBox(GridContainer grid, string label, Action pressed)
    {
        VBoxContainer box = new()
        {
            Name = label.Replace(" ", string.Empty) + "Box",
            CustomMinimumSize = new Vector2(210, 92)
        };
        box.AddThemeConstantOverride("separation", 8);
        box.AddChild(MakeLabel(label, _fontBody, 22, Hex("efeff5"), HorizontalAlignment.Center));

        Button button = MakePixelButton("ON", true, 160.0f, 36.0f);
        button.Pressed += pressed;
        box.AddChild(button);
        grid.AddChild(WrapDarkBox(box.Name + "Panel", box));
        return button;
    }

    private VBoxContainer MakeSettingStack(Control first, Control second)
    {
        VBoxContainer stack = new()
        {
            CustomMinimumSize = new Vector2(430, 86)
        };
        stack.AddThemeConstantOverride("separation", 8);
        stack.AddChild(first);
        stack.AddChild(second);
        return stack;
    }

    private Label AddStat(HBoxContainer parent, string label, string value, Color valueColor)
    {
        VBoxContainer stack = new()
        {
            CustomMinimumSize = new Vector2(110, 62),
            Alignment = BoxContainer.AlignmentMode.Center
        };
        stack.AddChild(MakeLabel(label, _fontBody, 17, Hex("8c89a0"), HorizontalAlignment.Center));
        Label valueLabel = MakeLabel(value, _fontBody, 26, valueColor, HorizontalAlignment.Center);
        stack.AddChild(valueLabel);
        parent.AddChild(stack);
        return valueLabel;
    }

    private void AddCredit(VBoxContainer parent, string role, string person)
    {
        VBoxContainer item = new()
        {
            Alignment = BoxContainer.AlignmentMode.Center
        };
        item.AddChild(MakeLabel(role, _fontBody, 24, Hex("00f0ff"), HorizontalAlignment.Center));
        item.AddChild(MakeLabel(person, _fontBody, 24, Colors.White, HorizontalAlignment.Center));
        parent.AddChild(item);
    }

    private PanelContainer WrapPill(string name, Label label, Color accentColor, float minimumWidth = 126.0f)
    {
        PanelContainer panel = new()
        {
            Name = name,
            CustomMinimumSize = new Vector2(minimumWidth, 42),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        panel.AddThemeStyleboxOverride("panel", MakePillStyle(accentColor));
        label.AddThemeColorOverride("font_color", accentColor);
        panel.AddChild(label);
        return panel;
    }

    private PanelContainer WrapDarkBox(string name, Control child)
    {
        PanelContainer panel = new()
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        panel.AddThemeStyleboxOverride("panel", MakeDarkBoxStyle());
        panel.AddChild(child);
        return panel;
    }

    private PanelContainer MakePanel(string name, float width, float height)
    {
        PanelContainer panel = new()
        {
            Name = name,
            CustomMinimumSize = new Vector2(width, height)
        };
        panel.AddThemeStyleboxOverride("panel", MakePanelStyle());
        return panel;
    }

    private Button MakePixelButton(string text, bool green, float width, float height)
    {
        Button button = new()
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, height),
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand
        };
        button.AddThemeFontOverride("font", _fontPixel);
        button.AddThemeFontSizeOverride("font_size", 13);
        button.AddThemeColorOverride("font_focus_color", Hex("100a20"));
        button.MouseEntered += () => AudioManager.Instance?.PlayUiHover();
        button.FocusEntered += () => AudioManager.Instance?.PlayUiHover();
        button.Pressed += () => AudioManager.Instance?.PlayUiConfirm();
        ApplyPixelButtonStyle(button, green);
        return button;
    }

    private LineEdit MakeLineEdit(string placeholder, string text)
    {
        LineEdit field = new()
        {
            PlaceholderText = placeholder,
            Text = text,
            CustomMinimumSize = new Vector2(520.0f, 44.0f),
            FocusMode = Control.FocusModeEnum.All,
            MouseDefaultCursorShape = Control.CursorShape.Ibeam
        };
        field.AddThemeFontOverride("font", _fontBody);
        field.AddThemeFontSizeOverride("font_size", 26);
        field.AddThemeColorOverride("font_color", Colors.White);
        field.AddThemeColorOverride("font_placeholder_color", Hex("8c89a0"));
        return field;
    }

    private void ApplyPixelButtonStyle(Button button, bool green)
    {
        if (button == null)
            return;

        Color bg = green ? Hex("10142a") : Hex("21152f");
        Color border = green ? Hex("35e7f2") : Hex("6e5d89");
        Color hover = green ? Hex("35e7f2") : Hex("342044");
        Color text = green ? Hex("35e7f2") : Hex("f2efff");

        button.AddThemeStyleboxOverride("normal", MakeButtonStyle(bg, border, Hex("080612")));
        button.AddThemeStyleboxOverride("hover", MakeButtonStyle(hover, green ? Hex("a9fbff") : Hex("a18abd"), Hex("080612")));
        button.AddThemeStyleboxOverride("pressed", MakeButtonStyle(Hex("d72676"), Hex("ff7eb6"), Hex("080612")));
        button.AddThemeStyleboxOverride("focus", MakeButtonStyle(Hex("f5c451"), Hex("fff0a8"), Hex("d72676")));
        button.AddThemeColorOverride("font_color", text);
        button.AddThemeColorOverride("font_hover_color", green ? Hex("090717") : Hex("f7f4ff"));
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", Hex("090717"));
        button.AddThemeColorOverride("font_outline_color", Hex("090717"));
        button.AddThemeConstantOverride("outline_size", 1);
    }

    private Label MakeLabel(string text, FontFile font, int size, Color color, HorizontalAlignment alignment)
    {
        Label label = new()
        {
            Text = text,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        if (font != null)
            label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private Control FullRectControl(string name)
    {
        Control control = new()
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ProcessMode = ProcessModeEnum.Always
        };
        ConfigureFullRect(control);
        return control;
    }

    private ColorRect FullRectColor(string name, Color color)
    {
        ColorRect rect = new()
        {
            Name = name,
            Color = color,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        ConfigureFullRect(rect);
        return rect;
    }

    private void ConfigureFullRect(Control control)
    {
        control.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        control.OffsetLeft = 0.0f;
        control.OffsetTop = 0.0f;
        control.OffsetRight = 0.0f;
        control.OffsetBottom = 0.0f;
    }

    private void AnchorCenter(Control control, float width, float height, float xOffset = 0.0f, float yOffset = 0.0f)
    {
        control.AnchorLeft = 0.5f;
        control.AnchorRight = 0.5f;
        control.AnchorTop = 0.5f;
        control.AnchorBottom = 0.5f;
        control.OffsetLeft = -width * 0.5f + xOffset;
        control.OffsetRight = width * 0.5f + xOffset;
        control.OffsetTop = -height * 0.5f + yOffset;
        control.OffsetBottom = height * 0.5f + yOffset;
    }

    private void AnchorTopRight(Control control, float right, float top, float width, float height)
    {
        control.AnchorLeft = 1.0f;
        control.AnchorRight = 1.0f;
        control.AnchorTop = 0.0f;
        control.AnchorBottom = 0.0f;
        control.OffsetLeft = -right - width;
        control.OffsetRight = -right;
        control.OffsetTop = top;
        control.OffsetBottom = top + height;
    }

    private void FocusFirstButton(Node parent)
    {
        Button button = FindFirstButton(parent);
        button?.GrabFocus();
    }

    private Button FindFirstButton(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            if (child is Button button)
                return button;

            Button nested = FindFirstButton(child);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private void UpdateCrtTransform()
    {
        if (_crtWarp == null)
            return;

        if (_crtEnabled)
        {
            Vector2 size = GetViewport().GetVisibleRect().Size;
            _crtWarp.Scale = new Vector2(1.01f, 1.01f);
            _crtWarp.Position = size * -0.005f;
        }
        else
        {
            _crtWarp.Scale = Vector2.One;
            _crtWarp.Position = Vector2.Zero;
        }
    }

    private StyleBoxFlat MakeButtonStyle(Color bg, Color border, Color shadow)
    {
        StyleBoxFlat style = new()
        {
            BgColor = bg,
            BorderColor = border,
            ShadowColor = shadow,
            ShadowSize = 3,
            ShadowOffset = new Vector2(3, 3)
        };
        style.SetBorderWidthAll(3);
        style.SetCornerRadiusAll(0);
        style.SetContentMarginAll(8);
        return style;
    }

    private StyleBoxFlat MakePillStyle(Color accentColor)
    {
        StyleBoxFlat style = new()
        {
            BgColor = new Color(0.025f, 0.02f, 0.08f, 0.88f),
            BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.82f),
            ShadowColor = new Color(0, 0, 0, 0.70f),
            ShadowSize = 2,
            ShadowOffset = new Vector2(2, 2)
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(0);
        style.SetContentMarginAll(8);
        return style;
    }

    private StyleBoxFlat MakePanelStyle()
    {
        StyleBoxFlat style = new()
        {
            BgColor = Hex("171126"),
            BorderColor = Hex("6e5d89"),
            ShadowColor = Colors.Black,
            ShadowSize = 8,
            ShadowOffset = new Vector2(8, 8)
        };
        style.SetBorderWidthAll(4);
        style.SetCornerRadiusAll(0);
        style.SetContentMarginAll(22);
        return style;
    }

    private StyleBoxFlat MakeDarkBoxStyle()
    {
        StyleBoxFlat style = new()
        {
            BgColor = new Color(0, 0, 0, 0.48f),
            BorderColor = Hex("15141d")
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(0);
        style.SetContentMarginAll(12);
        return style;
    }

    private string PixelationLabel(int factor)
    {
        return factor switch
        {
            1 => "1x (SHARP)",
            2 => "2x (SMOOTH)",
            4 => "4x (RETRO)",
            8 => "8x (CHUNKY)",
            _ => $"{factor}x"
        };
    }

    private static Color Hex(string hex)
    {
        return Color.FromHtml("#" + hex.TrimStart('#'));
    }
}

internal partial class RetroScanlineOverlay : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        for (float y = 0.0f; y < size.Y; y += 4.0f)
            DrawRect(new Rect2(0.0f, y + 3.0f, size.X, 1.0f), new Color(0, 0, 0, 0.12f));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }
}

internal partial class RetroVignetteOverlay : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        DrawRect(new Rect2(0, 0, size.X, 58), new Color(0, 0, 0, 0.12f));
        DrawRect(new Rect2(0, size.Y - 90, size.X, 90), new Color(0, 0, 0, 0.20f));
        DrawRect(new Rect2(0, 0, 58, size.Y), new Color(0, 0, 0, 0.16f));
        DrawRect(new Rect2(size.X - 58, 0, 58, size.Y), new Color(0, 0, 0, 0.16f));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }
}

internal partial class RetroGridBackground : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        float horizon = size.Y * 0.50f;
        Color lineColor = new(1.0f, 0.0f, 0.50f, 0.24f);

        for (int i = -12; i <= 12; i++)
        {
            float topX = size.X * 0.5f + i * 23.0f;
            float bottomX = size.X * 0.5f + i * 96.0f;
            DrawLine(new Vector2(topX, horizon), new Vector2(bottomX, size.Y), lineColor, 1.0f);
        }

        for (int i = 0; i < 14; i++)
        {
            float t = i / 13.0f;
            float y = Mathf.Lerp(horizon, size.Y, t * t);
            DrawLine(new Vector2(0, y), new Vector2(size.X, y), lineColor, 1.0f);
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }
}

internal partial class RetroSunControl : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Vector2 center = Size * 0.5f;
        float radius = Mathf.Min(Size.X, Size.Y) * 0.48f;
        DrawCircle(center, radius, Color.FromHtml("#f12711"));
        DrawCircle(center + new Vector2(0, -14), radius * 0.78f, Color.FromHtml("#f5af19"));

        Color cutColor = Color.FromHtml("#10031f");
        for (int i = 0; i < 7; i++)
        {
            float y = center.Y + i * 18.0f;
            DrawRect(new Rect2(center.X - radius, y, radius * 2.0f, 7.0f), cutColor);
        }
    }
}

internal partial class RetroCheckerboardOverlay : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        const float cell = 32.0f;
        Vector2 size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0, 0, 0, 0.70f));

        for (float y = 0.0f; y < size.Y; y += cell)
        {
            for (float x = 0.0f; x < size.X; x += cell)
            {
                bool dark = ((int)(x / cell) + (int)(y / cell)) % 2 == 0;
                Color color = dark ? Color.FromHtml("#18181b") : Color.FromHtml("#0c0c0e");
                color.A = 0.85f;
                DrawRect(new Rect2(x, y, cell, cell), color);
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
            QueueRedraw();
    }
}

internal partial class RetroSpeedometer : Control
{
    private readonly FontFile _fontBody;
    private readonly FontFile _fontPixel;

    public float CurrentSpeed { get; set; } = 0.0f;
    public float MaxSpeed { get; set; } = 100.0f;

    public RetroSpeedometer(FontFile fontBody, FontFile fontPixel)
    {
        _fontBody = fontBody;
        _fontPixel = fontPixel;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        Vector2 center = size * 0.5f;
        float radius = Mathf.Min(size.X, size.Y) * 0.45f;

        // 1. Draw dark background circle with a neon-tinted background color
        DrawCircle(center, radius, new Color(0.025f, 0.02f, 0.08f, 0.88f));

        // 2. Draw circular border ring (glow style using neon cyan)
        Color ringColor = Color.FromHtml("#00f0ff");
        float startAngleRad = Mathf.DegToRad(135.0f);
        float endAngleRad = Mathf.DegToRad(405.0f);
        DrawArc(center, radius - 4.0f, startAngleRad, endAngleRad, 64, ringColor, 4.0f);

        // 3. Draw tick marks and values
        for (int s = 0; s <= 100; s += 10)
        {
            float t = s / 100.0f;
            float angleDeg = 135.0f + t * 270.0f;
            float angleRad = Mathf.DegToRad(angleDeg);
            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));

            bool isMajor = s % 20 == 0;
            float tickStart = radius - (isMajor ? 16.0f : 10.0f);
            float tickEnd = radius - 6.0f;

            Color tickColor = isMajor ? Color.FromHtml("#fcd34d") : Color.FromHtml("#efeff5");
            DrawLine(center + dir * tickStart, center + dir * tickEnd, tickColor, isMajor ? 2.5f : 1.5f);

            // Draw numbers for major ticks
            if (isMajor)
            {
                float textDist = radius - 30.0f;
                Vector2 textPos = center + dir * textDist;
                string valStr = s.ToString();
                Vector2 stringSize = _fontPixel.GetStringSize(valStr, HorizontalAlignment.Center, -1, 10);
                DrawString(_fontPixel, textPos + new Vector2(0, stringSize.Y * 0.35f), valStr, HorizontalAlignment.Center, -1, 10, Color.FromHtml("#fcd34d"));
            }
        }

        // 4. Draw digital display box framing the speed numbers (centered slightly above pivot)
        Vector2 boxPos = center + new Vector2(-40.0f, -42.0f);
        Vector2 boxSize = new Vector2(80.0f, 32.0f);
        DrawRect(new Rect2(boxPos, boxSize), new Color(0.0f, 0.0f, 0.0f, 0.5f), true);
        DrawRect(new Rect2(boxPos, boxSize), Color.FromHtml("#00f0ff"), false, 1.5f);

        // 5. Draw digital speed value inside the frame
        string speedStr = Mathf.RoundToInt(CurrentSpeed).ToString("000");
        DrawString(_fontBody, center + new Vector2(0, -18.0f), speedStr, HorizontalAlignment.Center, -1, 26, Colors.White);

        // 6. Draw speed unit text ("MPH") below the center pin
        string unitStr = "MPH";
        DrawString(_fontPixel, center + new Vector2(0, 36.0f), unitStr, HorizontalAlignment.Center, -1, 11, Color.FromHtml("#f5c451"));

        // 7. Draw tapered neon needle pointing to current speed
        float clampedSpeed = Mathf.Clamp(CurrentSpeed, 0.0f, MaxSpeed);
        float needleAngleDeg = 135.0f + (clampedSpeed / MaxSpeed) * 270.0f;
        float needleAngleRad = Mathf.DegToRad(needleAngleDeg);
        Vector2 needleDir = new Vector2(Mathf.Cos(needleAngleRad), Mathf.Sin(needleAngleRad));
        float needleLength = radius - 14.0f;

        Color needleColor = Color.FromHtml("#ff007f"); // neon pink/red
        Vector2 perp = new Vector2(-needleDir.Y, needleDir.X);
        Vector2 p1 = center + perp * 3.5f;
        Vector2 p2 = center - perp * 3.5f;
        Vector2 p3 = center + needleDir * needleLength;
        DrawPolygon(new Vector2[] { p1, p2, p3 }, new Color[] { needleColor, needleColor, needleColor });

        // 8. Center Pin (layered circles)
        DrawCircle(center, 9.0f, Color.FromHtml("#ff007f"));
        DrawCircle(center, 4.0f, Color.FromHtml("#fcd34d"));
    }
}
