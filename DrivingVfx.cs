using Godot;

/// <summary>Speed and drift-reactive light streaks, exhaust glow, and brake lamps for the taxi.</summary>
public partial class DrivingVfx : Node3D
{
    private static readonly StringName MoveBackwardAction = "move_backward";
    private Kart _kart;
    private MeshInstance3D[] _streaks;
    private StandardMaterial3D[] _streakMaterials;
    private MeshInstance3D _exhaust;
    private StandardMaterial3D _exhaustMaterial;
    private MeshInstance3D[] _brakeLamps;
    private StandardMaterial3D[] _brakeMaterials;
    private bool? _lastBraking;

    public override void _Ready()
    {
        _kart = GetParent()?.GetParent() as Kart;
        _streaks = new MeshInstance3D[4];
        _streakMaterials = new StandardMaterial3D[4];
        Vector3[] wheels = { new(-0.48f, 0.06f, 0.76f), new(0.48f, 0.06f, 0.76f), new(-0.48f, 0.06f, -0.82f), new(0.48f, 0.06f, -0.82f) };
        for (int i = 0; i < wheels.Length; i++)
        {
            var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = new Color(0.0f, 0.9f, 1.0f), AlbedoColor = new Color(0.0f, 0.9f, 1.0f, 0.0f) };
            var streak = new MeshInstance3D { Name = $"DriftStreak{i}", Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.012f, 1.0f) }, MaterialOverride = material, Position = wheels[i] + Vector3.Back * 0.55f, Visible = false };
            AddChild(streak);
            _streaks[i] = streak;
            _streakMaterials[i] = material;
        }

        _exhaustMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = new Color(1.0f, 0.1f, 0.55f), AlbedoColor = new Color(1.0f, 0.1f, 0.55f, 0.0f) };
        _exhaust = new MeshInstance3D { Name = "ExhaustGlow", Mesh = new SphereMesh { Radius = 0.12f, Height = 0.32f }, MaterialOverride = _exhaustMaterial, Position = new Vector3(0, 0.28f, -1.47f) };
        AddChild(_exhaust);

        _brakeLamps = new MeshInstance3D[2];
        _brakeMaterials = new StandardMaterial3D[2];
        for (int i = 0; i < 2; i++)
        {
            var brakeMaterial = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, EmissionEnabled = true, Emission = new Color(1f, 0.02f, 0.04f), AlbedoColor = new Color(0.35f, 0.01f, 0.02f) };
            var lamp = new MeshInstance3D { Name = $"BrakeLamp{i}", Mesh = new BoxMesh { Size = new Vector3(0.28f, 0.12f, 0.05f) }, MaterialOverride = brakeMaterial, Position = new Vector3(i == 0 ? -0.45f : 0.45f, 0.42f, -1.48f) };
            AddChild(lamp);
            _brakeLamps[i] = lamp;
            _brakeMaterials[i] = brakeMaterial;
        }
    }

    public override void _Process(double delta)
    {
        using var perf = PerfProbe.Measure(PerfHotspot.DrivingVfxProcess);
        if (_kart == null || !GodotObject.IsInstanceValid(_kart)) return;
        float speed = _kart.LinearVelocity.Length();
        float intensity = Mathf.Clamp((speed - 7.0f) / 14.0f, 0.0f, 1.0f) * Mathf.Lerp(0.25f, 1.0f, _kart.DriftAmount);
        for (int i = 0; i < _streaks.Length; i++)
        {
            _streaks[i].Visible = intensity > 0.04f;
            _streaks[i].Scale = new Vector3(1, 1, Mathf.Lerp(0.35f, 4.2f, intensity));
            Color c = _kart.DriftAmount > 0.45f ? new Color(1.0f, 0.08f, 0.52f, intensity * 0.78f) : new Color(0.0f, 0.9f, 1.0f, intensity * 0.58f);
            _streakMaterials[i].AlbedoColor = c;
            _streakMaterials[i].Emission = new Color(c.R, c.G, c.B) * (0.7f + intensity);
        }
        float flame = Mathf.Clamp(speed / 28.0f, 0.0f, 1.0f);
        _exhaust.Visible = flame > 0.08f;
        _exhaust.Scale = new Vector3(0.7f + flame * 0.55f, 0.8f + Mathf.Sin((float)Time.GetTicksMsec() * 0.02f) * 0.16f, 1.0f + flame * 1.6f);
        _exhaustMaterial.AlbedoColor = new Color(1.0f, 0.08f + flame * 0.35f, 0.45f + flame * 0.45f, flame * 0.75f);
        bool braking = _kart.CurrentDriftPhase != Kart.DriftPhase.None || (_kart.LinearVelocity.Length() > 2.0f && Input.IsActionPressed(MoveBackwardAction));
        if (_lastBraking != braking)
        {
            _lastBraking = braking;
            for (int i = 0; i < _brakeLamps.Length; i++)
            {
                float brakeEnergy = braking ? 3.0f : 0.45f;
                _brakeMaterials[i].EmissionEnergyMultiplier = brakeEnergy;
                _brakeMaterials[i].AlbedoColor = braking ? new Color(1f, 0.03f, 0.05f) : new Color(0.22f, 0.01f, 0.02f);
            }
        }
    }
}
