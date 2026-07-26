using Godot;

public partial class HolographicArrow : Node3D
{
    private MeshInstance3D _mesh;
    private float _timeAccumulator = 0.0f;
    private Color _color;
    private float _baseY;

    [Export] public Color ArrowColor
    {
        get => _color;
        set
        {
            _color = value;
            UpdateMaterial();
        }
    }

    [Export] public float FloatSpeed = 3.0f;
    [Export] public float FloatAmplitude = 0.18f;
    [Export] public float RotationSpeed = 1.8f;

    public override void _Ready()
    {
        _baseY = Position.Y;

        // Create arrow mesh pointing downwards
        var cone = new CylinderMesh
        {
            TopRadius = 0.0f,
            BottomRadius = 0.55f,
            Height = 1.2f,
            RadialSegments = 5
        };

        _mesh = new MeshInstance3D
        {
            Name = "ArrowMesh",
            Mesh = cone,
            Rotation = new Vector3(Mathf.Pi, 0.0f, 0.0f),
            Position = Vector3.Zero
        };

        AddChild(_mesh);
        UpdateMaterial();
    }

    public override void _Process(double delta)
    {
        _timeAccumulator += (float)delta;
        
        // Bob up and down relative to the initial baseY
        float hoverOffset = Mathf.Sin(_timeAccumulator * FloatSpeed) * FloatAmplitude;
        Position = new Vector3(Position.X, _baseY + hoverOffset, Position.Z);
        
        // Dynamically adjust visibility based on the shared objective query.
        UpdateArrowVisibility();
    }

    private void UpdateArrowVisibility()
    {
        var localKart = GetLocalPlayerKart();
        if (localKart == null || !IsInstanceValid(localKart))
        {
            Visible = false;
            return;
        }

        var parentNode = GetParent();
        if (parentNode == null || !IsInstanceValid(parentNode))
        {
            Visible = false;
            return;
        }

        var grandparent = parentNode.GetParent();
        if (grandparent == null || !IsInstanceValid(grandparent))
        {
            Visible = false;
            return;
        }

        if (!TaxiMode.Instance.TryGetObjectiveForKart(localKart, out TaxiMode.ObjectiveTarget target))
        {
            Visible = false;
            return;
        }

        if (grandparent is not Node3D markerNode)
        {
            Visible = false;
            return;
        }
        Vector3 markerPosition = markerNode.GlobalPosition;
        bool thisIsTarget = markerPosition.DistanceSquaredTo(target.WorldPosition) < 0.01f;
        if (!thisIsTarget || target.Distance <= 5.5f)
        {
            Visible = false;
            return;
        }

        Visible = true;
        float scale = target.Distance > 45.0f ? 0.85f : 1.0f;
        Scale = Vector3.One * scale;
        var material = _mesh?.MaterialOverride as StandardMaterial3D;
        if (material != null)
        {
            float alpha = target.Distance > 45.0f ? 0.38f : target.Distance <= 12.0f ? 0.24f : 0.50f;
            material.AlbedoColor = new Color(target.Color.R, target.Color.G, target.Color.B, alpha);
            material.Emission = target.Color * alpha;
        }
    }

    private Kart GetLocalPlayerKart()
    {
        if (GameManager.Instance == null)
            return null;

        int localPeerId = 1;
        if (Multiplayer != null && Multiplayer.HasMultiplayerPeer() && !(Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer))
        {
            localPeerId = Multiplayer.GetUniqueId();
        }

        return GameManager.Instance.GetKart(localPeerId);
    }

    private void UpdateMaterial()
    {
        if (_mesh == null) return;

        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(_color.R, _color.G, _color.B, 0.65f),
            EmissionEnabled = true,
            Emission = new Color(_color.R, _color.G, _color.B) * 1.6f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };

        _mesh.MaterialOverride = mat;
    }
}
