using Godot;
using System;

public partial class Kart : RigidBody3D
{
    [ExportGroup("Input")]
    [Export] public int OwnerPeerId { get; set; } = 1;
    [Export] public bool UseLocalInput { get; set; } = true;
    [Export] public bool IsLocalPlayer { get; set; } = false;
    [Export] public float TapInputHoldTime = 0.16f;

    [ExportGroup("Driving")]
    [Export] public float Acceleration = 4200.0f;
    [Export] public float ReverseAcceleration = 2600.0f;
    [Export] public float BrakeForce = 5200.0f;
    [Export] public float MaxForwardSpeed = 34.0f;
    [Export] public float MaxReverseSpeed = 13.0f;
    [Export] public float SteeringSpeed = 3.0f;
    [Export] public float MinSteeringSpeed = 2.0f;
    [Export] public float SideGrip = 18.0f;
    [Export] public float RollingDrag = 1.8f;
    [Export] public float ExtraGravity = 35.0f;

    [ExportGroup("Visuals")]
    [Export] public float VisualRotationSpeed = 12.0f;

    private RayCast3D _groundRay;
    private Node3D _visualContainer;

    private float _forwardInput;
    private float _steeringInput;
    private float _forwardTapInput;
    private float _steeringTapInput;
    private float _forwardTapTimer;
    private float _steeringTapTimer;
    private float _yaw = 0.0f;
    private bool _isGrounded;

    // Tactical Taxi variables
    public GameManager.CustomerData? ActivePassenger { get; set; }
    public float PanicMeter { get; private set; } = 0.0f;
    public float BoardingProgress { get; private set; } = 0.0f;
    private float _airtimeAccumulator = 0.0f;

    private const float InputDeadzone = 0.05f;

    public override void _Ready()
    {
        _groundRay = GetNode<RayCast3D>("GroundRay");
        _visualContainer = GetNode<Node3D>("VisualContainer");

        // Prevent the physics sphere from naturally rolling and tilting on its own axes
        AxisLockAngularX = true;
        AxisLockAngularY = true;
        AxisLockAngularZ = true;

        _yaw = _visualContainer.Rotation.Y;

        // Server is always the authority for physics; OwnerPeerId only identifies who may send input.
        SetMultiplayerAuthority(1);

        ContactMonitor = true;
        MaxContactsReported = 4;
        BodyEntered += OnBodyCollision;

        if (IsLocalPlayer)
        {
            var compass = new CompassArrow { Name = "CompassArrow" };
            AddChild(compass);
        }
    }

    public override void _Process(double delta)
    {
        UpdateTapTimers((float)delta);

        if (IsLocalPlayer && IsConnectedClient())
        {
            CaptureLocalInput();
            RpcId(1, nameof(SendInputRpc), _forwardInput, _steeringInput);
        }

        if (_visualContainer == null) return;

        _visualContainer.GlobalPosition = GlobalPosition;
        if (ShouldRunPhysics() == false)
        {
            _yaw = Rotation.Y;
            _groundRay.ForceRaycastUpdate();
            _isGrounded = _groundRay.IsColliding();
        }

        AlignVisualsWithGround((float)delta);
    }

    public override void _Input(InputEvent @event)
    {
        if (UseLocalInput == false && IsLocalPlayer == false && IsOffline() == false) return;

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

        if (UseLocalInput || IsOffline())
        {
            CaptureLocalInput();
        }

        float dt = (float)delta;
        _groundRay.ForceRaycastUpdate();
        _isGrounded = _groundRay.IsColliding();

        if (Multiplayer.IsServer() || IsOffline())
        {
            UpdatePassengerPanic(dt);
        }

        if (_isGrounded == false)
        {
            ApplyCentralForce(Vector3.Down * ExtraGravity * Mass);
            SyncYawToBodyRotation();
            return;
        }

        Vector3 groundNormal = _groundRay.GetCollisionNormal();
        Vector3 forwardDirection = GetForwardDirection(groundNormal);
        Vector3 sideDirection = groundNormal.Cross(forwardDirection).Normalized();

        Vector3 planarVelocity = LinearVelocity - groundNormal * LinearVelocity.Dot(groundNormal);
        float forwardSpeed = planarVelocity.Dot(forwardDirection);
        float lateralSpeed = planarVelocity.Dot(sideDirection);
        float planarSpeed = planarVelocity.Length();

        ApplyCentralForce(-sideDirection * lateralSpeed * SideGrip * Mass);
        ApplyCentralForce(-forwardDirection * forwardSpeed * RollingDrag * Mass);

        if (Mathf.Abs(_forwardInput) > InputDeadzone)
        {
            bool braking = Mathf.Abs(forwardSpeed) > 1.0f && Mathf.Sign(_forwardInput) != Mathf.Sign(forwardSpeed);
            float speedLimit = _forwardInput > 0.0f ? MaxForwardSpeed : MaxReverseSpeed;
            float driveForce = braking ? BrakeForce : (_forwardInput > 0.0f ? Acceleration : ReverseAcceleration);

            if (braking || Mathf.Abs(forwardSpeed) < speedLimit)
            {
                ApplyCentralForce(forwardDirection * _forwardInput * driveForce);
            }
        }

        if (Mathf.Abs(_steeringInput) > InputDeadzone && planarSpeed > 0.1f)
        {
            float steeringAuthority = Mathf.Clamp(planarSpeed / MinSteeringSpeed, 0.0f, 1.0f);
            float highSpeedFade = 1.0f - Mathf.Clamp(planarSpeed / MaxForwardSpeed, 0.0f, 1.0f) * 0.45f;
            float reverseMultiplier = forwardSpeed >= -0.5f ? 1.0f : -1.0f;

            _yaw -= _steeringInput * reverseMultiplier * SteeringSpeed * steeringAuthority * highSpeedFade * dt;
        }

        SyncYawToBodyRotation();
    }

    public void ApplyNetworkSnapshot(Vector3 position, Vector3 rotation, Vector3 velocity)
    {
        if (ShouldRunPhysics())
            return;

        GlobalPosition = position;
        Rotation = rotation;
        LinearVelocity = velocity;
        _yaw = rotation.Y;
    }

    private void SyncYawToBodyRotation()
    {
        Rotation = new Vector3(Rotation.X, _yaw, Rotation.Z);
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
            Vector3 groundNormal = _groundRay.GetCollisionNormal();
            Vector3 visualForward = GetForwardDirection(groundNormal);
            Vector3 visualRight = groundNormal.Cross(visualForward).Normalized();

            Basis targetBasis = new Basis(visualRight, groundNormal, visualForward).Orthonormalized();
            Basis currentBasis = _visualContainer.Transform.Basis.Orthonormalized();
            float blend = Mathf.Clamp(VisualRotationSpeed * delta, 0.0f, 1.0f);

            _visualContainer.Transform = new Transform3D(
                currentBasis.Slerp(targetBasis, blend),
                _visualContainer.Transform.Origin
            );
        }
    }

    private Vector3 GetForwardDirection(Vector3 groundNormal)
    {
        Vector3 forward = new Vector3(Mathf.Sin(_yaw), 0.0f, Mathf.Cos(_yaw));
        forward -= groundNormal * forward.Dot(groundNormal);

        if (forward.LengthSquared() < 0.0001f)
        {
            forward = Vector3.Back - groundNormal * Vector3.Back.Dot(groundNormal);
        }

        return forward.Normalized();
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
            if (!IsOffline() && Multiplayer.IsServer())
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
            if (ActivePassenger.HasValue)
            {
                float panicIncrease = speed * 1.5f;
                SetPanic(PanicMeter + panicIncrease);
                TriggerSpeechBubble(GetHumorousCollisionPhrase());
            }

            if (!IsOffline() && GameManager.Instance != null)
            {
                GameManager.Instance.ApplyVehicleDamage(OwnerPeerId, Mathf.RoundToInt(speed * 0.8f));
            }
        }
    }

    public void BoardPassenger(GameManager.CustomerData data)
    {
        ActivePassenger = data;
        PanicMeter = 0.0f;
        if (Multiplayer.IsServer())
        {
            Rpc(nameof(SyncPassengerStateRpc), true, (int)data.Distance, (int)data.Wealth, data.MaxAcceptableDamage, data.GroupSize, data.LoadTime, 0.0f);
        }
    }

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
            Modulate = Colors.HotPink,
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
