using Godot;
using System.Collections.Generic;

/// <summary>
/// Collision-free, camera-parallax skyline used to extend the playable city beyond its road grid.
/// Three seeded depth bands (far/mid/near) with instanced window rhythm, pulsing crown
/// beacons, and a small drifting hover-traffic ring. No physics bodies, no per-frame
/// allocations; all variation derives from SkylineSeed so layouts are stable.
/// </summary>
public partial class DistantSkyline : Node3D
{
    [Export] public int TowersPerBand { get; set; } = 34;
    [Export] public int SkylineSeed { get; set; } = 7;
    [Export] public float FarParallax { get; set; } = 0.07f;
    [Export] public float MidParallax { get; set; } = 0.11f;
    [Export] public float NearParallax { get; set; } = 0.16f;
    [Export] public float BeaconPulseSpeed { get; set; } = 1.6f;
    [Export] public int HoverTrafficCount { get; set; } = 24;

    private Camera3D _camera;
    private Node3D _farBand;
    private Node3D _midBand;
    private Node3D _nearBand;
    private Node3D _trafficBand;
    private int _towerCount;
    private float _elapsed;
    private StandardMaterial3D _farCrownMat;
    private StandardMaterial3D _midCrownMat;
    private StandardMaterial3D _nearCrownMat;
    private float _farCrownBase = 1.8f;
    private float _midCrownBase = 1.8f;
    private float _nearCrownBase = 1.8f;
    private MultiMesh _trafficMesh;
    private Vector3 _trafficCenter;
    private float[] _trafficAngle = System.Array.Empty<float>();
    private float[] _trafficRadius = System.Array.Empty<float>();
    private float[] _trafficHeight = System.Array.Empty<float>();
    private float[] _trafficSpeed = System.Array.Empty<float>();
    private float[] _trafficSize = System.Array.Empty<float>();

    public override void _Ready()
    {
        _camera = GetViewport().GetCamera3D();

        _farBand = new Node3D { Name = "FarSkyline" };
        _midBand = new Node3D { Name = "MidSkyline" };
        _nearBand = new Node3D { Name = "NearSkyline" };
        _trafficBand = new Node3D { Name = "SkylineTraffic" };
        AddChild(_farBand);
        AddChild(_midBand);
        AddChild(_nearBand);
        AddChild(_trafficBand);

        _farCrownMat = BuildBand(_farBand, 214f, new Color(0.045f, 0.025f, 0.11f, 0.52f), new Color(0.14f, 0.65f, 1f, 0.75f), SkylineSeed + 11, 54f);
        _midCrownMat = BuildBand(_midBand, 186f, new Color(0.06f, 0.022f, 0.12f, 0.64f), new Color(1f, 0.62f, 0.12f, 0.85f), SkylineSeed + 23, 62f);
        _nearCrownMat = BuildBand(_nearBand, 158f, new Color(0.085f, 0.02f, 0.14f, 0.76f), new Color(1f, 0.1f, 0.57f, 0.92f), SkylineSeed + 37, 70f);
        _farCrownBase = _farCrownMat != null ? _farCrownMat.EmissionEnergyMultiplier : 1.8f;
        _midCrownBase = _midCrownMat != null ? _midCrownMat.EmissionEnergyMultiplier : 1.8f;
        _nearCrownBase = _nearCrownMat != null ? _nearCrownMat.EmissionEnergyMultiplier : 1.8f;
        BuildHoverTraffic();
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_camera))
            _camera = GetViewport()?.GetCamera3D();

        if (!IsInstanceValid(_camera))
            return;

        _elapsed += (float)delta;
        Vector3 cameraPlanar = _camera.GlobalPosition;
        cameraPlanar.Y = 0f;
        _farBand.Position = cameraPlanar * FarParallax;
        _midBand.Position = cameraPlanar * MidParallax;
        _nearBand.Position = cameraPlanar * NearParallax;
        _trafficBand.Position = cameraPlanar * MidParallax;
        PulseCrowns();
        DriftTraffic();
    }

    public int GetTowerCount() => _towerCount;

    private StandardMaterial3D BuildBand(Node3D band, float radius, Color bodyColor, Color crownColor, int seed, float maxHeight)
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

        var towerTransforms = new List<Transform3D>(TowersPerBand);
        var crownTransforms = new List<Transform3D>(TowersPerBand / 3 + 1);

        for (int index = 0; index < TowersPerBand; index++)
        {
            bool alongX = index % 2 == 0;
            float side = index % 4 < 2 ? -1f : 1f;
            float span = Mathf.Lerp(-radius, radius, random.Randf());
            float depth = radius + random.RandfRange(-16f, 16f);
            Vector3 position = alongX ? new Vector3(span, 0f, side * depth) : new Vector3(side * depth, 0f, span);
            float height = random.RandfRange(20f, maxHeight);
            float width = random.RandfRange(5f, 12f);
            float depthSize = random.RandfRange(5f, 12f);

            towerTransforms.Add(new Transform3D(
                Basis.FromScale(new Vector3(width, height, depthSize)),
                position + Vector3.Up * (height * 0.5f)));
            _towerCount++;

            if (index % 3 == 0)
            {
                crownTransforms.Add(new Transform3D(
                    Basis.FromScale(new Vector3(width * 0.92f, 0.7f, depthSize * 0.92f)),
                    position + Vector3.Up * (height + 0.7f)));
            }
        }

        // A band used to be one mesh node per tower and crown. The instance transforms
        // below describe the same boxes, so only the draw submission count changes.
        float extent = radius + 16f;
        var bandBounds = new Aabb(new Vector3(-extent, 0f, -extent), new Vector3(extent * 2f, 90f, extent * 2f));

        AddInstancedBand(band, "SkylineTowers", towerTransforms, bodyMaterial, bandBounds);
        AddInstancedBand(band, "SkylineCrowns", crownTransforms, crownMaterial, bandBounds);
        return crownMaterial;
    }

    private void PulseCrowns()
    {
        if (BeaconPulseSpeed <= 0f)
            return;
        float t = _elapsed * BeaconPulseSpeed;
        if (IsInstanceValid(_farBand) && _farCrownMat != null)
            _farCrownMat.EmissionEnergyMultiplier = _farCrownBase * (0.86f + 0.14f * Mathf.Sin(t));
        if (IsInstanceValid(_midBand) && _midCrownMat != null)
            _midCrownMat.EmissionEnergyMultiplier = _midCrownBase * (0.86f + 0.14f * Mathf.Sin(t * 1.13f + 2.1f));
        if (IsInstanceValid(_nearBand) && _nearCrownMat != null)
            _nearCrownMat.EmissionEnergyMultiplier = _nearCrownBase * (0.86f + 0.14f * Mathf.Sin(t * 0.87f + 4.2f));
    }

    private void BuildHoverTraffic()
    {
        int count = Mathf.Max(0, HoverTrafficCount);
        _trafficAngle = new float[count];
        _trafficRadius = new float[count];
        _trafficHeight = new float[count];
        _trafficSpeed = new float[count];
        _trafficSize = new float[count];
        _trafficCenter = Vector3.Zero;
        if (count == 0)
        {
            _trafficMesh = null;
            return;
        }
        var random = new RandomNumberGenerator { Seed = (ulong)(SkylineSeed + 99) };
        for (int i = 0; i < count; i++)
        {
            _trafficAngle[i] = random.RandfRange(0f, Mathf.Tau);
            _trafficRadius[i] = random.RandfRange(112f, 168f);
            _trafficHeight[i] = random.RandfRange(24f, 54f);
            float dir = i % 2 == 0 ? 1f : -1f;
            _trafficSpeed[i] = dir * random.RandfRange(0.014f, 0.042f);
            _trafficSize[i] = random.RandfRange(0.9f, 2.2f);
        }
        var trafficMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.62f, 0.96f, 1f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            EmissionEnabled = true,
            Emission = new Color(0.1f, 0.8f, 1f),
            EmissionEnergyMultiplier = 2.2f
        };
        _trafficMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = count,
            Mesh = new BoxMesh { Size = Vector3.One },
            CustomAabb = new Aabb(new Vector3(-190f, 0f, -190f), new Vector3(380f, 80f, 380f))
        };
        for (int i = 0; i < count; i++)
        {
            var pos = new Vector3(Mathf.Cos(_trafficAngle[i]) * _trafficRadius[i], _trafficHeight[i], Mathf.Sin(_trafficAngle[i]) * _trafficRadius[i]);
            float s = _trafficSize[i];
            _trafficMesh.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(s * 2.2f, s * 0.5f, s * 0.8f)), pos));
        }
        _trafficBand.AddChild(new MultiMeshInstance3D
        {
            Name = "SkylineHoverTraffic",
            Multimesh = _trafficMesh,
            MaterialOverride = trafficMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }

    private void DriftTraffic()
    {
        if (_trafficMesh == null || _trafficAngle.Length == 0)
            return;
        float dt = (float)GetProcessDeltaTime();
        if (dt <= 0f)
            return;
        for (int i = 0; i < _trafficAngle.Length; i++)
        {
            _trafficAngle[i] += _trafficSpeed[i] * dt;
            if (_trafficAngle[i] > Mathf.Tau)
                _trafficAngle[i] -= Mathf.Tau;
            else if (_trafficAngle[i] < 0f)
                _trafficAngle[i] += Mathf.Tau;
            float bob = Mathf.Sin(_elapsed * 0.7f + i * 1.7f) * 1.5f;
            var pos = new Vector3(Mathf.Cos(_trafficAngle[i]) * _trafficRadius[i], _trafficHeight[i] + bob, Mathf.Sin(_trafficAngle[i]) * _trafficRadius[i]);
            float s = _trafficSize[i];
            _trafficMesh.SetInstanceTransform(i, new Transform3D(Basis.FromScale(new Vector3(s * 2.2f, s * 0.5f, s * 0.8f)), pos));
        }
    }

    private static void AddInstancedBand(Node3D band, string name, List<Transform3D> transforms, Material material, Aabb bounds)
    {
        if (transforms.Count == 0)
            return;

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            InstanceCount = transforms.Count,
            Mesh = new BoxMesh { Size = Vector3.One },
            CustomAabb = bounds
        };

        for (int index = 0; index < transforms.Count; index++)
            multimesh.SetInstanceTransform(index, transforms[index]);

        band.AddChild(new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multimesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });
    }
}
