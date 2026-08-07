using Godot;
using System;

/// <summary>
/// Endless Road — an offline endless-forward survival run. This mode is
/// intentionally isolated from TaxiMode so its rules, tuning, and failure
/// states can iterate independently.
/// </summary>
public partial class EndlessRoadMode : Node
{
    public enum RunState
    {
        Idle,
        Countdown,
        Running,
        ImpactRecovery,
        GameOver,
        Results
    }

    public static EndlessRoadMode Instance { get; private set; }

    public event Action<RunState, RunState> StateChanged;
    public event Action<float> DistanceChanged;
    public event Action<int> ScoreChanged;
    public event Action<int> MultiplierChanged;
    public event Action<float> HealthChanged;
    public event Action<float> BoostChanged;
    public event Action<int> SpeedChanged;

    [Export] public EndlessRoadSettings Settings = new();

    [ExportGroup("Runtime")]
    [Export] public float CountdownSeconds = 3.0f;

    public RunState State { get; private set; } = RunState.Idle;
    public float DistanceMeters { get; private set; }
    public int Score { get; private set; }
    public int Multiplier { get; private set; } = 1;
    public float Health { get; set; }
    public float Boost { get; set; }
    public float CurrentSpeedMps { get; private set; }
    public int CurrentSpeedDisplayMph { get; private set; }
    public float RunSeed { get; private set; }

    private float _stateTimer;
    private float _speed;
    private float _chainTimer;
    private bool _chainBrokenThisImpact;
    private float _invulnerabilityTimer;
    private int _lastReportedMultiplier = 1;
    private int _lastReportedScore;
    private float _lastReportedHealth = -1.0f;
    private float _lastReportedBoost = -1.0f;
    private int _lastReportedSpeedMph = -1;
    private bool _boostActive;

    public override void _EnterTree()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void StartRun(int? seedOverride = null)
    {
        ResetRun(seedOverride);
        State = RunState.Countdown;
        _stateTimer = CountdownSeconds;
        SetKartControlsEnabled(false);
        PublishStateChanged();
    }

    public void RestartRun()
    {
        StartRun(RunSeed > 0 ? (int)RunSeed : null);
    }

    public void ResetRun(int? seedOverride = null)
    {
        if (seedOverride.HasValue)
            RunSeed = seedOverride.Value;
        else if (RunSeed <= 0)
            RunSeed = (int)GD.RandRange(1, 2147483646);

        State = RunState.Idle;
        DistanceMeters = 0.0f;
        Score = 0;
        Multiplier = 1;
        Health = Settings.VehicleHealth;
        Boost = Settings.BoostMaxCharge;
        CurrentSpeedMps = Settings.OpeningSpeed;
        _speed = CurrentSpeedMps;
        _chainTimer = 0.0f;
        _chainBrokenThisImpact = false;
        _invulnerabilityTimer = 0.0f;
        _boostActive = false;
        _lastReportedMultiplier = -1;
        _lastReportedScore = -1;
        _lastReportedHealth = -1.0f;
        _lastReportedBoost = -1.0f;
        _lastReportedSpeedMph = -1;
        CurrentSpeedDisplayMph = -1;

        PublishRuntimeEvents();
        SetKartControlsEnabled(true);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        switch (State)
        {
            case RunState.Countdown:
                _stateTimer -= dt;
                if (_stateTimer <= 0.0f)
                {
                    State = RunState.Running;
                    PublishStateChanged();
                }
                break;

            case RunState.Running:
                UpdateRunning(dt);
                break;

            case RunState.ImpactRecovery:
                UpdateImpactRecovery(dt);
                break;

            case RunState.GameOver:
            case RunState.Idle:
            case RunState.Results:
            default:
                break;
        }
    }

    private void UpdateRunning(float dt)
    {
        float targetSpeed = Mathf.Clamp(
            Settings.OpeningSpeed + Settings.SpeedRampPerSecond * DistanceMeters,
            Settings.OpeningSpeed,
            Settings.MaxSpeed);

        float acceleration = 0.0f;
        if (_boostActive && Boost > 0.0f)
        {
            Boost = Mathf.Max(0.0f, Boost - Settings.BoostConsumePerSecond * dt);
            acceleration = targetSpeed * Settings.BoostAccelerationFactor * dt;
            targetSpeed *= Settings.BoostSpeedFactor;
            if (Boost <= 0.0f)
                _boostActive = false;
        }
        else
        {
            Boost = Mathf.Min(Settings.BoostMaxCharge, Boost + Settings.BoostRechargePerSecond * dt);
            _boostActive = false;
        }

        _speed = Mathf.MoveToward(_speed, targetSpeed, acceleration + targetSpeed * 0.35f * dt);
        CurrentSpeedMps = _speed;
        DistanceMeters += _speed * dt;

        _chainTimer = Mathf.Max(0.0f, _chainTimer - Settings.ChainDecayPerSecond * dt);
        if (_chainTimer <= 0.0f && Multiplier > 1)
        {
            Multiplier = 1;
            _chainBrokenThisImpact = false;
        }

        if (_invulnerabilityTimer > 0.0f)
            _invulnerabilityTimer = Mathf.Max(0.0f, _invulnerabilityTimer - dt);

        PublishRuntimeEvents();
    }

    private void UpdateImpactRecovery(float dt)
    {
        _stateTimer -= dt;
        if (_stateTimer <= 0.0f)
        {
            if (Health <= 0.0f)
            {
                State = RunState.GameOver;
                PublishStateChanged();
                return;
            }

            State = RunState.Running;
            _invulnerabilityTimer = Settings.InvulnerabilitySeconds;
            PublishStateChanged();
        }
    }

    public void ApplyImpact(Kart.ImpactSeverity severity)
    {
        if (State != RunState.Running || _invulnerabilityTimer > 0.0f)
            return;

        float damage = severity switch
        {
            Kart.ImpactSeverity.Glance => Settings.ImpactDamageGlance,
            Kart.ImpactSeverity.Bump => Settings.ImpactDamageBump,
            Kart.ImpactSeverity.Crash => Settings.ImpactDamageCrash,
            _ => Settings.ImpactDamageBump
        };

        Health = Mathf.Max(0.0f, Health - damage);
        _speed = Mathf.Max(Settings.OpeningSpeed * 0.5f, _speed * 0.45f);
        _invulnerabilityTimer = Settings.InvulnerabilitySeconds;
        _stateTimer = Mathf.Max(0.45f, severity == Kart.ImpactSeverity.Crash ? 1.2f : 0.7f);
        State = RunState.ImpactRecovery;
        _chainBrokenThisImpact = true;
        Multiplier = Mathf.Max(1, Multiplier - 2);
        _chainTimer = 0.0f;

        PublishRuntimeEvents();
        PublishStateChanged();
    }

    public void AddScore(int basePoints)
    {
        int awarded = basePoints * Multiplier;
        Score += awarded;
        Multiplier = Mathf.Min(Settings.MaxMultiplier, Multiplier + 1);
        _chainTimer = Settings.ChainWindowSeconds;
        _chainBrokenThisImpact = false;
        PublishRuntimeEvents();
    }

    public void ActivateBoost()
    {
        if (State != RunState.Running || Boost <= 0.0f || _boostActive)
            return;

        _boostActive = true;
    }

    public void UpdateStreamer(float playerZ)
    {
        EndlessRoadStreamer.Instance?.UpdateStream(playerZ);
    }

    private void SetKartControlsEnabled(bool enabled)
    {
        GameManager.Instance?.SetAllKartControlsEnabled(enabled);
    }

    private void PublishStateChanged()
    {
        StateChanged?.Invoke(State, State);
    }

    private void PublishRuntimeEvents()
    {
        int mph = Mathf.RoundToInt(CurrentSpeedMps * 2.23694f);
        DistanceChanged?.Invoke(DistanceMeters);

        if (Score != _lastReportedScore)
        {
            _lastReportedScore = Score;
            ScoreChanged?.Invoke(Score);
        }

        if (Multiplier != _lastReportedMultiplier)
        {
            _lastReportedMultiplier = Multiplier;
            MultiplierChanged?.Invoke(Multiplier);
        }

        if (!Mathf.IsEqualApprox(Health, _lastReportedHealth))
        {
            _lastReportedHealth = Health;
            HealthChanged?.Invoke(Health);
        }

        if (!Mathf.IsEqualApprox(Boost, _lastReportedBoost))
        {
            _lastReportedBoost = Boost;
            BoostChanged?.Invoke(Boost);
        }

        if (mph != _lastReportedSpeedMph)
        {
            _lastReportedSpeedMph = mph;
            SpeedChanged?.Invoke(mph);
            CurrentSpeedDisplayMph = mph;
        }
    }
}
