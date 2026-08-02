using Godot;

/// <summary>
/// Camera-relative neon rain. Keeping it attached to the camera guarantees readable weather
/// at driving speeds without filling the procedurally generated city with hundreds of nodes.
/// </summary>
public partial class CameraRainVfx : MultiMeshInstance3D
{
    [Export] public int DropCount = 104;
    private readonly Vector3[] _drops = new Vector3[104];
    private readonly RandomNumberGenerator _rng = new();
    private Kart _kart;
    private float _speed;

    public override void _Ready()
    {
        _kart = GetNodeOrNull<Kart>("../../Kart");
        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            VertexColorUseAsAlbedo = true,
            AlbedoColor = new Color(0.2f, 0.84f, 1.0f, 0.46f),
            EmissionEnabled = true,
            Emission = new Color(0.05f, 0.7f, 1.0f)
        };
        var mesh = new QuadMesh { Size = new Vector2(0.04f, 0.9f), Material = material };
        Multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = DropCount,
            Mesh = mesh,
            // Camera-relative drops move far outside the default zero-sized MultiMesh bounds.
            CustomAabb = new Aabb(new Vector3(-18.0f, -11.0f, -33.0f), new Vector3(36.0f, 23.0f, 33.0f))
        };

        _rng.Seed = 8088;
        for (int index = 0; index < DropCount; index++)
        {
            _drops[index] = NewDrop(_rng, true);
            SetDropTransform(index);
        }
    }

    public override void _Process(double delta)
    {
        using var perf = PerfProbe.Measure(PerfHotspot.CameraRainProcess);
        if (Multimesh == null) return;
        _speed = _kart != null && GodotObject.IsInstanceValid(_kart) ? _kart.LinearVelocity.Length() : 0.0f;
        float fallSpeed = 13.0f + _speed * 1.18f;
        _rng.Seed = (ulong)Time.GetTicksMsec();
        for (int index = 0; index < DropCount; index++)
        {
            _drops[index].Y -= fallSpeed * (float)delta;
            _drops[index].Z += _speed * 0.14f * (float)delta;
            if (_drops[index].Y < -10.0f || _drops[index].Z > -1.1f)
                _drops[index] = NewDrop(_rng, false);
            SetDropTransform(index);
        }
    }

    private Vector3 NewDrop(RandomNumberGenerator rng, bool scattered)
    {
        float z = scattered ? rng.RandfRange(-30.0f, -2.0f) : rng.RandfRange(-32.0f, -20.0f);
        return new Vector3(rng.RandfRange(-17.0f, 17.0f), rng.RandfRange(-7.0f, 11.0f), z);
    }

    private void SetDropTransform(int index)
    {
        float stretch = 0.75f + Mathf.Clamp(_speed / 28.0f, 0.0f, 1.0f) * 2.0f;
        Multimesh.SetInstanceTransform(index, new Transform3D(Basis.FromScale(new Vector3(1.0f, stretch, 1.0f)), _drops[index]));
    }
}
