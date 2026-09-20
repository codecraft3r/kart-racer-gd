using Godot;

/// <summary>
/// High-performance visual juice: Headlights, neon underglow, speed streaks,
/// exhaust flames, brake lamps, and drift spark/smoke particles for the taxi.
/// </summary>
public partial class DrivingVfx : Node3D
{
    private Kart _kart;
    private MeshInstance3D[] _streaks;
    private StandardMaterial3D[] _streakMaterials;
    private MeshInstance3D _exhaust;
    private StandardMaterial3D _exhaustMaterial;
    private MeshInstance3D[] _brakeLamps;
    private StandardMaterial3D[] _brakeMaterials;
    private MeshInstance3D[] _headLamps;
    private StandardMaterial3D _headlampMaterial;
    private SpotLight3D _headlightSpot;
    private OmniLight3D _underglow;
    private MeshInstance3D[] _driftSparks;
    private StandardMaterial3D[] _driftSparkMaterials;
    private bool? _lastBraking;
    private float _sparkTimer;
    private bool _lightBudgetApplied;

    public override void _Ready()
    {
        _kart = GetParent()?.GetParent() as Kart ?? GetParent() as Kart;
        
        // Speed/Drift Streaks
        _streaks = new MeshInstance3D[4];
        _streakMaterials = new StandardMaterial3D[4];
        Vector3[] wheels = {
            new(-0.48f, 0.06f, 0.76f),
            new(0.48f, 0.06f, 0.76f),
            new(-0.48f, 0.06f, -0.82f),
            new(0.48f, 0.06f, -0.82f)
        };
        for (int i = 0; i < wheels.Length; i++)
        {
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                EmissionEnabled = true,
                Emission = new Color(0.0f, 0.9f, 1.0f),
                AlbedoColor = new Color(0.0f, 0.9f, 1.0f, 0.0f)
            };
            var streak = new MeshInstance3D
            {
                Name = $"DriftStreak{i}",
                Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.012f, 1.0f) },
                MaterialOverride = material,
                Position = wheels[i] + Vector3.Back * 0.55f,
                Visible = false
            };
            AddChild(streak);
            _streaks[i] = streak;
            _streakMaterials[i] = material;
        }

        // Exhaust Flame Glow
        _exhaustMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.1f, 0.55f),
            AlbedoColor = new Color(1.0f, 0.1f, 0.55f, 0.0f)
        };
        _exhaust = new MeshInstance3D
        {
            Name = "ExhaustGlow",
            Mesh = new SphereMesh { Radius = 0.12f, Height = 0.32f },
            MaterialOverride = _exhaustMaterial,
            Position = new Vector3(0, 0.28f, -1.47f)
        };
        AddChild(_exhaust);

        // Rear Brake Lamps
        _brakeLamps = new MeshInstance3D[2];
        _brakeMaterials = new StandardMaterial3D[2];
        for (int i = 0; i < 2; i++)
        {
            var brakeMaterial = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled = true,
                Emission = new Color(1f, 0.02f, 0.04f),
                AlbedoColor = new Color(0.35f, 0.01f, 0.02f)
            };
            var lamp = new MeshInstance3D
            {
                Name = $"BrakeLamp{i}",
                Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.12f, 0.05f) },
                MaterialOverride = brakeMaterial,
                Position = new Vector3(i == 0 ? -0.45f : 0.45f, 0.42f, -1.48f)
            };
            AddChild(lamp);
            _brakeLamps[i] = lamp;
            _brakeMaterials[i] = brakeMaterial;
        }

        // Front Headlight Lenses
        _headlampMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.95f, 0.98f, 1.0f),
            EmissionEnergyMultiplier = 2.2f,
            AlbedoColor = new Color(0.9f, 0.95f, 1.0f)
        };
        _headLamps = new MeshInstance3D[2];
        for (int i = 0; i < 2; i++)
        {
            var lamp = new MeshInstance3D
            {
                Name = $"HeadLamp{i}",
                Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.10f, 0.05f) },
                MaterialOverride = _headlampMaterial,
                Position = new Vector3(i == 0 ? -0.44f : 0.44f, 0.38f, 1.45f)
            };
            AddChild(lamp);
            _headLamps[i] = lamp;
        }

        // Forward Headlight Projection Spotlight
        _headlightSpot = new SpotLight3D
        {
            Name = "HeadlightSpot",
            Position = new Vector3(0, 0.42f, 1.40f),
            RotationDegrees = new Vector3(-8.0f, 0, 0), // Slight downward tilt onto asphalt
            SpotRange = 26.0f,
            SpotAngle = 38.0f,
            LightColor = new Color(0.92f, 0.97f, 1.0f),
            LightEnergy = 1.85f,
            ShadowEnabled = false
        };
        AddChild(_headlightSpot);

        // Neon Chassis Underglow
        _underglow = new OmniLight3D
        {
            Name = "NeonUnderglow",
            Position = new Vector3(0, 0.14f, 0),
            OmniRange = 4.2f,
            LightColor = new Color(0.0f, 0.92f, 1.0f),
            LightEnergy = 1.25f,
            ShadowEnabled = false
        };
        AddChild(_underglow);

        // Drift Spark Particles
        _driftSparks = new MeshInstance3D[4];
        _driftSparkMaterials = new StandardMaterial3D[4];
        for (int i = 0; i < 4; i++)
        {
            var sparkMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                EmissionEnabled = true,
                Emission = new Color(1.0f, 0.85f, 0.1f),
                EmissionEnergyMultiplier = 3.0f,
                AlbedoColor = new Color(1.0f, 0.85f, 0.1f, 0.0f)
            };
            var spark = new MeshInstance3D
            {
                Name = $"DriftSpark{i}",
                Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f },
                MaterialOverride = sparkMat,
                Position = new Vector3((i % 2 == 0 ? -0.48f : 0.48f), 0.05f, -0.85f),
                Visible = false
            };
            AddChild(spark);
            _driftSparks[i] = spark;
            _driftSparkMaterials[i] = sparkMat;
        }
    }

    public override void _ExitTree()
    {
        SetProcess(false);
    }

    private void ApplyLightBudget()
    {
        if (_lightBudgetApplied || _kart == null || !GodotObject.IsInstanceValid(_kart) || _headlightSpot == null || _underglow == null)
            return;

        // Every kart carried a realtime spot plus an omni. AI rivals keep their emissive
        // lenses and skip the clustered lights, which is where a full grid adds up.
        bool realtime = !_kart.IsAI;
        _headlightSpot.Visible = realtime;
        _underglow.Visible = realtime;
        _lightBudgetApplied = true;
    }

    public override void _Process(double delta)
    {
        ApplyLightBudget();
        using var perf = PerfProbe.Measure(PerfHotspot.DrivingVfxProcess);
        if (_kart == null || !GodotObject.IsInstanceValid(_kart)) return;

        float dt = (float)delta;
        float speed = _kart.LinearVelocity.Length();
        float speedRatio = Mathf.Clamp(speed / Mathf.Max(1.0f, _kart.MaxForwardSpeed), 0.0f, 1.0f);
        float drift = _kart.DriftAmount;

        // Speed/Drift Streaks
        float intensity = Mathf.Clamp((speed - 7.0f) / 14.0f, 0.0f, 1.0f) * Mathf.Lerp(0.25f, 1.0f, drift);
        for (int i = 0; i < _streaks.Length; i++)
        {
            _streaks[i].Visible = intensity > 0.04f;
            _streaks[i].Scale = new Vector3(1, 1, Mathf.Lerp(0.35f, 4.2f, intensity));
            Color c = drift > 0.45f
                ? new Color(1.0f, 0.08f, 0.52f, intensity * 0.78f)
                : new Color(0.0f, 0.9f, 1.0f, intensity * 0.58f);
            _streakMaterials[i].AlbedoColor = c;
            _streakMaterials[i].Emission = new Color(c.R, c.G, c.B) * (0.7f + intensity);
        }

        // Exhaust flame
        float flame = Mathf.Clamp(speed / 28.0f, 0.0f, 1.0f);
        _exhaust.Visible = flame > 0.08f;
        _exhaust.Scale = new Vector3(
            0.7f + flame * 0.55f,
            0.8f + Mathf.Sin((float)Time.GetTicksMsec() * 0.02f) * 0.16f,
            1.0f + flame * 1.6f
        );
        _exhaustMaterial.AlbedoColor = new Color(1.0f, 0.08f + flame * 0.35f, 0.45f + flame * 0.45f, flame * 0.75f);

        // Braking lamps
        bool braking = _kart.CurrentDriftPhase != Kart.DriftPhase.None || (speed > 2.0f && _kart.BrakeInputActive);
        if (_lastBraking != braking)
        {
            _lastBraking = braking;
            for (int i = 0; i < _brakeLamps.Length; i++)
            {
                float brakeEnergy = braking ? 3.2f : 0.45f;
                _brakeMaterials[i].EmissionEnergyMultiplier = brakeEnergy;
                _brakeMaterials[i].AlbedoColor = braking ? new Color(1f, 0.03f, 0.05f) : new Color(0.22f, 0.01f, 0.02f);
            }
        }

        // Drift Sparks & Tire Friction Particles
        bool isDrifting = drift > 0.2f && speed > 5.0f;
        _sparkTimer += dt * 35.0f;
        float chargeRatio = _kart.DriftMaxChargeTime > 0.001f ? _kart.DriftCharge / _kart.DriftMaxChargeTime : 0.0f;
        Color sparkColor = chargeRatio >= 0.75f
            ? new Color(1.0f, 0.95f, 0.2f) // Tier 3 Gold/Yellow
            : chargeRatio >= 0.35f
                ? new Color(1.0f, 0.15f, 0.65f) // Tier 2 Hot Pink
                : new Color(0.0f, 0.85f, 1.0f);  // Tier 1 Cyan

        for (int i = 0; i < _driftSparks.Length; i++)
        {
            _driftSparks[i].Visible = isDrifting;
            if (isDrifting)
            {
                float jitterX = Mathf.Sin(_sparkTimer + i * 1.7f) * 0.12f;
                float jitterY = Mathf.Abs(Mathf.Cos(_sparkTimer * 1.3f + i)) * 0.14f;
                float jitterZ = -0.85f - Mathf.Abs(Mathf.Sin(_sparkTimer * 0.9f + i)) * 0.45f;
                float baseWheelX = (i % 2 == 0 ? -0.48f : 0.48f);
                _driftSparks[i].Position = new Vector3(baseWheelX + jitterX, 0.05f + jitterY, jitterZ);
                _driftSparkMaterials[i].AlbedoColor = new Color(sparkColor.R, sparkColor.G, sparkColor.B, 0.85f);
                _driftSparkMaterials[i].Emission = sparkColor * 2.5f;
            }
        }

        // Dynamic Underglow & Headlight modulation
        if (_underglow != null)
        {
            Color underColor = drift > 0.35f ? new Color(1.0f, 0.05f, 0.6f) : new Color(0.0f, 0.9f, 1.0f);
            _underglow.LightColor = underColor;
            _underglow.LightEnergy = 1.0f + speedRatio * 0.6f;
        }
    }
}
