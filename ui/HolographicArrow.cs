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
    [Export] public float FloatAmplitude = 0.4f;
    [Export] public float RotationSpeed = 1.8f;

    public override void _Ready()
    {
        _baseY = Position.Y;

        // Create arrow mesh pointing downwards
        var cone = new CylinderMesh
        {
            TopRadius = 0.0f,
            BottomRadius = 1.6f,
            Height = 3.2f,
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
        
        // Spin around Y axis
        RotateY(RotationSpeed * (float)delta);

        // Dynamically adjust visibility based on local player perspective
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

        string grandParentName = grandparent.Name.ToString();

        // 1. If it's attached to a PickupZone
        if (grandparent is PickupZone pickupZone)
        {
            // If the player has a passenger, they shouldn't see pickup zone arrows
            if (localKart.ActivePassenger.HasValue)
            {
                Visible = false;
                return;
            }

            // If the player's vehicle health is too low (meaning damage is too high)
            // for this customer's preferences, they cannot pick them up. Hide the arrow.
            int playerHealth = GameManager.Instance != null ? GameManager.Instance.GetPlayerHealth(localKart.OwnerPeerId) : 100;
            if (playerHealth < 100 - pickupZone.MaxAcceptableDamage)
            {
                Visible = false;
                return;
            }

            Visible = true;
        }
        // 2. If it's attached to a Drop-off Area
        else if (grandParentName.StartsWith("DropoffArea_"))
        {
            // Only show if the player currently has a passenger
            if (!localKart.ActivePassenger.HasValue)
            {
                Visible = false;
                return;
            }

            // Respect parent container's visibility (since TaxiMode already sets it
            // to visible only for the local player peer who owns it)
            Visible = (parentNode is Node3D parent3D) ? parent3D.Visible : true;
        }
        else
        {
            Visible = false;
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
