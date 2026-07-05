using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class TrackBuilder : Node3D
{
    [Export] public int BuildingCount = 40;
    [Export] public float TrackRadius = 50.0f;
    [Export] public float TrackWidth = 14.0f;
    [Export] public int RoadSegmentCount = 128;
    [Export] public int DecorationCount = 22;
    [Export] public int LaneMarkerCount = 36;
    [Export] public int TrackLightCount = 16;
    [Export] public float ShoulderWidth = 7.0f;
    [Export] public float BuildingSetback = 7.0f;
    [Export] public float BuildingJitter = 4.0f;
    [Export] public float BuildingFootprintMin = 8.5f;
    [Export] public float BuildingFootprintMax = 13.5f;
    [Export] public float DecorationFootprintMin = 3.0f;
    [Export] public float DecorationFootprintMax = 5.5f;
    [Export] public float TrackLightEnergy = 0.95f;
    [Export] public float TrackLightRange = 16.0f;
    [Export] public int Seed = -1;

    private PackedScene[] _buildingScenes = Array.Empty<PackedScene>();
    private PackedScene[] _decorationScenes = Array.Empty<PackedScene>();
    private RandomNumberGenerator _rng = new();
    private StandardMaterial3D _roadMaterial;
    private StandardMaterial3D _laneMarkerMaterial;
    private StandardMaterial3D _curbRedMaterial;
    private StandardMaterial3D _curbWhiteMaterial;
    private StandardMaterial3D _lightPoleMaterial;
    private StandardMaterial3D _lightHeadMaterial;

    public override void _Ready()
    {
        if (Seed >= 0)
            _rng.Seed = (ulong)Seed;
        else
            _rng.Randomize();

        CreateMaterials();
        GenerateRoadSurface();
        GenerateLaneMarkers();
        GenerateCurbs();
        GenerateTrackLights();

        LoadBuildingScenes();
        PlaceBuildings();
        PlaceDecorations();
    }

    private void LoadBuildingScenes()
    {
        var dir = DirAccess.Open("res://assets/kenney_industrial");
        if (dir == null) return;

        var buildingFiles = new List<string>();
        var decorationFiles = new List<string>();

        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != "")
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
            {
                if (fileName.StartsWith("building-", StringComparison.OrdinalIgnoreCase))
                    buildingFiles.Add(fileName);
                else
                    decorationFiles.Add(fileName);
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();

        buildingFiles.Sort(StringComparer.OrdinalIgnoreCase);
        decorationFiles.Sort(StringComparer.OrdinalIgnoreCase);

        _buildingScenes = LoadScenes(buildingFiles).ToArray();
        _decorationScenes = decorationFiles.Count > 0 ? LoadScenes(decorationFiles).ToArray() : _buildingScenes;
    }

    private static IEnumerable<PackedScene> LoadScenes(IEnumerable<string> fileNames)
    {
        foreach (string fileName in fileNames)
        {
            var scene = GD.Load<PackedScene>($"res://assets/kenney_industrial/{fileName}");
            if (scene != null)
                yield return scene;
        }
    }

    private void CreateMaterials()
    {
        _roadMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.025f, 0.029f, 0.034f),
            Roughness = 0.92f
        };

        _laneMarkerMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.82f, 0.22f),
            Roughness = 0.8f
        };

        _curbRedMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.78f, 0.08f, 0.06f),
            Roughness = 0.82f
        };

        _curbWhiteMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.93f, 0.9f, 0.82f),
            Roughness = 0.82f
        };

        _lightPoleMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.13f, 0.14f),
            Roughness = 0.65f
        };

        _lightHeadMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.84f, 0.42f),
            Roughness = 0.35f
        };
    }

    private void GenerateRoadSurface()
    {
        int segmentCount = Math.Max(16, RoadSegmentCount);
        float segmentLength = Mathf.Tau * TrackRadius / segmentCount * 1.02f;
        var roadSegmentMesh = new BoxMesh { Size = new Vector3(segmentLength, 0.04f, TrackWidth) };

        for (int i = 0; i < segmentCount; i++)
        {
            float angle = Mathf.Tau * i / segmentCount;
            var segment = new MeshInstance3D
            {
                Mesh = roadSegmentMesh,
                MaterialOverride = _roadMaterial,
                Transform = TrackTransform(TrackRadius, angle, 0.025f)
            };

            AddChild(segment);
            segment.Name = $"RoadSegment{i:000}";
        }
    }

    private void GenerateLaneMarkers()
    {
        int markerCount = Math.Max(8, LaneMarkerCount);
        float angleStep = Mathf.Tau / markerCount;
        var dashMesh = new BoxMesh { Size = new Vector3(3.0f, 0.035f, 0.24f) };

        for (int i = 0; i < markerCount; i += 2)
        {
            float angle = angleStep * i;
            var dash = new MeshInstance3D
            {
                Mesh = dashMesh,
                MaterialOverride = _laneMarkerMaterial,
                Transform = TrackTransform(TrackRadius, angle, 0.065f)
            };

            AddChild(dash);
            dash.Name = $"LaneMarker{i:000}";
        }
    }

    private void GenerateCurbs()
    {
        int curbCount = Math.Max(24, RoadSegmentCount / 2);
        float angleStep = Mathf.Tau / curbCount;
        float innerRadius = TrackRadius - TrackWidth * 0.5f - 0.35f;
        float outerRadius = TrackRadius + TrackWidth * 0.5f + 0.35f;
        var curbMesh = new BoxMesh { Size = new Vector3(3.4f, 0.22f, 0.6f) };

        for (int i = 0; i < curbCount; i++)
        {
            float angle = angleStep * i;
            var material = i % 2 == 0 ? _curbWhiteMaterial : _curbRedMaterial;

            AddCurb(curbMesh, material, innerRadius, angle, $"CurbMarkerInner{i:000}");
            AddCurb(curbMesh, material, outerRadius, angle, $"CurbMarkerOuter{i:000}");
        }
    }

    private void AddCurb(BoxMesh curbMesh, Material material, float radius, float angle, string markerName)
    {
        var curb = new MeshInstance3D
        {
            Mesh = curbMesh,
            MaterialOverride = material,
            Transform = TrackTransform(radius, angle, 0.105f)
        };

        AddChild(curb);
        curb.Name = markerName;
    }

    private void GenerateTrackLights()
    {
        int lightCount = Math.Max(0, TrackLightCount);
        if (lightCount == 0) return;

        float angleStep = Mathf.Tau / lightCount;
        float radius = TrackRadius + TrackWidth * 0.5f + ShoulderWidth * 0.55f;
        var poleMesh = new BoxMesh { Size = new Vector3(0.22f, 5.2f, 0.22f) };
        var headMesh = new BoxMesh { Size = new Vector3(1.1f, 0.3f, 0.55f) };

        for (int i = 0; i < lightCount; i++)
        {
            float angle = angleStep * i;
            Vector3 radial = Radial(angle);
            Vector3 position = radial * radius;
            Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0.0f, Mathf.Cos(angle));
            Basis basis = new Basis(tangent, Vector3.Up, -radial).Orthonormalized();

            var pole = new MeshInstance3D
            {
                Mesh = poleMesh,
                MaterialOverride = _lightPoleMaterial,
                Transform = new Transform3D(basis, position + Vector3.Up * 2.6f)
            };
            AddChild(pole);
            pole.Name = $"TrackLightPole{i:000}";

            var head = new MeshInstance3D
            {
                Mesh = headMesh,
                MaterialOverride = _lightHeadMaterial,
                Transform = new Transform3D(basis, position + Vector3.Up * 5.35f - radial * 0.85f)
            };
            AddChild(head);
            head.Name = $"TrackLightHead{i:000}";

            var light = new OmniLight3D
            {
                LightColor = new Color(1.0f, 0.82f, 0.52f),
                LightEnergy = TrackLightEnergy,
                OmniRange = TrackLightRange,
                ShadowEnabled = false,
                Position = position + Vector3.Up * 4.9f - radial * 1.6f
            };
            AddChild(light);
            light.Name = $"TrackLightGlow{i:000}";
        }
    }

    private void PlaceBuildings()
    {
        if (_buildingScenes.Length == 0) return;

        int perSideCount = Math.Max(1, BuildingCount / 2);
        float angleStep = Mathf.Tau / perSideCount;

        for (int i = 0; i < perSideCount; i++)
        {
            float angle = angleStep * i + _rng.RandfRange(-0.05f, 0.05f);

            PlaceBuilding(angle, -1.0f, $"BuildingInner{i:000}");
            PlaceBuilding(angle + angleStep * 0.5f, 1.0f, $"BuildingOuter{i:000}");
        }
    }

    private void PlaceBuilding(float angle, float side, string buildingName)
    {
        float radius = TrackRadius + side * (TrackWidth * 0.5f + ShoulderWidth + BuildingSetback + _rng.RandfRange(0.0f, BuildingJitter));
        var scene = _buildingScenes[_rng.RandiRange(0, _buildingScenes.Length - 1)];
        var building = scene.Instantiate<Node3D>();
        Vector3 radial = Radial(angle);

        building.Position = radial * radius;
        building.Rotation = new Vector3(0.0f, -angle + Mathf.Pi * 0.5f + _rng.RandfRange(-0.2f, 0.2f), 0.0f);
        ScaleAndGroundNode(building, RandomRangeOrdered(BuildingFootprintMin, BuildingFootprintMax));

        AddChild(building);
        building.Name = buildingName;
    }

    private void PlaceDecorations()
    {
        if (_decorationScenes.Length == 0) return;

        for (int i = 0; i < DecorationCount; i++)
        {
            float angle = _rng.RandfRange(0, Mathf.Tau);
            float side = _rng.RandfRange(0, 1) > 0.5f ? 1.0f : -1.0f;
            float radius = TrackRadius + side * _rng.RandfRange(TrackWidth * 0.5f + ShoulderWidth + 6.0f, TrackWidth * 0.5f + ShoulderWidth + 22.0f);

            var scene = _decorationScenes[_rng.RandiRange(0, _decorationScenes.Length - 1)];
            var deco = scene.Instantiate<Node3D>();
            deco.Position = Radial(angle) * radius;
            deco.RotateY(_rng.RandfRange(0, Mathf.Tau));
            ScaleAndGroundNode(deco, RandomRangeOrdered(DecorationFootprintMin, DecorationFootprintMax));
            AddChild(deco);
            deco.Name = $"Decoration{i:000}";
        }
    }

    private float RandomRangeOrdered(float min, float max)
    {
        float orderedMin = Mathf.Min(min, max);
        float orderedMax = Mathf.Max(min, max);
        return _rng.RandfRange(orderedMin, orderedMax);
    }

    private static void ScaleAndGroundNode(Node3D node, float targetFootprint)
    {
        if (targetFootprint <= 0.0f) return;

        if (TryGetLocalVisualBounds(node, out Aabb bounds) == false) return;

        float footprint = Mathf.Max(bounds.Size.X, bounds.Size.Z);
        if (footprint <= 0.001f) return;

        float scale = targetFootprint / footprint;
        node.Scale *= scale;
        node.Position += Vector3.Up * -bounds.Position.Y * scale;
    }

    private static bool TryGetLocalVisualBounds(Node root, out Aabb bounds)
    {
        bool hasBounds = false;
        bounds = new Aabb();
        AccumulateVisualBounds(root, Transform3D.Identity, ref bounds, ref hasBounds);
        return hasBounds;
    }

    private static void AccumulateVisualBounds(Node node, Transform3D parentTransform, ref Aabb bounds, ref bool hasBounds)
    {
        Transform3D localTransform = parentTransform;
        if (node is Node3D node3D)
            localTransform = parentTransform * node3D.Transform;

        if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null)
        {
            Aabb transformedBounds = TransformAabb(localTransform, meshInstance.GetAabb());
            if (hasBounds)
                bounds = bounds.Merge(transformedBounds);
            else
            {
                bounds = transformedBounds;
                hasBounds = true;
            }
        }

        foreach (Node child in node.GetChildren())
            AccumulateVisualBounds(child, localTransform, ref bounds, ref hasBounds);
    }

    private static Aabb TransformAabb(Transform3D transform, Aabb aabb)
    {
        Vector3 start = aabb.Position;
        Vector3 end = aabb.Position + aabb.Size;
        Vector3 min = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        for (int x = 0; x <= 1; x++)
        {
            for (int y = 0; y <= 1; y++)
            {
                for (int z = 0; z <= 1; z++)
                {
                    Vector3 point = transform * new Vector3(
                        x == 0 ? start.X : end.X,
                        y == 0 ? start.Y : end.Y,
                        z == 0 ? start.Z : end.Z
                    );

                    min = new Vector3(
                        Mathf.Min(min.X, point.X),
                        Mathf.Min(min.Y, point.Y),
                        Mathf.Min(min.Z, point.Z)
                    );
                    max = new Vector3(
                        Mathf.Max(max.X, point.X),
                        Mathf.Max(max.Y, point.Y),
                        Mathf.Max(max.Z, point.Z)
                    );
                }
            }
        }

        return new Aabb(min, max - min);
    }

    private static Vector3 Radial(float angle)
    {
        return new Vector3(Mathf.Cos(angle), 0.0f, Mathf.Sin(angle));
    }

    private static Transform3D TrackTransform(float radius, float angle, float y)
    {
        Vector3 radial = Radial(angle);
        Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0.0f, Mathf.Cos(angle));
        return new Transform3D(new Basis(tangent, Vector3.Up, radial), radial * radius + Vector3.Up * y);
    }
}
