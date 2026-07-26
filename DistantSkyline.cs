using Godot;

/// <summary>
/// Collision-free, camera-parallax skyline used to extend the playable city beyond its road grid.
/// </summary>
public partial class DistantSkyline : Node3D
{
    [Export] public int TowersPerBand { get; set; } = 34;
    [Export] public float FarParallax { get; set; } = 0.07f;
    [Export] public float NearParallax { get; set; } = 0.16f;

    private Camera3D _camera;
    private Node3D _farBand;
    private Node3D _nearBand;
    private int _towerCount;

    public override void _Ready()
    {
        _camera = GetViewport().GetCamera3D();

        _farBand = new Node3D { Name = "FarSkyline" };
        _nearBand = new Node3D { Name = "NearSkyline" };
        AddChild(_farBand);
        AddChild(_nearBand);

        BuildBand(_farBand, 214f, new Color(0.045f, 0.025f, 0.11f, 0.52f), new Color(0.14f, 0.65f, 1f, 0.75f), 1);
        BuildBand(_nearBand, 158f, new Color(0.085f, 0.02f, 0.14f, 0.76f), new Color(1f, 0.1f, 0.57f, 0.92f), 2);
    }

    public override void _Process(double delta)
    {
        _camera ??= GetViewport().GetCamera3D();
        if (_camera == null)
        {
            return;
        }

        Vector3 cameraPlanar = _camera.GlobalPosition;
        cameraPlanar.Y = 0f;
        _farBand.Position = cameraPlanar * FarParallax;
        _nearBand.Position = cameraPlanar * NearParallax;
    }

    public int GetTowerCount() => _towerCount;

    private void BuildBand(Node3D band, float radius, Color bodyColor, Color crownColor, int seed)
    {
        var random = new RandomNumberGenerator { Seed = (ulong)seed };
        var bodyMaterial = new StandardMaterial3D
        {
            AlbedoColor = bodyColor,
            Roughness = 0.92f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = bodyColor * 0.55f,
            EmissionEnergyMultiplier = 0.55f
        };
        var crownMaterial = new StandardMaterial3D
        {
            AlbedoColor = crownColor,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = crownColor,
            EmissionEnergyMultiplier = 1.8f
        };

        for (int index = 0; index < TowersPerBand; index++)
        {
            bool alongX = index % 2 == 0;
            float side = index % 4 < 2 ? -1f : 1f;
            float span = Mathf.Lerp(-radius, radius, random.Randf());
            float depth = radius + random.RandfRange(-16f, 16f);
            Vector3 position = alongX ? new Vector3(span, 0f, side * depth) : new Vector3(side * depth, 0f, span);
            float height = random.RandfRange(20f, band == _farBand ? 54f : 70f);
            float width = random.RandfRange(5f, 12f);
            float depthSize = random.RandfRange(5f, 12f);

            var tower = new MeshInstance3D
            {
                Name = $"SkylineTower{index:00}",
                Position = position + Vector3.Up * (height * 0.5f),
                Mesh = new BoxMesh { Size = new Vector3(width, height, depthSize) },
                MaterialOverride = bodyMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            band.AddChild(tower);
            _towerCount++;

            if (index % 3 == 0)
            {
                var crown = new MeshInstance3D
                {
                    Name = "NeonCrown",
                    Position = position + Vector3.Up * (height + 0.7f),
                    Mesh = new BoxMesh { Size = new Vector3(width * 0.92f, 0.7f, depthSize * 0.92f) },
                    MaterialOverride = crownMaterial,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                };
                band.AddChild(crown);
            }
        }
    }
}
