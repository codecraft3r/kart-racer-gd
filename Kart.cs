using Godot;
using System;

public partial class Kart : RigidBody3D
{
    [ExportGroup("Input")]
    [Export] public int OwnerPeerId { get; set; } = 1;
    [Export] public bool UseLocalInput { get; set; } = true;
    [Export] public bool IsLocalPlayer { get; set; } = false;
    [Export] public bool IsAI { get; set; } = false;
    [Export] public float TapInputHoldTime = 0.16f;

    public bool ControlsEnabled { get; private set; } = true;

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

    [ExportGroup("Visuals")]
    [Export] public float VisualRotationSpeed = 12.0f;
    [Export] public float VisualSteerLeanDegrees = 6.0f;
    [Export] public float NetworkSmoothingSpeed = 12.0f;

    public float DriftAmount { get; private set; }

    private RayCast3D[] _groundRays;
    private Node3D _visualContainer;

    private float _forwardInput;
    private float _steeringInput;
    private float _forwardTapInput;
    private float _steeringTapInput;
    private float _forwardTapTimer;
    private float _steeringTapTimer;
    private bool _isGrounded;
    private Vector3 _groundNormal = Vector3.Up;
    private float _groundGraceTimer;
    private float _driftTimer;

    private Vector3 _netTargetPosition;
    private Vector3 _netTargetRotation;
    private bool _hasNetTarget = false;

    // Tactical Taxi variables
    public GameManager.CustomerData? ActivePassenger { get; set; }
    public float PanicMeter { get; private set; } = 0.0f;
    public float BoardingProgress { get; private set; } = 0.0f;
    private float _airtimeAccumulator = 0.0f;
    private int _lastBoardingAudioStep = -1;
    private ulong _lastCollisionAudioMs;
    private ulong _lastPanicWarningMs;

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
    }

    public override void _Process(double delta)
    {
        UpdateTapTimers((float)delta);

        if (!IsAI && IsLocalPlayer && IsConnectedClient())
        {
            CaptureLocalInput();
            RpcId(1, nameof(SendInputRpc), _forwardInput, _steeringInput);
        }

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
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SendInputRpc(float forward, float steer)
    {
        if (Multiplayer.IsServer())
        {
            int senderId = Multiplayer.GetRemoteSenderId();
            if (senderId != OwnerPeerId)
            {
                GD.PushWarning($"Rejected kart input from peer {senderId}; kart belongs to peer {OwnerPeerId}.");
                return;
            }

            _forwardInput = Mathf.Clamp(forward, -1.0f, 1.0f);
            _steeringInput = Mathf.Clamp(steer, -1.0f, 1.0f);
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (ShouldRunPhysics() == false) return;

        if (ControlsEnabled && !IsAI && (UseLocalInput || IsOffline()))
        {
            CaptureLocalInput();
        }
        else if (!ControlsEnabled)
        {
            _forwardInput = 0.0f;
            _steeringInput = 0.0f;
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
        bool driftTriggered = braking &&
            forwardSpeed > MinDriftSpeed &&
            Mathf.Abs(_steeringInput) >= DriftSteeringThreshold;

        if (driftTriggered)
        {
            _driftTimer = DriftDuration;
        }
        else
        {
            _driftTimer = Mathf.Max(0.0f, _driftTimer - dt);
        }

        bool drifting = _driftTimer > 0.0f && Mathf.Abs(_steeringInput) > InputDeadzone;
        if (drifting && Mathf.Abs(lateralSpeed) >= DriftSustainLateralSpeed && _forwardInput > 0.2f)
            _driftTimer = Mathf.Max(_driftTimer, 0.25f);

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

    public void ApplyNetworkSnapshot(Vector3 position, Vector3 rotation, Vector3 velocity)
    {
        if (ShouldRunPhysics())
            return;

        if (!_hasNetTarget)
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

    public void SetAIInput(float forward, float steer)
    {
        if (IsAI && ControlsEnabled)
        {
            _forwardInput = Mathf.Clamp(forward, -1.0f, 1.0f);
            _steeringInput = Mathf.Clamp(steer, -1.0f, 1.0f);
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
        }
    }

    public bool GetControlsEnabled() => ControlsEnabled;

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
        float forwardAxis = Input.GetAxis("move_backward", "move_forward");
        float steeringAxis = Input.GetAxis("move_left", "move_right");

        _forwardInput = Mathf.Abs(forwardAxis) > InputDeadzone ? forwardAxis : (_forwardTapTimer > 0.0f ? _forwardTapInput : 0.0f);
        _steeringInput = Mathf.Abs(steeringAxis) > InputDeadzone ? steeringAxis : (_steeringTapTimer > 0.0f ? _steeringTapInput : 0.0f);

        _forwardInput = Mathf.Clamp(_forwardInput, -1.0f, 1.0f);
        _steeringInput = Mathf.Clamp(_steeringInput, -1.0f, 1.0f);
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
            float lean = Mathf.DegToRad(-_steeringInput * VisualSteerLeanDegrees * speedRatio * Mathf.Lerp(0.45f, 1.0f, DriftAmount));
            targetBasis = targetBasis.Rotated(targetBasis.Z.Normalized(), lean).Orthonormalized();
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
        Vector3 normalSum = Vector3.Zero;
        int contactCount = 0;
        bool centerContact = false;

        for (int i = 0; i < _groundRays.Length; i++)
        {
            RayCast3D ray = _groundRays[i];
            ray.ForceRaycastUpdate();
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

        if (!_isGrounded)
        {
            _airtimeAccumulator += dt;
            if (_airtimeAccumulator > 0.4f)
            {
                SetPanic(PanicMeter + 25.0f * dt);
                if (GD.Randf() < dt * 1.5f)
                    TriggerSpeechBubble(GetHumorousAirtimePhrase());
            }
        }
        else
        {
            _airtimeAccumulator = 0.0f;
            if (LinearVelocity.Length() > 2.0f)
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
            TriggerSpeechBubble("I'M OUTTA HERE!");
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

    private void OnBodyCollision(Node body)
    {
        if (Multiplayer.HasMultiplayerPeer() && !Multiplayer.IsServer()) return;

        // Ignore harmless road/ground surfaces
        string name = body.Name;
        if (name == "Ground" || 
            name.IndexOf("RoadSegment") == 0 || 
            name.IndexOf("Intersection") == 0 || 
            name.IndexOf("LaneMarker") == 0 || 
            name.IndexOf("Crosswalk") == 0)
        {
            return;
        }

        float speed = LinearVelocity.Length();
        if (speed > 5.0f)
        {
            ulong now = Time.GetTicksMsec();
            if (now - _lastCollisionAudioMs >= 140)
            {
                _lastCollisionAudioMs = now;
                AudioManager.Cue cue = speed >= 18.0f
                    ? AudioManager.Cue.CollisionHeavy
                    : speed >= 10.0f
                        ? AudioManager.Cue.CollisionMedium
                        : AudioManager.Cue.CollisionLight;
                float volumeDb = Mathf.Lerp(-7.0f, 1.0f, Mathf.Clamp((speed - 5.0f) / 20.0f, 0.0f, 1.0f));
                BroadcastAudioCue(cue, volumeDb, (float)GD.RandRange(0.92, 1.08));
            }

            if (ActivePassenger.HasValue)
            {
                float panicIncrease = speed * 1.5f;
                SetPanic(PanicMeter + panicIncrease);
                TriggerSpeechBubble(GetHumorousCollisionPhrase());
            }

            if (GameManager.Instance != null)
            {
                GameManager.Instance.ApplyVehicleDamage(OwnerPeerId, Mathf.RoundToInt(speed * 0.8f));
            }
        }
    }

    public void BoardPassenger(GameManager.CustomerData data)
    {
        BroadcastAudioCue(AudioManager.Cue.PassengerBoard, -2.0f, (float)GD.RandRange(0.97, 1.03));
        ActivePassenger = data;
        PanicMeter = 0.0f;
        if (Multiplayer.IsServer())
        {
            Rpc(nameof(SyncPassengerStateRpc), true, (int)data.Distance, (int)data.Wealth, data.MaxAcceptableDamage, data.GroupSize, data.LoadTime, 0.0f);
        }
    }

    public bool HasPassenger() => ActivePassenger.HasValue;

    public void ClearPassenger()
    {
        ActivePassenger = null;
        PanicMeter = 0.0f;
        if (Multiplayer.IsServer())
        {
            Rpc(nameof(SyncPassengerStateRpc), false, 0, 0, 0, 0, 0.0f, 0.0f);
        }
    }

    public void SetPanic(float panic)
    {
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
            Rpc(nameof(PlayKartAudioCueRpc), (int)cue, position, volumeDb, pitchScale);
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
        var font = GD.Load<Font>("res://assets/fonts/VT323-Regular.ttf");
        if (font != null)
        {
            label.Font = font;
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
}
