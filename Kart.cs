using Godot;
using System;

public partial class Kart : RigidBody3D
{
    public enum DriftPhase { None, Initiate, Holding }
    public enum ImpactSeverity { Glance, Bump, Crash }

    public readonly struct VehicleArchetype
    {
        public string Name { get; }
        public string Subtitle { get; }
        public float MaxSpeed { get; }
        public float Acceleration { get; }
        public float SteeringSpeed { get; }
        public float DriftChargeMultiplier { get; }
        public float PanicDampener { get; }

        public VehicleArchetype(string name, string subtitle, float maxSpeed, float acceleration, float steeringSpeed, float driftChargeMultiplier, float panicDampener)
        {
            Name = name;
            Subtitle = subtitle;
            MaxSpeed = maxSpeed;
            Acceleration = acceleration;
            SteeringSpeed = steeringSpeed;
            DriftChargeMultiplier = driftChargeMultiplier;
            PanicDampener = panicDampener;
        }
    }

    public static readonly VehicleArchetype[] VehicleArchetypes = new[]
    {
        new VehicleArchetype("NEON CAB", "BALANCED ALL-ROUNDER", 28.16f, 22.88f, 3.4f, 1.0f, 1.0f),
        new VehicleArchetype("CITY TAXI", "HEAVY TANK / LOW PANIC", 25.5f, 20.5f, 3.2f, 0.9f, 0.70f),
        new VehicleArchetype("SPORT SEDAN", "DRIFT SPECIALIST", 30.5f, 25.5f, 3.8f, 1.35f, 1.0f),
        new VehicleArchetype("FUTURE RACER", "HYPER SPEED ROCKET", 34.0f, 29.0f, 3.2f, 1.1f, 1.25f),
        new VehicleArchetype("LUXURY SUV", "VIP CRUISER / HEAVY", 26.0f, 22.0f, 3.0f, 0.85f, 0.65f),
    };

    private static readonly string[] VehicleNames = { "NEON CAB", "CITY TAXI", "SPORT SEDAN", "FUTURE RACER", "LUXURY SUV" };
    private static readonly string[] VehiclePaths =
    {
        "",
        "res://assets/kenny_car-kit/taxi.glb",
        "res://assets/kenny_car-kit/sedan-sports.glb",
        "res://assets/kenny_car-kit/race-future.glb",
        "res://assets/kenny_car-kit/suv-luxury.glb"
    };
    private static readonly PackedScene[] VehicleSceneCache = new PackedScene[VehiclePaths.Length];
    private static Font _cachedSpeechFont;
    [ExportGroup("Input")]
    [Export] public int OwnerPeerId { get; set; } = 1;
    [Export] public bool UseLocalInput { get; set; } = true;
    [Export] public bool IsLocalPlayer { get; set; } = false;
    [Export] public bool IsAI { get; set; } = false;
    [Export] public float TapInputHoldTime = 0.16f;

    public bool ControlsEnabled { get; private set; } = true;

    /// <summary>
    /// True while this kart's own controls ask for brakes. AI and remote karts drive
    /// their lamps and skid audio from this instead of the local player's keyboard.
    /// </summary>
    public bool BrakeInputActive { get; private set; }

    [ExportGroup("Driving")]
    [Export] public float Acceleration = 22.88f;
    [Export] public float ReverseAcceleration = 14.08f;
    [Export] public float BrakeForce = 56.0f;
    [Export] public float MaxForwardSpeed = 28.16f;
    [Export] public float MaxReverseSpeed = 8.8f;
    [Export] public float SpeedLimitApproachRange = 5.0f;
    [Export] public float AISpeedScale = 0.85f;
    [Export] public float SteeringSpeed = 3.4f;
    [Export] public float MinSteeringSpeed = 1.4f;
    [Export] public float HighSpeedSteeringRetention = 0.72f;
    [Export] public float SteeringResponse = 11.0f;
    [Export] public float SteeringRecentering = 15.0f;
    [Export] public float SideGrip = 11.0f;
    [Export] public float MaxLateralGripAcceleration = 32.0f;
    [Export] public float RollingDrag = 0.16f;
    [Export] public float ExtraGravity = 46.0f;
    [Export] public float GroundAdhesion = 28.0f;
    [Export] public float GroundContactDistance = 0.72f;
    [Export] public float GroundGraceTime = 0.08f;
    [Export] public float GroundNormalSmoothing = 16.0f;

    [ExportGroup("Drift")]
    [Export] public float MinDriftSpeed = 10.0f;
    [Export] public float DriftSteeringThreshold = 0.35f;
    [Export] public float DriftDuration = 0.9f;
    [Export] public float DriftGripMultiplier = 0.32f;
    [Export] public float DriftSteeringBoost = 1.32f;
    [Export] public float DriftSustainLateralSpeed = 1.8f;
    [Export] public float DriftMaxChargeTime = 1.8f;
    [Export] public float DriftExitAcceleration = 5.5f;

    [ExportGroup("Visuals")]
    [Export] public float VisualRotationSpeed = 12.0f;
    [Export] public float VisualSteerLeanDegrees = 6.0f;
    [Export] public float NetworkSmoothingSpeed = 24.0f;

    public float DriftAmount { get; private set; }
    public DriftPhase CurrentDriftPhase { get; private set; }
    public float DriftCharge { get; private set; }
    public int PendingStyleTip { get; private set; }
    public int VehicleOption { get; private set; }
    public int VehicleOptionCount => VehicleCount;

    public static int VehicleCount => VehicleNames.Length;
    public static string GetVehicleName(int option) => VehicleNames[Mathf.PosMod(option, VehicleNames.Length)];
    public string VehicleName => GetVehicleName(VehicleOption);
    public Vector3 NetworkTargetPosition => _netTargetPosition;

    private RayCast3D[] _groundRays;
    private Node3D _visualContainer;
    private Node _vehicleOptionVisual;

    // Expose speed to the UI (converting m/s to km/h)
    public float CurrentSpeedKmh => LinearVelocity.Length() * 3.6f;

    private float _forwardInput;
    private float _steeringInput;
    private bool _handbrakeInput;
    private bool _brakeHeld;
    private float _forwardTapInput;
    private float _steeringTapInput;
    private float _forwardTapTimer;
    private float _steeringTapTimer;
    private bool _isGrounded;
    private Vector3 _groundNormal = Vector3.Up;
    private float _groundGraceTimer;
    private float _driftTimer;
    private bool _driftWasHeld;
    private readonly System.Collections.Generic.Dictionary<ulong, ulong> _collisionCooldowns = new();
    private const ulong CollisionCooldownMs = 450;
    private const ulong CollisionCooldownSweepMs = 5000;
    private ulong _lastCollisionCooldownSweepMs;

    private Vector3 _netTargetPosition;
    private Vector3 _netTargetRotation;
    private bool _hasNetTarget = false;
    private int _nextInputSequence;
    private int _lastAcceptedInputSequence = -1;
    private int _lastNetworkSnapshotSequence = -1;
    private int _nextFireSequence;
    private int _lastAcceptedFireSequence = -1;
    private int _shotSequence;
    private ulong _lastShotMs;
    private ulong _lastValidInputAtMs;
    private ulong _lastRejectedInputWarningMs;

    private const ulong InputTimeoutMs = 250;
    private const ulong FireIntervalMs = 120;
    private const ulong RejectedInputWarningIntervalMs = 1000;
    private const float NetworkSnapDistance = 8.0f;

    // Tactical Taxi variables
    public GameManager.CustomerData? ActivePassenger { get; set; }
    public float PanicMeter { get; private set; } = 0.0f;
    public GameManager.FarePayoutBreakdown LastFarePayout { get; private set; }
    public ulong LastFarePayoutMs { get; private set; }
    public float BoardingProgress { get; private set; } = 0.0f;
    private float _airtimeAccumulator = 0.0f;
    private int _lastBoardingAudioStep = -1;
    private ulong _lastCollisionAudioMs;
    private ulong _lastPanicWarningMs;
    private Node3D _passengerCabinVisual;

    private const float InputDeadzone = 0.05f;

    public override void _Ready()
    {
        _groundRays = new[]
        {
            GetNode<RayCast3D>("GroundRay"),
            GetNode<RayCast3D>("GroundRayFrontLeft"),
            GetNode<RayCast3D>("GroundRayFrontRight"),
            GetNode<RayCast3D>("GroundRayRearLeft"),
            GetNode<RayCast3D>("GroundRayRearRight")
        };
        _visualContainer = GetNode<Node3D>("VisualContainer");

        // Keep the chassis upright while allowing physics-safe yaw steering.
        AxisLockAngularX = true;
        AxisLockAngularY = false;
        AxisLockAngularZ = true;

        // Server is always the authority for physics; OwnerPeerId only identifies who may send input.
        SetMultiplayerAuthority(1);

        ContactMonitor = true;
        MaxContactsReported = 4;
        BodyEntered += OnBodyCollision;

        EnsureLocalPlayerFeatures();
        ApplyVehicleStats();
        ApplyVehicleVisual();
    }

    public override void _Process(double delta)
    {
        UpdateTapTimers((float)delta);

        if (ShouldRunPhysics() == false && _hasNetTarget)
        {
            float networkBlend = 1.0f - Mathf.Exp(-NetworkSmoothingSpeed * (float)delta);
            GlobalPosition = GlobalPosition.Lerp(_netTargetPosition, networkBlend);
            Rotation = new Vector3(
                Mathf.LerpAngle(Rotation.X, _netTargetRotation.X, networkBlend),
                Mathf.LerpAngle(Rotation.Y, _netTargetRotation.Y, networkBlend),
                Mathf.LerpAngle(Rotation.Z, _netTargetRotation.Z, networkBlend)
            );
        }

        if (_visualContainer == null) return;

        _visualContainer.GlobalPosition = GlobalPosition;
        if (ShouldRunPhysics() == false)
        {
            UpdateGroundContact((float)delta);
        }

        AlignVisualsWithGround((float)delta);

        if (IsLocalPlayer && ActivePassenger.HasValue && PanicMeter >= 75.0f)
        {
            ulong now = Time.GetTicksMsec();
            if (now - _lastPanicWarningMs >= 3000)
            {
                _lastPanicWarningMs = now;
                AudioManager.Instance?.PlayLocal(AudioManager.Cue.Warning, -4.0f, 0.96f + PanicMeter / 500.0f);
            }
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!ControlsEnabled || IsAI || (UseLocalInput == false && IsLocalPlayer == false && IsOffline() == false)) return;

        if (@event.IsActionPressed("move_forward"))
            BufferTapInput(1.0f, 0.0f);
        else if (@event.IsActionPressed("move_backward"))
            BufferTapInput(-1.0f, 0.0f);
        else if (@event.IsActionPressed("move_right"))
            BufferTapInput(0.0f, 1.0f);
        else if (@event.IsActionPressed("move_left"))
            BufferTapInput(0.0f, -1.0f);
        // fire_weapon is registered by InputBindings, so the raw F/E fallback is no longer
        // needed and no longer overrides a rebound key.
        else if (@event.IsActionPressed("fire_weapon"))
            FireWeapon();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SendInputRpc(int sequence, float forward, float steer, bool handbrake)
    {
        if (!Multiplayer.IsServer())
            return;

        int senderId = Multiplayer.GetRemoteSenderId();
        if (senderId != OwnerPeerId)
        {
            WarnRejectedInput($"Rejected kart input from peer {senderId}; kart belongs to peer {OwnerPeerId}.");
            return;
        }

        if (sequence <= _lastAcceptedInputSequence)
        {
            WarnRejectedInput($"Rejected stale kart input sequence {sequence} from peer {senderId}.");
            return;
        }

        _lastAcceptedInputSequence = sequence;
        _lastValidInputAtMs = Time.GetTicksMsec();
        if (ControlsEnabled)
        {
            _forwardInput = Mathf.Clamp(forward, -1.0f, 1.0f);
            _steeringInput = Mathf.Clamp(steer, -1.0f, 1.0f);
            _handbrakeInput = handbrake;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // A connected client has a frozen local replica, but it must sample and
        // submit input before the client-only physics early return.
        if (ControlsEnabled && UseLocalInput && !IsAI)
        {
            CaptureLocalInput();
            if (IsConnectedClient())
                RpcId(1, nameof(SendInputRpc), ++_nextInputSequence, _forwardInput, _steeringInput, _handbrakeInput);
        }

        if (ShouldRunPhysics() == false) return;

        if (ControlsEnabled && !IsAI && (UseLocalInput || IsOffline()))
        {
            CaptureLocalInput();
        }
        else if (!ControlsEnabled)
        {
            _forwardInput = 0.0f;
            _steeringInput = 0.0f;
            _handbrakeInput = false;
        }

        BrakeInputActive = ControlsEnabled && (_brakeHeld || _forwardInput < -InputDeadzone);
        PruneCollisionCooldowns();

        if (Multiplayer.IsServer() && !IsAI && OwnerPeerId != Multiplayer.GetUniqueId() &&
            Time.GetTicksMsec() - _lastValidInputAtMs > InputTimeoutMs)
        {
            ClearInput();
        }

        float dt = (float)delta;
        UpdateGroundContact(dt);

        if (Multiplayer.IsServer() || IsOffline())
        {
            UpdatePassengerPanic(dt);
        }

        if (_isGrounded == false)
        {
            ApplyCentralForce(Vector3.Down * ExtraGravity * Mass);
            _driftTimer = Mathf.Max(0.0f, _driftTimer - dt);
            DriftAmount = Mathf.MoveToward(DriftAmount, 0.0f, dt * 2.5f);
            AngularVelocity = new Vector3(0.0f, Mathf.MoveToward(AngularVelocity.Y, 0.0f, dt * 3.0f), 0.0f);
            return;
        }

        Vector3 groundNormal = _groundNormal;
        Vector3 forwardDirection = GetForwardDirection(groundNormal);
        Vector3 sideDirection = groundNormal.Cross(forwardDirection).Normalized();

        Vector3 planarVelocity = LinearVelocity - groundNormal * LinearVelocity.Dot(groundNormal);
        float forwardSpeed = planarVelocity.Dot(forwardDirection);
        float lateralSpeed = planarVelocity.Dot(sideDirection);
        float planarSpeed = planarVelocity.Length();

        bool braking = Mathf.Abs(_forwardInput) > InputDeadzone &&
            Mathf.Abs(forwardSpeed) > 1.0f &&
            Mathf.Sign(_forwardInput) != Mathf.Sign(forwardSpeed);
        bool driftTriggered = _handbrakeInput &&
            forwardSpeed > MinDriftSpeed &&
            Mathf.Abs(_steeringInput) >= DriftSteeringThreshold;

        bool drifting = driftTriggered && Mathf.Abs(lateralSpeed) >= DriftSustainLateralSpeed;
        UpdateDriftState(drifting, dt, forwardDirection);

        DriftAmount = Mathf.MoveToward(DriftAmount, drifting ? 1.0f : 0.0f, dt * (drifting ? 7.0f : 3.5f));
        float gripScale = Mathf.Lerp(1.0f, DriftGripMultiplier, DriftAmount);

        float lateralGripAcceleration = Mathf.Clamp(
            -lateralSpeed * SideGrip * gripScale,
            -MaxLateralGripAcceleration * gripScale,
            MaxLateralGripAcceleration * gripScale
        );
        ApplyCentralForce(sideDirection * lateralGripAcceleration * Mass);
        ApplyCentralForce(-forwardDirection * forwardSpeed * RollingDrag * Mass);
        ApplyCentralForce(-groundNormal * GroundAdhesion * Mass);

        if (_handbrakeInput && !drifting)
        {
            float hardStopForce = 45.0f; // Massive drag for instant stopping
            ApplyCentralForce(-planarVelocity * hardStopForce * Mass);
        }

        if (Mathf.Abs(_forwardInput) > InputDeadzone)
        {
            float speedLimit = _forwardInput > 0.0f ? MaxForwardSpeed : MaxReverseSpeed;
            float driveAcceleration = braking ? BrakeForce : (_forwardInput > 0.0f ? Acceleration : ReverseAcceleration);

            if (IsAI)
            {
                speedLimit *= AISpeedScale;
                driveAcceleration *= AISpeedScale;
            }

            float speedInInputDirection = forwardSpeed * Mathf.Sign(_forwardInput);
            float speedLimitFactor = braking
                ? 1.0f
                : Mathf.Clamp((speedLimit - speedInInputDirection) / Mathf.Max(0.01f, SpeedLimitApproachRange), 0.0f, 1.0f);

            if (braking || speedLimitFactor > 0.0f)
            {
                ApplyCentralForce(forwardDirection * _forwardInput * driveAcceleration * speedLimitFactor * Mass);
            }
        }

        if (Mathf.Abs(_steeringInput) > InputDeadzone && planarSpeed > 0.1f)
        {
            float steeringAuthority = Mathf.Clamp(planarSpeed / MinSteeringSpeed, 0.0f, 1.0f);
            float speedRatio = Mathf.Clamp(planarSpeed / MaxForwardSpeed, 0.0f, 1.0f);
            float highSpeedFade = Mathf.Lerp(1.0f, HighSpeedSteeringRetention, speedRatio);
            bool reversing = forwardSpeed < -0.5f ||
                (Mathf.Abs(forwardSpeed) <= 0.5f && _forwardInput < -InputDeadzone);
            float reverseMultiplier = reversing ? -1.0f : 1.0f;

            float driftSteering = Mathf.Lerp(1.0f, DriftSteeringBoost, DriftAmount);
            float targetYawVelocity = -_steeringInput * reverseMultiplier * SteeringSpeed * steeringAuthority * highSpeedFade * driftSteering;
            AngularVelocity = new Vector3(
                0.0f,
                Mathf.MoveToward(AngularVelocity.Y, targetYawVelocity, SteeringResponse * dt),
                0.0f
            );
        }
        else
        {
            AngularVelocity = new Vector3(
                0.0f,
                Mathf.MoveToward(AngularVelocity.Y, 0.0f, SteeringRecentering * dt),
                0.0f
            );
        }
    }

    public void ApplyNetworkSnapshot(int sequence, Vector3 position, Vector3 rotation, Vector3 velocity)
    {
        if (ShouldRunPhysics())
            return;

        if (sequence <= _lastNetworkSnapshotSequence)
            return;

        _lastNetworkSnapshotSequence = sequence;

        if (!_hasNetTarget || GlobalPosition.DistanceTo(position) > NetworkSnapDistance)
        {
            GlobalPosition = position;
            Rotation = rotation;
            _netTargetPosition = position;
            _netTargetRotation = rotation;
            _hasNetTarget = true;
        }
        else
        {
            _netTargetPosition = position;
            _netTargetRotation = rotation;
        }

        LinearVelocity = velocity;
    }

    public void ResetNetworkReplicationState()
    {
        _nextInputSequence = 0;
        _lastAcceptedInputSequence = -1;
        _lastNetworkSnapshotSequence = -1;
        _lastValidInputAtMs = Time.GetTicksMsec();
        _hasNetTarget = false;
    }

    private void WarnRejectedInput(string message)
    {
        ulong now = Time.GetTicksMsec();
        if (now - _lastRejectedInputWarningMs < RejectedInputWarningIntervalMs)
            return;

        _lastRejectedInputWarningMs = now;
        GD.PushWarning(message);
    }

    public void SetAIInput(float forward, float steer, bool handbrake = false)
    {
        if (!IsAI || !ControlsEnabled) return;

        if (Multiplayer.IsServer())
        {
            _forwardInput = Mathf.Clamp(forward, -1.0f, 1.0f);
            _steeringInput = Mathf.Clamp(steer, -1.0f, 1.0f);
            _handbrakeInput = handbrake;
        }
    }

    public void SetControlsEnabled(bool enabled)
    {
        ControlsEnabled = enabled;
        if (!enabled)
        {
            _forwardInput = 0.0f;
            _steeringInput = 0.0f;
            _forwardTapTimer = 0.0f;
            _steeringTapTimer = 0.0f;
            _brakeHeld = false;
            BrakeInputActive = false;
        }
    }

    public bool GetControlsEnabled() => ControlsEnabled;

    private void PruneCollisionCooldowns()
    {
        // Streamed traffic is freed constantly, so the map would keep one entry per body
        // ever hit. Entries past the cooldown window can never suppress another impact.
        if (_collisionCooldowns.Count == 0)
            return;

        ulong now = Time.GetTicksMsec();
        if (now - _lastCollisionCooldownSweepMs < CollisionCooldownSweepMs)
            return;

        _lastCollisionCooldownSweepMs = now;
        var stale = new System.Collections.Generic.List<ulong>();
        foreach (var entry in _collisionCooldowns)
        {
            if (now - entry.Value >= CollisionCooldownMs)
                stale.Add(entry.Key);
        }

        for (int index = 0; index < stale.Count; index++)
            _collisionCooldowns.Remove(stale[index]);
    }

    public void ClearInput()
    {
        if (Multiplayer.IsServer() || UseLocalInput)
        {
            _forwardInput = 0.0f;
            _steeringInput = 0.0f;
            _handbrakeInput = false;
        }
    }

    private void UpdateDriftState(bool drifting, float dt, Vector3 forwardDirection)
    {
        if (drifting)
        {
            if (CurrentDriftPhase == DriftPhase.None)
            {
                CurrentDriftPhase = DriftPhase.Initiate;
                DriftCharge = 0.0f;
                BroadcastAudioCue(AudioManager.Cue.CollisionLight, -14.0f, 1.35f);
            }

            float chargeRate = GetCurrentArchetype().DriftChargeMultiplier;
            DriftCharge = Mathf.Min(DriftMaxChargeTime, DriftCharge + dt * chargeRate);
            if (DriftCharge >= 0.4f)
                CurrentDriftPhase = DriftPhase.Holding;
            _driftTimer = DriftDuration;
        }
        else if (_driftWasHeld && CurrentDriftPhase != DriftPhase.None)
        {
            if (CurrentDriftPhase == DriftPhase.Holding)
            {
                int earnedTip = Mathf.RoundToInt(Mathf.Clamp((DriftCharge - 0.4f) / (DriftMaxChargeTime - 0.4f), 0.0f, 1.0f) * 45.0f);
                PendingStyleTip += earnedTip;
                ApplyCentralImpulse(forwardDirection * (DriftExitAcceleration * Mass));
                TriggerSpeechBubble(earnedTip > 0 ? $"SMOOTH! +${earnedTip}" : "NICE TURN!");
            }
            CurrentDriftPhase = DriftPhase.None;
            DriftCharge = 0.0f;
            _driftTimer = 0.0f;
        }
        _driftWasHeld = drifting;
    }

    public void CancelDriftReward()
    {
        CurrentDriftPhase = DriftPhase.None;
        DriftCharge = 0.0f;
        PendingStyleTip = 0;
        _driftTimer = 0.0f;
        _driftWasHeld = false;
    }

    public int ConsumeStyleTip()
    {
        int tip = PendingStyleTip;
        PendingStyleTip = 0;
        return tip;
    }

    public void SetLastFarePayout(GameManager.FarePayoutBreakdown breakdown)
    {
        LastFarePayout = breakdown;
        LastFarePayoutMs = Time.GetTicksMsec();
    }

    public void EnsureLocalPlayerFeatures()
    {
        if (!IsLocalPlayer || GetNodeOrNull<CompassArrow>("CompassArrow") != null)
            return;

        AddChild(new CompassArrow { Name = "CompassArrow" });
    }

    public void ConfigureIdentityGlow(Color color)
    {
        Node3D existing = GetNodeOrNull<Node3D>("IdentityGlow");
        if (existing != null)
            return;

        var glowRoot = new Node3D { Name = "IdentityGlow", Position = new Vector3(0.0f, -0.38f, 0.0f) };
        var glowMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(color.R, color.G, color.B, 0.62f),
            EmissionEnabled = true,
            Emission = color * 0.85f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.25f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        glowRoot.AddChild(new MeshInstance3D
        {
            Name = "IdentityRing",
            Mesh = new TorusMesh { InnerRadius = 0.72f, OuterRadius = 1.14f, Rings = 8, RingSegments = 24 },
            MaterialOverride = glowMaterial
        });
        glowRoot.AddChild(new OmniLight3D
        {
            Name = "GlowLight",
            LightColor = color,
            LightEnergy = 0.5f,
            OmniRange = 4.8f,
            ShadowEnabled = false,
            Position = Vector3.Up * 0.28f
        });
        AddChild(glowRoot);
    }

    public void ResetForRun(Transform3D spawnTransform)
    {
        GlobalTransform = spawnTransform;
        LinearVelocity = Vector3.Zero;
        AngularVelocity = Vector3.Zero;
        Sleeping = false;
        _forwardInput = 0.0f;
        _steeringInput = 0.0f;
        _handbrakeInput = false;
        BoardingProgress = 0.0f;
        ActivePassenger = null;
        PanicMeter = 0.0f;
        _airtimeAccumulator = 0.0f;
        _groundGraceTimer = 0.0f;
        _driftTimer = 0.0f;
        DriftAmount = 0.0f;
        _lastBoardingAudioStep = -1;
        _lastPanicWarningMs = 0;
    }

    private void CaptureLocalInput()
    {
        // Endless Road: always drive forward; steering/drift/boost stay live.
        if (EndlessRoadMode.Instance != null && EndlessRoadMode.Instance.State == EndlessRoadMode.RunState.Running)
        {
            float steeringAxis = Input.GetAxis("move_left", "move_right");
            _forwardInput = 1.0f;
            _steeringInput = Mathf.Abs(steeringAxis) > InputDeadzone ? steeringAxis : (_steeringTapTimer > 0.0f ? _steeringTapInput : 0.0f);
            _handbrakeInput = Input.IsActionPressed("drift");
            if (Input.IsActionJustPressed("boost") || Input.IsActionPressed("boost"))
                EndlessRoadMode.Instance?.ActivateBoost();
            // Allow light braking to scrub speed without reversing the endless run.
            _brakeHeld = Input.IsActionPressed("move_backward");
            if (_brakeHeld)
                _forwardInput = Mathf.Clamp(_forwardInput - 0.55f, 0.35f, 1.0f);
            _forwardInput = Mathf.Clamp(_forwardInput, -1.0f, 1.0f);
            _steeringInput = Mathf.Clamp(_steeringInput, -1.0f, 1.0f);
            return;
        }

        float forwardAxis = Input.GetAxis("move_backward", "move_forward");
        float steeringAxis2 = Input.GetAxis("move_left", "move_right");

        _forwardInput = Mathf.Abs(forwardAxis) > InputDeadzone ? forwardAxis : (_forwardTapTimer > 0.0f ? _forwardTapInput : 0.0f);
        _steeringInput = Mathf.Abs(steeringAxis2) > InputDeadzone ? steeringAxis2 : (_steeringTapTimer > 0.0f ? _steeringTapInput : 0.0f);

        _handbrakeInput = Input.IsActionPressed("drift");
        if (Input.IsActionJustPressed("boost") || Input.IsActionPressed("boost"))
        {
            if (EndlessRoadMode.Instance != null && EndlessRoadMode.Instance.State == EndlessRoadMode.RunState.Running)
                EndlessRoadMode.Instance.ActivateBoost();
            else if (GameManager.Instance != null && GameManager.Instance.TryUseNitrousCharge(OwnerPeerId))
            {
                ApplyCentralImpulse(GetForwardDirection(_groundNormal) * (34.0f * Mass));
                AudioManager.Instance?.PlayLocal(AudioManager.Cue.CountdownGo, 1.5f, 1.4f);
                TriggerSpeechBubble(">> NITROUS BOOST <<");
                if (GetViewport()?.GetCamera3D() is TrackCamera cam)
                    cam.AddTrauma(0.42f);
            }
        }

        _forwardInput = Mathf.Clamp(_forwardInput, -1.0f, 1.0f);
        _steeringInput = Mathf.Clamp(_steeringInput, -1.0f, 1.0f);
        _brakeHeld = _forwardInput < -InputDeadzone;
    }

    private void BufferTapInput(float forward, float steering)
    {
        if (Mathf.Abs(forward) > InputDeadzone)
        {
            _forwardTapInput = forward;
            _forwardTapTimer = TapInputHoldTime;
        }

        if (Mathf.Abs(steering) > InputDeadzone)
        {
            _steeringTapInput = steering;
            _steeringTapTimer = TapInputHoldTime;
        }
    }

    private void UpdateTapTimers(float delta)
    {
        _forwardTapTimer = Mathf.Max(0.0f, _forwardTapTimer - delta);
        _steeringTapTimer = Mathf.Max(0.0f, _steeringTapTimer - delta);
    }

    private bool ShouldRunPhysics()
    {
        return IsOffline() || Multiplayer.IsServer();
    }

    private bool IsConnectedClient()
    {
        return IsOffline() == false && Multiplayer.IsServer() == false;
    }

    private bool IsOffline()
    {
        return Multiplayer.HasMultiplayerPeer() == false || Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer;
    }

    private void AlignVisualsWithGround(float delta)
    {
        if (_isGrounded)
        {
            Vector3 groundNormal = _groundNormal;
            Vector3 visualForward = GetForwardDirection(groundNormal);
            Vector3 visualRight = groundNormal.Cross(visualForward).Normalized();

            Basis targetBasis = new Basis(visualRight, groundNormal, visualForward).Orthonormalized();
            Vector3 planarVelocity = LinearVelocity - groundNormal * LinearVelocity.Dot(groundNormal);
            float speedRatio = Mathf.Clamp(planarVelocity.Length() / Mathf.Max(1.0f, MaxForwardSpeed), 0.0f, 1.0f);
            
            // Lateral body lean
            float lean = Mathf.DegToRad(-_steeringInput * VisualSteerLeanDegrees * speedRatio * Mathf.Lerp(0.45f, 1.0f, DriftAmount));
            targetBasis = targetBasis.Rotated(targetBasis.Z.Normalized(), lean).Orthonormalized();

            // Dynamic suspension pitch: squat on acceleration, dive on braking
            float pitchAngle = 0.0f;
            if (_forwardInput > 0.05f)
                pitchAngle = -Mathf.DegToRad(Mathf.Clamp(_forwardInput * 2.4f * (1.0f - speedRatio * 0.35f), 0.0f, 2.8f));
            else if (_forwardInput < -0.05f && planarVelocity.Length() > 1.0f)
                pitchAngle = Mathf.DegToRad(Mathf.Clamp(3.2f * speedRatio, 0.0f, 3.6f));

            if (Mathf.Abs(pitchAngle) > 0.0001f)
                targetBasis = targetBasis.Rotated(targetBasis.X.Normalized(), pitchAngle).Orthonormalized();

            Basis currentBasis = _visualContainer.GlobalTransform.Basis.Orthonormalized();
            float blend = 1.0f - Mathf.Exp(-VisualRotationSpeed * delta);

            _visualContainer.GlobalTransform = new Transform3D(
                currentBasis.Slerp(targetBasis, blend),
                GlobalPosition
            );
        }
    }

    private Vector3 GetForwardDirection(Vector3 groundNormal)
    {
        Vector3 forward = GlobalTransform.Basis.Z;
        forward -= groundNormal * forward.Dot(groundNormal);

        if (forward.LengthSquared() < 0.0001f)
        {
            forward = Vector3.Back - groundNormal * Vector3.Back.Dot(groundNormal);
        }

        return forward.Normalized();
    }

    private void UpdateGroundContact(float delta)
    {
        using var perf = PerfProbe.Measure(PerfHotspot.KartGroundRaycasts);
        Vector3 normalSum = Vector3.Zero;
        int contactCount = 0;
        bool centerContact = false;

        for (int i = 0; i < _groundRays.Length; i++)
        {
            RayCast3D ray = _groundRays[i];
            ray.ForceRaycastUpdate();
            PerfProbe.Count(PerfEvent.GroundRaycastQuery);
            if (!ray.IsColliding())
                continue;

            float hitDistance = ray.GlobalPosition.DistanceTo(ray.GetCollisionPoint());
            if (hitDistance > GroundContactDistance)
                continue;

            normalSum += ray.GetCollisionNormal();
            contactCount++;
            centerContact |= i == 0;
        }

        bool hasStableContact = contactCount >= 2 || centerContact;
        if (hasStableContact)
        {
            _groundGraceTimer = GroundGraceTime;
            Vector3 targetNormal = (normalSum / contactCount).Normalized();
            float normalBlend = 1.0f - Mathf.Exp(-GroundNormalSmoothing * delta);
            _groundNormal = _groundNormal.Slerp(targetNormal, normalBlend).Normalized();
        }
        else
        {
            _groundGraceTimer = Mathf.Max(0.0f, _groundGraceTimer - delta);
        }

        _isGrounded = hasStableContact || _groundGraceTimer > 0.0f;
    }

    // --- Tactical Taxi passenger methods ---

    private void UpdatePassengerPanic(float dt)
    {
        if (!ActivePassenger.HasValue) return;

        var passenger = ActivePassenger.Value;
        bool isVip = passenger.Archetype == GameManager.CustomerArchetype.VIP;
        bool isThrill = passenger.Archetype == GameManager.CustomerArchetype.ThrillSeeker;
        float speed = LinearVelocity.Length();

        if (!_isGrounded)
        {
            _airtimeAccumulator += dt;
            if (_airtimeAccumulator > (isThrill ? 0.8f : 0.4f))
            {
                float rate = isVip ? 45.0f : isThrill ? 10.0f : 25.0f;
                SetPanic(PanicMeter + rate * dt);
                if (GD.Randf() < dt * 1.5f)
                    TriggerSpeechBubble(isThrill ? "WOOOOO! AIRTIME!" : GetHumorousAirtimePhrase());
            }
        }
        else
        {
            _airtimeAccumulator = 0.0f;
            if (isThrill && (speed > 16.0f || DriftAmount > 0.3f))
            {
                SetPanic(PanicMeter - 20.0f * dt);
            }
            else if (speed > 2.0f)
            {
                SetPanic(PanicMeter - 5.0f * dt);
            }
            else
            {
                SetPanic(PanicMeter - 12.0f * dt);
            }
        }

        if (PanicMeter >= 100.0f)
        {
            TriggerSpeechBubble(isVip ? "TERRIBLE DRIVING! I'M SUING!" : "I'M OUTTA HERE!");
            if (TaxiMode.Instance != null && GameManager.Instance != null)
            {
                TaxiMode.Instance.ClearActiveFare(OwnerPeerId);
                GameManager.Instance.NotifyBailout(OwnerPeerId);
            }
            else
            {
                ClearPassenger();
            }
        }
    }

    /// <summary>
    /// Velocity of whatever was hit. Rigid bodies report their own, moving endless-road
    /// traffic reports its travel speed, and everything else counts as parked geometry.
    /// </summary>
    private static Vector3 BodyVelocity(Node body)
    {
        if (body is RigidBody3D rigidBody)
            return rigidBody.LinearVelocity;

        if (body is EndlessRoadTraffic traffic)
            return traffic.Velocity;

        return Vector3.Zero;
    }

    private void OnBodyCollision(Node body)
    {
        if (Multiplayer.HasMultiplayerPeer() && !Multiplayer.IsServer()) return;

        // Ignore harmless road/ground surfaces
        string name = body.Name.ToString();
        if (name.Length > 0)
        {
            char first = name[0];
            if (first == 'R')
            {
                if (name == "RoadMesh" || name == "RoadBody" || name.StartsWith("RoadSegment", StringComparison.Ordinal) || name.StartsWith("RightShoulder", StringComparison.Ordinal))
                    return;
            }
            else if (first == 'I')
            {
                if (name.StartsWith("Intersection", StringComparison.Ordinal))
                    return;
            }
            else if (first == 'L')
            {
                if (name.StartsWith("LaneMarker", StringComparison.Ordinal) || name.StartsWith("LeftShoulder", StringComparison.Ordinal))
                    return;
            }
            else if (first == 'C')
            {
                if (name.StartsWith("Crosswalk", StringComparison.Ordinal))
                    return;
            }
            else if (first == 'G')
            {
                if (name == "Ground")
                    return;
            }
        }

        // Endless Road: route impact through EndlessRoadMode and degrade to Burnout-style scoring elsewhere.
        if (EndlessRoadMode.Instance != null && EndlessRoadMode.Instance.State == EndlessRoadMode.RunState.Running)
        {
            // Don't let road-surface debris count as a collision — hazards use Area3D.
            if (body is StaticBody3D sb && (sb.Name == "RoadBody" || sb.Name.ToString().Contains("BarrierBody")))
            {
                // Barrier hit at speed — still counts, but via severity path below.
            }

            Vector3 otherVelocityER = BodyVelocity(body);
            Vector3 otherPositionER = body is Node3D body3DER ? body3DER.GlobalPosition : GlobalPosition - LinearVelocity;
            Vector3 normalER = GlobalPosition - otherPositionER;
            normalER = normalER.LengthSquared() > 0.001f ? normalER.Normalized() : -LinearVelocity.Normalized();
            float impactSpeedER = Mathf.Max(0.0f, (LinearVelocity - otherVelocityER).Dot(normalER));
            if (impactSpeedER > 2.5f)
            {
                ulong nowER = Time.GetTicksMsec();
                ulong keyER = body.GetInstanceId();
                if (_collisionCooldowns.TryGetValue(keyER, out ulong lastHitER) && nowER - lastHitER < CollisionCooldownMs)
                    return;
                _collisionCooldowns[keyER] = nowER;
                ImpactSeverity severityER = impactSpeedER >= 13.0f ? ImpactSeverity.Crash : impactSpeedER >= 6.0f ? ImpactSeverity.Bump : ImpactSeverity.Glance;
                if (nowER - _lastCollisionAudioMs >= 140)
                {
                    _lastCollisionAudioMs = nowER;
                    AudioManager.Cue cueER = severityER == ImpactSeverity.Crash ? AudioManager.Cue.CollisionHeavy : severityER == ImpactSeverity.Bump ? AudioManager.Cue.CollisionMedium : AudioManager.Cue.CollisionLight;
                    float volumeDbER = Mathf.Lerp(-10.0f, 1.0f, Mathf.Clamp((impactSpeedER - 2.5f) / 16.0f, 0.0f, 1.0f));
                    BroadcastAudioCue(cueER, volumeDbER, (float)GD.RandRange(0.92, 1.08));
                }
                // Camera shake scales with severity.
                var camER = GetViewport()?.GetCamera3D() as TrackCamera;
                if (camER != null)
                    camER.AddTrauma(severityER == ImpactSeverity.Crash ? 0.85f : severityER == ImpactSeverity.Bump ? 0.45f : 0.18f);
                EndlessRoadMode.Instance.ApplyImpact(severityER);
                CancelDriftReward();
            }
            return;
        }

        Vector3 otherVelocity = BodyVelocity(body);
        Vector3 otherPosition = body is Node3D body3D ? body3D.GlobalPosition : GlobalPosition - LinearVelocity;
        Vector3 normal = GlobalPosition - otherPosition;
        normal = normal.LengthSquared() > 0.001f ? normal.Normalized() : -LinearVelocity.Normalized();
        float impactSpeed = Mathf.Max(0.0f, (LinearVelocity - otherVelocity).Dot(normal));
        if (impactSpeed > 2.5f)
        {
            ulong now = Time.GetTicksMsec();
            ulong key = body.GetInstanceId();
            if (_collisionCooldowns.TryGetValue(key, out ulong lastHit) && now - lastHit < CollisionCooldownMs)
                return;
            _collisionCooldowns[key] = now;
            ImpactSeverity severity = impactSpeed >= 13.0f ? ImpactSeverity.Crash : impactSpeed >= 6.0f ? ImpactSeverity.Bump : ImpactSeverity.Glance;
            if (now - _lastCollisionAudioMs >= 140)
            {
                _lastCollisionAudioMs = now;
                AudioManager.Cue cue = severity == ImpactSeverity.Crash ? AudioManager.Cue.CollisionHeavy : severity == ImpactSeverity.Bump ? AudioManager.Cue.CollisionMedium : AudioManager.Cue.CollisionLight;
                float volumeDb = Mathf.Lerp(-10.0f, 1.0f, Mathf.Clamp((impactSpeed - 2.5f) / 16.0f, 0.0f, 1.0f));
                BroadcastAudioCue(cue, volumeDb, (float)GD.RandRange(0.92, 1.08));
            }

            if (ActivePassenger.HasValue)
            {
                if (severity != ImpactSeverity.Glance)
                {
                    bool isVip = ActivePassenger.Value.Archetype == GameManager.CustomerArchetype.VIP;
                    bool isThrill = ActivePassenger.Value.Archetype == GameManager.CustomerArchetype.ThrillSeeker;
                    bool hasArmor = GameManager.Instance != null && GameManager.Instance.HasArmorPlating(OwnerPeerId);

                    float panicScale = isVip ? 2.0f : isThrill ? 0.5f : 1.0f;
                    if (hasArmor) panicScale *= 0.75f;

                    float panicIncrease = (severity == ImpactSeverity.Crash ? impactSpeed * 2.0f : impactSpeed * 0.65f) * panicScale;
                    SetPanic(PanicMeter + panicIncrease);
                    TriggerSpeechBubble(isThrill ? "YEAH! RUBBIN' IS RACIN'!" : GetHumorousCollisionPhrase());
                    CancelDriftReward();
                }
            }

            if (GameManager.Instance != null && severity != ImpactSeverity.Glance)
            {
                GameManager.Instance.ApplyVehicleDamage(OwnerPeerId, Mathf.RoundToInt(impactSpeed * (severity == ImpactSeverity.Crash ? 1.1f : 0.25f)));
            }
        }
    }

    public void BoardPassenger(GameManager.CustomerData data)
    {
        BroadcastAudioCue(AudioManager.Cue.PassengerBoard, -2.0f, (float)GD.RandRange(0.97, 1.03));
        ActivePassenger = data;
        PanicMeter = 0.0f;
        CancelDriftReward();
        CreateCabinPassenger(data);
        if (Multiplayer.IsServer())
        {
            Rpc(nameof(SyncPassengerStateRpc), true, (int)data.Distance, (int)data.Wealth, data.MaxAcceptableDamage, data.GroupSize, data.LoadTime, 0.0f);
        }
    }

    public bool HasPassenger() => ActivePassenger.HasValue;

    public VehicleArchetype GetCurrentArchetype() => VehicleArchetypes[Mathf.Clamp(VehicleOption, 0, VehicleArchetypes.Length - 1)];
    public string GetVehicleSubtitle() => GetCurrentArchetype().Subtitle;

    public void ApplyVehicleStats()
    {
        var arch = GetCurrentArchetype();
        MaxForwardSpeed = arch.MaxSpeed;
        Acceleration = arch.Acceleration;
        SteeringSpeed = arch.SteeringSpeed;
    }

    public void SetVehicleOption(int option)
    {
        VehicleOption = Mathf.PosMod(option, VehicleNames.Length);
        ApplyVehicleStats();
        if (IsNodeReady())
            ApplyVehicleVisual();
    }

    public string GetVehicleName() => VehicleName;
    public int GetVehicleOptionCount() => VehicleOptionCount;

    private void ApplyVehicleVisual()
    {
        if (_visualContainer == null)
            return;

        Node3D defaultCab = _visualContainer.GetNodeOrNull<Node3D>("NeonCabVisual");
        if (defaultCab != null)
            defaultCab.Visible = VehicleOption == 0;

        if (_vehicleOptionVisual != null && GodotObject.IsInstanceValid(_vehicleOptionVisual))
        {
            _vehicleOptionVisual.QueueFree();
            _vehicleOptionVisual = null;
        }
        if (VehicleOption == 0)
            return;

        PackedScene scene = VehicleSceneCache[VehicleOption];
        if (scene == null)
        {
            scene = GD.Load<PackedScene>(VehiclePaths[VehicleOption]);
            VehicleSceneCache[VehicleOption] = scene;
        }

        if (scene == null)
        {
            GD.PushWarning($"Vehicle asset missing: {VehiclePaths[VehicleOption]}");
            if (defaultCab != null) defaultCab.Visible = true;
            return;
        }

        Node optionVisual = scene.Instantiate();
        optionVisual.Name = "VehicleOptionVisual";
        _visualContainer.AddChild(optionVisual);
        if (optionVisual is Node3D optionMesh)
        {
            // Kenney's vehicle kit is authored larger than this arcade kart chassis.
            optionMesh.Scale = Vector3.One * 0.9f;
            optionMesh.Position = new Vector3(0.0f, 0.06f, 0.0f);
            optionMesh.RotationDegrees = new Vector3(0.0f, 180.0f, 0.0f);
        }
        _vehicleOptionVisual = optionVisual;
    }

    public void ClearPassenger()
    {
        bool hadPassenger = ActivePassenger.HasValue;
        ActivePassenger = null;
        PanicMeter = 0.0f;
        if (hadPassenger)
            ReleaseCabinPassenger();
        if (Multiplayer.IsServer())
        {
            Rpc(nameof(SyncPassengerStateRpc), false, 0, 0, 0, 0, 0.0f, 0.0f);
        }
    }

    private void CreateCabinPassenger(GameManager.CustomerData data)
    {
        if (_passengerCabinVisual != null && GodotObject.IsInstanceValid(_passengerCabinVisual))
            _passengerCabinVisual.QueueFree();

        Color color = data.Wealth switch
        {
            GameManager.CustomerWealth.High => new Color(1.0f, 0.08f, 0.5f),
            GameManager.CustomerWealth.Medium => new Color(0.05f, 0.88f, 1.0f),
            _ => new Color(1.0f, 0.74f, 0.16f)
        };
        // Keep the silhouette inside the glass cabin; it becomes full-size only on exit.
        var passenger = new PassengerActor { Name = "PassengerInCab", Position = new Vector3(0.22f, 0.14f, -0.08f), Scale = new Vector3(0.28f, 0.28f, 0.28f) };
        _visualContainer.AddChild(passenger);
        passenger.Build(color, "FARE ON BOARD", 0.4f);
        _passengerCabinVisual = passenger;
    }

    private void ReleaseCabinPassenger()
    {
        if (_passengerCabinVisual == null || !GodotObject.IsInstanceValid(_passengerCabinVisual))
            return;

        _passengerCabinVisual.GetParent()?.RemoveChild(_passengerCabinVisual);
        GetParent()?.AddChild(_passengerCabinVisual);
        // Start at the curbside door rather than inside the taxi silhouette so the exit reads in chase view.
        _passengerCabinVisual.GlobalPosition = GlobalPosition + GlobalTransform.Basis.X * 1.28f + Vector3.Up * 0.06f;
        _passengerCabinVisual.Scale = Vector3.One;
        if (_passengerCabinVisual is PassengerActor passenger)
            passenger.ExitFrom(this);
        _passengerCabinVisual = null;
    }

    public void SetPanic(float panic)
    {
        if (panic > PanicMeter)
        {
            float delta = (panic - PanicMeter) * GetCurrentArchetype().PanicDampener;
            panic = PanicMeter + delta;
        }
        PanicMeter = Mathf.Clamp(panic, 0.0f, 100.0f);
        if (Multiplayer.IsServer())
        {
            Rpc(nameof(SyncPassengerPanicRpc), PanicMeter);
        }
    }

    public void SetBoardingProgress(float progress)
    {
        BoardingProgress = progress;
        UpdateBoardingAudio(progress);
        if (Multiplayer.IsServer())
        {
            Rpc(nameof(SyncBoardingProgressRpc), progress);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncPassengerStateRpc(bool hasPassenger, int distance, int wealth, int maxDmg, int groupSize, float loadTime, float panic)
    {
        if (hasPassenger)
        {
            ActivePassenger = new GameManager.CustomerData
            {
                Distance = (GameManager.CustomerDistance)distance,
                Wealth = (GameManager.CustomerWealth)wealth,
                MaxAcceptableDamage = maxDmg,
                GroupSize = groupSize,
                LoadTime = loadTime
            };
            PanicMeter = panic;
        }
        else
        {
            ActivePassenger = null;
            PanicMeter = 0.0f;
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SyncPassengerPanicRpc(float panic)
    {
        PanicMeter = panic;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SyncBoardingProgressRpc(float progress)
    {
        BoardingProgress = progress;
        UpdateBoardingAudio(progress);
    }

    public void PlayPickupEnterAudio()
    {
        if (IsLocalPlayer)
            AudioManager.Instance?.PlayLocal(AudioManager.Cue.PickupEnter, -2.0f);
    }

    public void BroadcastFareCompletedAudio()
    {
        BroadcastAudioCue(AudioManager.Cue.FareComplete, -1.0f);
    }

    public void BroadcastPassengerBailoutAudio()
    {
        BroadcastAudioCue(AudioManager.Cue.PassengerBailout, -1.0f);
    }

    public void BroadcastDestroyedAudio()
    {
        BroadcastAudioCue(AudioManager.Cue.Destroyed, 1.0f, (float)GD.RandRange(0.94, 1.02));
    }

    public void BroadcastRespawnAudio()
    {
        BroadcastAudioCue(AudioManager.Cue.Respawn, -1.0f);
    }

    private void UpdateBoardingAudio(float progress)
    {
        if (progress <= 0.01f)
        {
            _lastBoardingAudioStep = -1;
            return;
        }

        int step = Mathf.Clamp(Mathf.FloorToInt(progress * 4.0f), 0, 3);
        if (!IsLocalPlayer || step <= _lastBoardingAudioStep)
            return;

        _lastBoardingAudioStep = step;
        AudioManager.Instance?.PlayLocal(AudioManager.Cue.BoardingTick, -5.0f, 0.92f + step * 0.08f);
    }

    private void BroadcastAudioCue(AudioManager.Cue cue, float volumeDb = 0.0f, float pitchScale = 1.0f)
    {
        Vector3 position = GlobalPosition;
        if (Multiplayer.HasMultiplayerPeer())
        {
            Rpc(nameof(PlayKartAudioCueRpc), (int)cue, position, volumeDb, pitchScale);
            PerfProbe.Count(PerfEvent.AudioRpcSent);
        }
        else
            PlayKartAudioCueRpc((int)cue, position, volumeDb, pitchScale);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void PlayKartAudioCueRpc(int cueValue, Vector3 position, float volumeDb, float pitchScale)
    {
        AudioManager manager = AudioManager.Instance;
        if (manager == null)
            return;

        AudioManager.Cue cue = (AudioManager.Cue)cueValue;
        if (IsLocalPlayer && GetViewport().GetCamera3D() is TrackCamera camera)
        {
            float trauma = cue switch
            {
                AudioManager.Cue.CollisionHeavy => 0.7f,
                AudioManager.Cue.CollisionMedium => 0.42f,
                AudioManager.Cue.CollisionLight => 0.18f,
                AudioManager.Cue.Destroyed => 1.0f,
                _ => 0.0f
            };
            if (trauma > 0.0f)
                camera.AddTrauma(trauma);
        }

        switch (cue)
        {
            case AudioManager.Cue.FareComplete:
                if (IsLocalPlayer)
                {
                    manager.PlayLocal(AudioManager.Cue.Cash, -2.0f, (float)GD.RandRange(0.98, 1.04));
                    manager.PlayLocal(AudioManager.Cue.FareComplete, -1.0f, pitchScale);
                }
                break;
            case AudioManager.Cue.PassengerBailout:
            case AudioManager.Cue.Respawn:
                if (IsLocalPlayer)
                    manager.PlayLocal(cue, volumeDb, pitchScale);
                break;
            case AudioManager.Cue.Destroyed:
                manager.PlayWorld(AudioManager.Cue.Destroyed, position, volumeDb, pitchScale, 115.0f);
                manager.PlayWorld(AudioManager.Cue.Explosion, position, -2.0f, 0.82f, 125.0f);
                break;
            default:
                manager.PlayWorld(cue, position, volumeDb, pitchScale);
                break;
        }
    }

    public void TriggerSpeechBubble(string text)
    {
        if (Multiplayer.IsServer() || IsOffline())
        {
            if (Multiplayer.HasMultiplayerPeer())
                Rpc(nameof(SpawnSpeechBubbleRpc), text);
            else
                SpawnSpeechBubbleRpc(text);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnSpeechBubbleRpc(string text)
    {
        var label = new Label3D
        {
            Text = text,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.015f,
            Modulate = IsAI ? Colors.Tomato : Colors.HotPink,
            OutlineModulate = Colors.Black,
            Position = new Vector3(0, 2.5f, 0)
        };

        // Try loading neon font, fallback to default
        if (_cachedSpeechFont == null)
            _cachedSpeechFont = GD.Load<Font>("res://assets/fonts/VT323-Regular.ttf");

        if (_cachedSpeechFont != null)
        {
            label.Font = _cachedSpeechFont;
            label.FontSize = 48;
        }
        else
        {
            label.FontSize = 36;
        }

        AddChild(label);

        // Simple pop animation using Godot 4 tween
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position:y", 4.0f, 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "modulate:a", 0.0f, 1.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    private string GetHumorousCollisionPhrase()
    {
        string[] phrases = {
            "Watch the paint job!",
            "My neck!",
            "Are you blind?!",
            "Ouch! Watch where you're going!",
            "Is this a demolition derby?!",
            "Slow down, maniac!",
            "I'm going to sue you!"
        };
        return phrases[GD.RandRange(0, phrases.Length - 1)];
    }

    private string GetHumorousAirtimePhrase()
    {
        string[] phrases = {
            "AHHH! WE'RE FLYING!",
            "PUT ME DOWN!",
            "I DIDN'T SIGN UP FOR A FLIGHT!",
            "OH MY GOD!",
            "GRAVITY! DO YOU KNOW IT?!",
            "WE'RE GONNA CRASH!"
        };
        return phrases[GD.RandRange(0, phrases.Length - 1)];
    }

    public void FireWeapon()
    {
        if (!ControlsEnabled || (UseLocalInput == false && IsLocalPlayer == false && IsOffline() == false))
            return;

        // A client only signals intent. Ammo, damage, and projectile spawning belong to the
        // server, so a client cannot spend ammo it does not own or invent a hit.
        if (IsConnectedClient())
        {
            RpcId(1, nameof(RequestFireWeaponRpc), ++_nextFireSequence);
            return;
        }

        ExecuteShot();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestFireWeaponRpc(int sequence)
    {
        if (!Multiplayer.IsServer())
            return;

        int senderId = Multiplayer.GetRemoteSenderId();
        if (senderId != OwnerPeerId)
        {
            WarnRejectedInput($"Rejected fire request from peer {senderId}; kart belongs to peer {OwnerPeerId}.");
            return;
        }

        if (sequence <= _lastAcceptedFireSequence)
        {
            WarnRejectedInput($"Rejected stale fire request {sequence} from peer {senderId}.");
            return;
        }

        _lastAcceptedFireSequence = sequence;
        ExecuteShot();
    }

    private void ExecuteShot()
    {
        var weapon = GameManager.Instance?.GetPlayerWeapon(OwnerPeerId);
        if (weapon == null || weapon.IsDepleted)
            return;

        // One rate limit for both input and replay-style request spam.
        ulong now = Time.GetTicksMsec();
        if (now - _lastShotMs < FireIntervalMs)
            return;

        _lastShotMs = now;

        if (weapon.Class == GameManager.WeaponClass.Rocket)
        {
            weapon.Ammo--;
            BroadcastAudioCue(AudioManager.Cue.RocketLaunch, 1.0f, (float)GD.RandRange(0.95, 1.05));

            // The kart's nose is +Basis.Z (front lamps and GetForwardDirection agree),
            // so the muzzle sits 1.5 m in front of the cab.
            Vector3 spawnOffset = -GlobalTransform.Basis.X * 0.65f + Vector3.Up * 0.75f + GlobalTransform.Basis.Z * 1.5f;
            Vector3 spawnPosition = GlobalPosition + spawnOffset;
            int shot = ++_shotSequence;

            if (Multiplayer.HasMultiplayerPeer())
            {
                Rpc(nameof(SpawnRocketRpc), spawnPosition, Rotation, OwnerPeerId, shot);
            }
            else
            {
                SpawnRocket(spawnPosition, Rotation, OwnerPeerId, shot);
            }

            TriggerSpeechBubble("ROCKET FIRED!");
        }
        else if (weapon.Class == GameManager.WeaponClass.Assault)
        {
            weapon.Ammo--;
            BroadcastAudioCue(AudioManager.Cue.AssaultFire, 0.0f, (float)GD.RandRange(0.95, 1.08));
            FireAssaultShot();
        }

        if (weapon.IsDepleted)
        {
            GameManager.Instance?.SetPlayerWeapon(OwnerPeerId, null);
            TriggerSpeechBubble("WEAPON EMPTY");
        }
        else
        {
            GameManager.Instance?.SyncWeaponState(OwnerPeerId);
        }
    }

    private void FireAssaultShot()
    {
        var spaceState = GetWorld3D()?.DirectSpaceState;
        if (spaceState == null)
            return;

        Vector3 forward = GlobalTransform.Basis.Z;
        Vector3 start = GlobalPosition + Vector3.Up * 0.8f;
        Vector3 end = start + forward * 65.0f;
        var query = PhysicsRayQueryParameters3D.Create(start, end);
        query.CollisionMask = 1;
        var hit = spaceState.IntersectRay(query);
        if (hit.Count > 0 && hit.TryGetValue("collider", out var col) && col.As<GodotObject>() is Kart targetKart)
        {
            if (targetKart.OwnerPeerId != OwnerPeerId)
            {
                GameManager.Instance?.ApplyVehicleDamage(targetKart.OwnerPeerId, 10);
                targetKart.ApplyCentralImpulse(forward * 8.0f * targetKart.Mass);
                if (targetKart.ActivePassenger.HasValue)
                    targetKart.SetPanic(targetKart.PanicMeter + 15.0f);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SpawnRocketRpc(Vector3 position, Vector3 rotation, int sourcePeerId, int shot)
    {
        SpawnRocket(position, rotation, sourcePeerId, shot);
    }

    /// <summary>
    /// Every peer spawns its own copy for the flight and explosion visuals, but only the
    /// server's copy deals damage, which RocketProjectile already enforces.
    /// </summary>
    private void SpawnRocket(Vector3 position, Vector3 rotation, int sourcePeerId, int shot)
    {
        var rocket = new RocketProjectile
        {
            Name = $"Rocket_{sourcePeerId}_{shot}",
            Position = position,
            Rotation = rotation,
            SourcePeerId = sourcePeerId
        };
        GetParent()?.AddChild(rocket);
    }
}
