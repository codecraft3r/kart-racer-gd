using Godot;

/// <summary>
/// Lightweight procedural passenger used for pickup, ride, and drop-off presentation.
/// The gameplay passenger remains authoritative in Kart; this node only makes its state visible.
/// </summary>
public partial class PassengerActor : Node3D
{
    private Node3D _art;
    private Vector3 _start;
    private Vector3 _target;
    private float _boardingProgress;
    private float _phase;
    private bool _isBoarding;
    private bool _isExiting;

    public void Build(Color color, string labelText, float phaseOffset = 0.0f)
    {
        _phase = phaseOffset;
        _art = new Node3D { Name = "PassengerArt" };
        AddChild(_art);

        var fabric = new StandardMaterial3D
        {
            AlbedoColor = color,
            EmissionEnabled = true,
            Emission = color * 0.22f,
            Roughness = 0.72f
        };
        var skin = new StandardMaterial3D { AlbedoColor = new Color(0.88f, 0.58f, 0.42f), Roughness = 0.9f };
        _art.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.18f, Height = 0.78f }, MaterialOverride = fabric, Position = new Vector3(0, 0.62f, 0) });
        _art.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.2f, Height = 0.4f }, MaterialOverride = skin, Position = new Vector3(0, 1.2f, 0) });

        var tag = new Label3D
        {
            Name = "PassengerTag",
            Text = labelText,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            PixelSize = 0.009f,
            FontSize = 38,
            OutlineSize = 4,
            Modulate = color,
            Position = new Vector3(0, 1.75f, 0)
        };
        tag.Font = GD.Load<Font>("res://assets/fonts/VT323-Regular.ttf");
        _art.AddChild(tag);
    }

    public void SetBoarding(Kart kart, float progress)
    {
        if (kart == null || !GodotObject.IsInstanceValid(kart)) return;
        if (!_isBoarding)
        {
            _isBoarding = true;
            _start = GlobalPosition;
        }
        _boardingProgress = progress;
        _target = kart.GlobalPosition + kart.GlobalTransform.Basis.X * 0.72f + kart.GlobalTransform.Basis.Z * 0.1f;
    }

    public void ExitFrom(Kart kart)
    {
        _isExiting = true;
        var tag = _art?.GetNodeOrNull<Label3D>("PassengerTag");
        if (tag != null)
        {
            tag.Text = "THANKS!";
            tag.Modulate = new Color(1.0f, 0.82f, 0.2f);
        }
        _start = GlobalPosition;
        _target = kart.GlobalPosition - kart.GlobalTransform.Basis.X * 2.8f - kart.GlobalTransform.Basis.Z * 0.5f;
    }

    public override void _Process(double delta)
    {
        float time = (float)Time.GetTicksMsec() * 0.001f + _phase;
        if (_art != null)
        {
            float stride = (_isBoarding || _isExiting) ? Mathf.Sin(time * 15.0f) * 0.08f : Mathf.Sin(time * 2.4f) * 0.035f;
            _art.Position = new Vector3(0, stride, 0);
        }

        if (_isBoarding)
        {
            float t = Mathf.SmoothStep(0.0f, 1.0f, Mathf.Clamp(_boardingProgress * 1.25f, 0.0f, 1.0f));
            GlobalPosition = _start.Lerp(_target, t);
            LookAt(_target + GlobalTransform.Basis.Z, Vector3.Up, true);
            if (_boardingProgress >= 0.9f)
                Visible = false;
        }
        else if (_isExiting)
        {
            GlobalPosition = GlobalPosition.MoveToward(_target, (float)delta * 3.3f);
            LookAt(_target, Vector3.Up, true);
            if (GlobalPosition.DistanceTo(_target) < 0.15f)
                QueueFree();
        }
    }
}
