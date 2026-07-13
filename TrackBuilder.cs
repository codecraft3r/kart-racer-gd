using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class TrackBuilder : Node3D
{
    public static TrackBuilder Instance { get; private set; }

    [Export] public int CityColumns = 5;
    [Export] public int CityRows = 4;
    [Export] public float CityBlockSize = 30.0f;
    [Export] public float CityBlockSizeJitter = 5.0f;
    [Export] public float MainAvenueWidthMultiplier = 1.35f;
    [Export] public float SideStreetMinWidthMultiplier = 0.82f;
    [Export] public float SideStreetMaxWidthMultiplier = 1.05f;
    [Export] public int BuildingCount = 72;
    [Export] public int BuildingsPerBlockMax = 4;
    [Export] public float TrackWidth = 14.0f;
    [Export] public int DecorationCount = 22;
    [Export] public int TrackLightCount = 16;
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
    private StandardMaterial3D _lightHeadAltMaterial;
    private StandardMaterial3D _intersectionMaterial;
    private StandardMaterial3D _crosswalkMaterial;
    private float[] _verticalStreetCenters = Array.Empty<float>();
    private float[] _horizontalStreetCenters = Array.Empty<float>();
    private float[] _verticalStreetWidths = Array.Empty<float>();
    private float[] _horizontalStreetWidths = Array.Empty<float>();
    private float[] _blockWidths = Array.Empty<float>();
    private float[] _blockDepths = Array.Empty<float>();
    private readonly List<Vector3> _intersectionPositions = new();

    public IReadOnlyList<Vector3> IntersectionPositions => _intersectionPositions;


    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Ready()
    {
        Instance = this;
        bool isNetworked = Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;
        if (isNetworked && MultiplayerManager.Instance != null)
        {
            _rng.Seed = (ulong)MultiplayerManager.Instance.SyncedSeed;
            GD.Print($"TrackBuilder: Seeding generation with synced seed: {MultiplayerManager.Instance.SyncedSeed}");
        }
        else if (Seed >= 0)
        {
            _rng.Seed = (ulong)Seed;
            GD.Print($"TrackBuilder: Seeding generation with inspector seed: {Seed}");
        }
        else
        {
            _rng.Randomize();
            GD.Print($"TrackBuilder: Seeding generation with random seed: {_rng.Seed}");
        }

        CreateMaterials();
        BuildCityLayout();
        GenerateRoadSurface();
        GenerateIntersections();
        GenerateDepot();
        GenerateWorldBounds();
        GenerateLaneMarkers();
        GenerateCurbs();
        GenerateCrosswalks();
        GenerateTrackLights();

        LoadBuildingScenes();
        PlaceBuildings();
        PlaceDecorations();
        GenerateNeonLandmarks();
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
            AlbedoColor = new Color(0.038f, 0.043f, 0.088f),
            Roughness = 0.9f,
            Metallic = 0.06f
        };

        _laneMarkerMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.72f, 0.12f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.48f, 0.04f) * 0.55f,
            Roughness = 0.8f
        };

        _curbRedMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.02f, 0.46f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.0f, 0.38f) * 0.42f,
            Roughness = 0.82f
        };

        _curbWhiteMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.58f, 0.96f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.0f, 0.78f, 1.0f) * 0.36f,
            Roughness = 0.82f
        };

        _lightPoleMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.12f, 0.13f, 0.14f),
            Roughness = 0.65f
        };

        _lightHeadMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.68f, 0.18f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.38f, 0.05f),
            Roughness = 0.35f
        };

        _lightHeadAltMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.1f, 0.9f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.0f, 0.72f, 1.0f),
            Roughness = 0.3f
        };

        _intersectionMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.04f, 0.038f, 0.09f),
            Roughness = 0.86f,
            Metallic = 0.08f
        };

        _crosswalkMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.7f, 0.95f, 1.0f),
            EmissionEnabled = true,
            Emission = new Color(0.15f, 0.65f, 0.85f) * 0.26f,
            Roughness = 0.82f
        };
    }

    private void GenerateRoadSurface()
    {
        int columns = CityColumnCount();
        int rows = CityRowCount();

        for (int row = 0; row <= rows; row++)
        {
            var roadMesh = new BoxMesh { Size = new Vector3(CityWidth(), 0.04f, HorizontalStreetWidth(row)) };
            var road = new MeshInstance3D
            {
                Mesh = roadMesh,
                MaterialOverride = _roadMaterial,
                Position = new Vector3(CityCenterX(), 0.025f, HorizontalStreetCoordinate(row))
            };

            AddChild(road);
            road.Name = $"RoadSegmentHorizontal{row:000}";
        }

        for (int column = 0; column <= columns; column++)
        {
            var roadMesh = new BoxMesh { Size = new Vector3(VerticalStreetWidth(column), 0.045f, CityDepth()) };
            var road = new MeshInstance3D
            {
                Mesh = roadMesh,
                MaterialOverride = _roadMaterial,
                Position = new Vector3(VerticalStreetCoordinate(column), 0.03f, CityCenterZ())
            };

            AddChild(road);
            road.Name = $"RoadSegmentVertical{column:000}";
        }
    }

    private void GenerateIntersections()
    {
        int columns = CityColumnCount();
        int rows = CityRowCount();
        _intersectionPositions.Clear();

        for (int column = 0; column <= columns; column++)
        {
            for (int row = 0; row <= rows; row++)
            {
                Vector3 pos = new Vector3(VerticalStreetCoordinate(column), 0.06f, HorizontalStreetCoordinate(row));
                var intersectionMesh = new BoxMesh { Size = new Vector3(VerticalStreetWidth(column) * 1.08f, 0.055f, HorizontalStreetWidth(row) * 1.08f) };
                var intersection = new MeshInstance3D
                {
                    Mesh = intersectionMesh,
                    MaterialOverride = _intersectionMaterial,
                    Position = pos
                };

                AddChild(intersection);
                intersection.Name = $"Intersection{column:00}_{row:00}";
                _intersectionPositions.Add(pos);
            }
        }
    }

    public Transform3D GetSpawnTransform(int slot)
    {
        Vector3 depot = GetDepotPosition();
        float lateralOffset = (slot - 1) * 3.4f;
        Vector3 position = depot + Vector3.Back * 6.0f + Vector3.Right * lateralOffset + Vector3.Up * 0.65f;
        return new Transform3D(Basis.Identity, position);
    }

    public Vector3 GetDepotPosition()
    {
        if (_intersectionPositions.Count == 0)
            return new Vector3(0.0f, 0.1f, 0.0f);

        Vector3 nearest = _intersectionPositions[0];
        float nearestDistance = nearest.LengthSquared();
        foreach (Vector3 position in _intersectionPositions)
        {
            float distance = position.LengthSquared();
            if (distance < nearestDistance)
            {
                nearest = position;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    public Vector3 GetNearestIntersection(Vector3 position)
    {
        if (_intersectionPositions.Count == 0)
            return position;

        Vector3 nearest = _intersectionPositions[0];
        float nearestDistance = position.DistanceSquaredTo(nearest);
        foreach (Vector3 intersection in _intersectionPositions)
        {
            float distance = position.DistanceSquaredTo(intersection);
            if (distance < nearestDistance)
            {
                nearest = intersection;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    public List<Vector3> BuildStreetRoute(Vector3 from, Vector3 to)
    {
        var route = new List<Vector3>();
        if (_intersectionPositions.Count == 0)
        {
            route.Add(to);
            return route;
        }

        (int startColumn, int startRow) = GetNearestIntersectionCoordinates(from);
        (int targetColumn, int targetRow) = GetNearestIntersectionCoordinates(to);
        int column = startColumn;
        int row = startRow;

        bool travelRowsFirst = Mathf.Abs(from.Z - HorizontalStreetCoordinate(startRow)) <=
            Mathf.Abs(from.X - VerticalStreetCoordinate(startColumn));

        if (travelRowsFirst)
        {
            AppendRowRoute(route, column, ref row, targetRow);
            AppendColumnRoute(route, row, ref column, targetColumn);
        }
        else
        {
            AppendColumnRoute(route, row, ref column, targetColumn);
            AppendRowRoute(route, column, ref row, targetRow);
        }

        if (route.Count == 0 || route[^1].DistanceSquaredTo(to) > 0.25f)
            route.Add(new Vector3(to.X, 0.1f, to.Z));

        return route;
    }

    private (int Column, int Row) GetNearestIntersectionCoordinates(Vector3 position)
    {
        int nearestColumn = 0;
        int nearestRow = 0;
        float nearestDistance = float.MaxValue;

        for (int column = 0; column < _verticalStreetCenters.Length; column++)
        {
            for (int row = 0; row < _horizontalStreetCenters.Length; row++)
            {
                Vector3 intersection = new(VerticalStreetCoordinate(column), 0.1f, HorizontalStreetCoordinate(row));
                float distance = position.DistanceSquaredTo(intersection);
                if (distance < nearestDistance)
                {
                    nearestColumn = column;
                    nearestRow = row;
                    nearestDistance = distance;
                }
            }
        }

        return (nearestColumn, nearestRow);
    }

    private void AppendRowRoute(List<Vector3> route, int column, ref int row, int targetRow)
    {
        int direction = Math.Sign(targetRow - row);
        while (row != targetRow)
        {
            row += direction;
            route.Add(new Vector3(VerticalStreetCoordinate(column), 0.1f, HorizontalStreetCoordinate(row)));
        }
    }

    private void AppendColumnRoute(List<Vector3> route, int row, ref int column, int targetColumn)
    {
        int direction = Math.Sign(targetColumn - column);
        while (column != targetColumn)
        {
            column += direction;
            route.Add(new Vector3(VerticalStreetCoordinate(column), 0.1f, HorizontalStreetCoordinate(row)));
        }
    }

    private void GenerateDepot()
    {
        Vector3 depotPosition = GetDepotPosition();
        var depot = new Node3D { Name = "TaxiDepot", Position = depotPosition };
        AddChild(depot);

        var padMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.02f, 0.08f, 0.11f),
            EmissionEnabled = true,
            Emission = new Color(0.0f, 0.38f, 0.5f),
            Roughness = 0.75f
        };
        var accentMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.0f, 0.5f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.0f, 0.5f) * 0.75f
        };
        var headerMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.72f, 0.08f),
            EmissionEnabled = true,
            Emission = new Color(1.0f, 0.38f, 0.03f) * 0.65f
        };

        depot.AddChild(new MeshInstance3D
        {
            Name = "DepotPad",
            Mesh = new TorusMesh { InnerRadius = 7.0f, OuterRadius = 8.3f, Rings = 8, RingSegments = 32 },
            MaterialOverride = padMaterial,
            Position = Vector3.Up * 0.12f
        });

        for (int side = -1; side <= 1; side += 2)
        {
            depot.AddChild(new MeshInstance3D
            {
                Name = side < 0 ? "DepotPostLeft" : "DepotPostRight",
                Mesh = new BoxMesh { Size = new Vector3(0.28f, 4.8f, 0.28f) },
                MaterialOverride = accentMaterial,
                Position = new Vector3(side * 8.2f, 2.4f, 8.0f)
            });
        }

        depot.AddChild(new MeshInstance3D
        {
            Name = "DepotHeader",
            Mesh = new BoxMesh { Size = new Vector3(16.7f, 0.38f, 0.38f) },
            MaterialOverride = headerMaterial,
            Position = new Vector3(0.0f, 4.8f, 8.0f)
        });

    }

    private void GenerateWorldBounds()
    {
        const float wallThickness = 2.0f;
        const float wallHeight = 5.0f;
        float width = CityWidth() + 10.0f;
        float depth = CityDepth() + 10.0f;
        float centerX = CityCenterX();
        float centerZ = CityCenterZ();

        AddWorldCollider(
            "WorldBoundaryNorth",
            new Vector3(width, wallHeight, wallThickness),
            new Vector3(centerX, wallHeight * 0.5f, CityMinZ() - 5.0f));
        AddWorldCollider(
            "WorldBoundarySouth",
            new Vector3(width, wallHeight, wallThickness),
            new Vector3(centerX, wallHeight * 0.5f, CityMaxZ() + 5.0f));
        AddWorldCollider(
            "WorldBoundaryWest",
            new Vector3(wallThickness, wallHeight, depth),
            new Vector3(CityMinX() - 5.0f, wallHeight * 0.5f, centerZ));
        AddWorldCollider(
            "WorldBoundaryEast",
            new Vector3(wallThickness, wallHeight, depth),
            new Vector3(CityMaxX() + 5.0f, wallHeight * 0.5f, centerZ));
    }

    private void AddWorldCollider(string name, Vector3 size, Vector3 position)
    {
        var body = new StaticBody3D { Name = name, Position = position, CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
    }

    private void GenerateLaneMarkers()
    {
        int columns = CityColumnCount();
        int rows = CityRowCount();
        int markerIndex = 0;

        foreach (float z in _horizontalStreetCenters)
        {
            AddLaneDashes(true, z, CityMinX(), CityMaxX(), _verticalStreetCenters, _verticalStreetWidths, ref markerIndex);
        }

        foreach (float x in _verticalStreetCenters)
        {
            AddLaneDashes(false, x, CityMinZ(), CityMaxZ(), _horizontalStreetCenters, _horizontalStreetWidths, ref markerIndex);
        }
    }

    private void GenerateCurbs()
    {
        int columns = CityColumnCount();
        int rows = CityRowCount();
        int curbIndex = 0;

        for (int row = 0; row <= rows; row++)
        {
            float z = HorizontalStreetCoordinate(row);
            float streetHalfWidth = HorizontalStreetWidth(row) * 0.5f;
            for (int column = 0; column < columns; column++)
            {
                float x0 = VerticalStreetCoordinate(column) + VerticalStreetWidth(column) * 0.5f + 0.25f;
                float x1 = VerticalStreetCoordinate(column + 1) - VerticalStreetWidth(column + 1) * 0.5f - 0.25f;
                float length = Mathf.Max(0.1f, x1 - x0);
                float centerX = (x0 + x1) * 0.5f;

                AddCurbSegment(new Vector3(length, 0.22f, 0.6f), new Vector3(centerX, 0.105f, z - streetHalfWidth - 0.35f), $"CurbMarkerHorizontal{curbIndex++:000}");
                AddCurbSegment(new Vector3(length, 0.22f, 0.6f), new Vector3(centerX, 0.105f, z + streetHalfWidth + 0.35f), $"CurbMarkerHorizontal{curbIndex++:000}");
            }
        }

        for (int column = 0; column <= columns; column++)
        {
            float x = VerticalStreetCoordinate(column);
            float streetHalfWidth = VerticalStreetWidth(column) * 0.5f;
            for (int row = 0; row < rows; row++)
            {
                float z0 = HorizontalStreetCoordinate(row) + HorizontalStreetWidth(row) * 0.5f + 0.25f;
                float z1 = HorizontalStreetCoordinate(row + 1) - HorizontalStreetWidth(row + 1) * 0.5f - 0.25f;
                float length = Mathf.Max(0.1f, z1 - z0);
                float centerZ = (z0 + z1) * 0.5f;

                AddCurbSegment(new Vector3(0.6f, 0.22f, length), new Vector3(x - streetHalfWidth - 0.35f, 0.105f, centerZ), $"CurbMarkerVertical{curbIndex++:000}");
                AddCurbSegment(new Vector3(0.6f, 0.22f, length), new Vector3(x + streetHalfWidth + 0.35f, 0.105f, centerZ), $"CurbMarkerVertical{curbIndex++:000}");
            }
        }
    }

    private void GenerateCrosswalks()
    {
        int columns = CityColumnCount();
        int rows = CityRowCount();
        int crosswalkIndex = 0;

        for (int column = 0; column <= columns; column++)
        {
            float x = VerticalStreetCoordinate(column);
            float verticalHalfWidth = VerticalStreetWidth(column) * 0.5f;
            for (int row = 0; row <= rows; row++)
            {
                float z = HorizontalStreetCoordinate(row);
                float horizontalHalfWidth = HorizontalStreetWidth(row) * 0.5f;
                AddCrosswalk(new Vector3(0.18f, 0.03f, HorizontalStreetWidth(row) * 0.75f), new Vector3(x - verticalHalfWidth * 0.72f, 0.09f, z), $"Crosswalk{crosswalkIndex++:000}");
                AddCrosswalk(new Vector3(0.18f, 0.03f, HorizontalStreetWidth(row) * 0.75f), new Vector3(x + verticalHalfWidth * 0.72f, 0.09f, z), $"Crosswalk{crosswalkIndex++:000}");
                AddCrosswalk(new Vector3(VerticalStreetWidth(column) * 0.75f, 0.03f, 0.18f), new Vector3(x, 0.09f, z - horizontalHalfWidth * 0.72f), $"Crosswalk{crosswalkIndex++:000}");
                AddCrosswalk(new Vector3(VerticalStreetWidth(column) * 0.75f, 0.03f, 0.18f), new Vector3(x, 0.09f, z + horizontalHalfWidth * 0.72f), $"Crosswalk{crosswalkIndex++:000}");
            }
        }
    }

    private void GenerateTrackLights()
    {
        int lightCount = Math.Max(0, TrackLightCount);
        if (lightCount == 0) return;

        int columns = CityColumnCount();
        int rows = CityRowCount();
        var poleMesh = new BoxMesh { Size = new Vector3(0.22f, 5.2f, 0.22f) };
        var headMesh = new BoxMesh { Size = new Vector3(1.1f, 0.3f, 0.55f) };
        var candidates = new List<LightCandidate>();

        for (int column = 0; column <= columns; column++)
        {
            float x = VerticalStreetCoordinate(column);
            float xOffset = VerticalStreetWidth(column) * 0.5f + 1.5f;
            for (int row = 0; row <= rows; row++)
            {
                float z = HorizontalStreetCoordinate(row);
                float zOffset = HorizontalStreetWidth(row) * 0.5f + 1.5f;
                AddLightCandidate(candidates, new Vector3(x - xOffset, 0.0f, z - zOffset), new Vector3(1.0f, 0.0f, 1.0f));
                AddLightCandidate(candidates, new Vector3(x + xOffset, 0.0f, z - zOffset), new Vector3(-1.0f, 0.0f, 1.0f));
                AddLightCandidate(candidates, new Vector3(x - xOffset, 0.0f, z + zOffset), new Vector3(1.0f, 0.0f, -1.0f));
                AddLightCandidate(candidates, new Vector3(x + xOffset, 0.0f, z + zOffset), new Vector3(-1.0f, 0.0f, -1.0f));
            }
        }

        for (int i = 0; i < lightCount; i++)
        {
            LightCandidate candidate = candidates[Mathf.Clamp(i * candidates.Count / lightCount, 0, candidates.Count - 1)];
            Vector3 position = candidate.Position;
            Vector3 forward = candidate.Forward.Normalized();
            Vector3 right = Vector3.Up.Cross(forward).Normalized();
            Basis basis = new Basis(right, Vector3.Up, forward).Orthonormalized();

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
                MaterialOverride = i % 3 == 0 ? _lightHeadAltMaterial : _lightHeadMaterial,
                Transform = new Transform3D(basis, position + Vector3.Up * 5.35f + forward * 0.85f)
            };
            AddChild(head);
            head.Name = $"TrackLightHead{i:000}";

            var light = new OmniLight3D
            {
                LightColor = i % 3 == 0 ? new Color(0.08f, 0.76f, 1.0f) : new Color(1.0f, 0.5f, 0.16f),
                LightEnergy = TrackLightEnergy * (i % 3 == 0 ? 0.82f : 1.0f),
                OmniRange = TrackLightRange,
                ShadowEnabled = false,
                Position = position + Vector3.Up * 4.9f + forward * 1.6f
            };
            AddChild(light);
            light.Name = $"TrackLightGlow{i:000}";
        }
    }

    private void GenerateNeonLandmarks()
    {
        string[] messages = { "PAIN", "24H CAB", "NO BRAKES", "FARE//FASTER", "RUSH", "DOWNTOWN" };
        Color[] colors =
        {
            new Color(1.0f, 0.02f, 0.48f),
            new Color(0.0f, 0.88f, 1.0f),
            new Color(1.0f, 0.72f, 0.1f)
        };

        int columns = CityColumnCount();
        int rows = CityRowCount();
        for (int i = 0; i < messages.Length; i++)
        {
            int column = i * 2 % columns;
            int row = (i * 3 + 1) % rows;
            Vector3 center = BlockCenter(column, row);
            var sign = new Label3D
            {
                Name = $"NeonLandmark{i:00}",
                Text = messages[i],
                Font = GD.Load<Font>("res://assets/fonts/VT323-Regular.ttf"),
                FontSize = 72,
                PixelSize = 0.018f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = false,
                Modulate = colors[i % colors.Length],
                OutlineModulate = new Color(0.025f, 0.01f, 0.06f),
                OutlineSize = 16,
                Position = center + Vector3.Up * (7.5f + i % 2 * 2.0f)
            };
            AddChild(sign);
        }
    }

    private void PlaceBuildings()
    {
        if (_buildingScenes.Length == 0) return;

        int columns = CityColumnCount();
        int rows = CityRowCount();
        int slotsPerBlock = Math.Max(1, BuildingsPerBlockMax);
        int maxBuildings = columns * rows * slotsPerBlock;
        int targetBuildingCount = Mathf.Min(Math.Max(0, BuildingCount), maxBuildings);
        int buildingIndex = 0;

        for (int row = 0; row < rows && buildingIndex < targetBuildingCount; row++)
        {
            for (int column = 0; column < columns && buildingIndex < targetBuildingCount; column++)
            {
                for (int slot = 0; slot < slotsPerBlock && buildingIndex < targetBuildingCount; slot++)
                {
                    PlaceBuildingInBlock(column, row, slot, buildingIndex);
                    buildingIndex++;
                }
            }
        }
    }

    private void PlaceBuildingInBlock(int column, int row, int slot, int buildingIndex)
    {
        var scene = _buildingScenes[_rng.RandiRange(0, _buildingScenes.Length - 1)];
        var building = scene.Instantiate<Node3D>();
        Vector3 blockCenter = BlockCenter(column, row);
        Vector3 localOffset = BuildingSlotOffset(column, row, slot);

        building.Position = blockCenter + localOffset;
        building.Rotation = new Vector3(0.0f, OrthogonalRotation() + _rng.RandfRange(-0.06f, 0.06f), 0.0f);
        ScaleAndGroundNode(building, RandomRangeOrdered(BuildingFootprintMin, BuildingFootprintMax));

        AddChild(building);
        building.Name = $"BuildingBlock{column:00}_{row:00}_{slot:00}_{buildingIndex:000}";
        AddBuildingCollision(building, buildingIndex);
    }

    private void AddBuildingCollision(Node3D building, int buildingIndex)
    {
        if (!TryGetLocalVisualBounds(building, out Aabb bounds))
            return;

        Vector3 size = bounds.Size;
        size.X = Mathf.Max(1.0f, size.X * 0.86f);
        size.Y = Mathf.Max(1.5f, size.Y);
        size.Z = Mathf.Max(1.0f, size.Z * 0.86f);

        var body = new StaticBody3D
        {
            Name = $"WorldCollider{buildingIndex:000}",
            Position = bounds.GetCenter(),
            CollisionLayer = 1,
            CollisionMask = 0
        };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        AddChild(body);
    }

    private void PlaceDecorations()
    {
        if (_decorationScenes.Length == 0) return;
        int columns = CityColumnCount();
        int rows = CityRowCount();

        for (int i = 0; i < DecorationCount; i++)
        {
            int column = _rng.RandiRange(0, columns - 1);
            int row = _rng.RandiRange(0, rows - 1);
            Vector3 blockCenter = BlockCenter(column, row);
            Vector2 blockSize = BlockInteriorSize(column, row);

            var scene = _decorationScenes[_rng.RandiRange(0, _decorationScenes.Length - 1)];
            var deco = scene.Instantiate<Node3D>();
            deco.Position = blockCenter + new Vector3(
                _rng.RandfRange(-blockSize.X * 0.34f, blockSize.X * 0.34f),
                0.0f,
                _rng.RandfRange(-blockSize.Y * 0.34f, blockSize.Y * 0.34f)
            );
            deco.Rotation = new Vector3(0.0f, OrthogonalRotation() + _rng.RandfRange(-0.12f, 0.12f), 0.0f);
            ScaleAndGroundNode(deco, RandomRangeOrdered(DecorationFootprintMin, DecorationFootprintMax));
            AddChild(deco);
            deco.Name = $"Decoration{i:000}";
        }
    }

    private void BuildCityLayout()
    {
        int columns = CityColumnCount();
        int rows = CityRowCount();

        _blockWidths = BuildBlockSizes(columns);
        _blockDepths = BuildBlockSizes(rows);
        _verticalStreetWidths = BuildStreetWidths(columns + 1, columns);
        _horizontalStreetWidths = BuildStreetWidths(rows + 1, rows);
        _verticalStreetCenters = BuildStreetCenters(_blockWidths, _verticalStreetWidths);
        _horizontalStreetCenters = BuildStreetCenters(_blockDepths, _horizontalStreetWidths);
    }

    private float[] BuildBlockSizes(int blockCount)
    {
        var sizes = new float[blockCount];
        float jitter = Mathf.Max(0.0f, CityBlockSizeJitter);

        for (int i = 0; i < blockCount; i++)
        {
            float centerBias = 1.0f - 0.08f * DowntownWeight(i, blockCount);
            sizes[i] = Mathf.Max(16.0f, CityBlockSize * centerBias + _rng.RandfRange(-jitter, jitter));
        }

        return sizes;
    }

    private float[] BuildStreetWidths(int streetCount, int blockCount)
    {
        var widths = new float[streetCount];
        float sideMin = Mathf.Min(SideStreetMinWidthMultiplier, SideStreetMaxWidthMultiplier);
        float sideMax = Mathf.Max(SideStreetMinWidthMultiplier, SideStreetMaxWidthMultiplier);

        for (int i = 0; i < streetCount; i++)
        {
            bool isPerimeter = i == 0 || i == streetCount - 1;
            bool isMainAvenue = IsPrimaryAvenue(i, blockCount);
            float multiplier = isMainAvenue
                ? MainAvenueWidthMultiplier
                : _rng.RandfRange(sideMin, sideMax);

            if (isPerimeter)
                multiplier = Mathf.Max(multiplier, 1.05f);

            widths[i] = Mathf.Max(8.0f, TrackWidth * multiplier);
        }

        return widths;
    }

    private static float[] BuildStreetCenters(float[] blockSizes, float[] streetWidths)
    {
        var centers = new float[streetWidths.Length];

        for (int i = 1; i < streetWidths.Length; i++)
        {
            centers[i] = centers[i - 1]
                + streetWidths[i - 1] * 0.5f
                + blockSizes[i - 1]
                + streetWidths[i] * 0.5f;
        }

        float minEdge = centers[0] - streetWidths[0] * 0.5f;
        float maxEdge = centers[^1] + streetWidths[^1] * 0.5f;
        float centerOffset = (minEdge + maxEdge) * 0.5f;

        for (int i = 0; i < centers.Length; i++)
            centers[i] -= centerOffset;

        return centers;
    }

    private bool IsPrimaryAvenue(int streetIndex, int blockCount)
    {
        float cityMid = blockCount * 0.5f;
        return Mathf.Abs(streetIndex - cityMid) < 0.75f;
    }

    private float DowntownWeight(int index, int count)
    {
        if (count <= 1) return 1.0f;

        float center = (count - 1) * 0.5f;
        float distance = Mathf.Abs(index - center) / Mathf.Max(1.0f, center);
        return 1.0f - Mathf.Clamp(distance, 0.0f, 1.0f);
    }

    private void AddLaneDashes(bool horizontal, float fixedCoordinate, float start, float end, float[] crossingCenters, float[] crossingWidths, ref int markerIndex)
    {
        float dashSpacing = 8.0f;
        float dashLength = horizontal ? 3.2f : 3.0f;

        for (float position = start + dashSpacing * 0.5f; position < end; position += dashSpacing)
        {
            if (IsNearIntersection(position, crossingCenters, crossingWidths))
                continue;

            var dash = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = horizontal ? new Vector3(dashLength, 0.035f, 0.24f) : new Vector3(0.24f, 0.035f, dashLength) },
                MaterialOverride = _laneMarkerMaterial,
                Position = horizontal
                    ? new Vector3(position, 0.085f, fixedCoordinate)
                    : new Vector3(fixedCoordinate, 0.085f, position)
            };

            AddChild(dash);
            dash.Name = $"LaneMarker{markerIndex++:000}";
        }
    }

    private static bool IsNearIntersection(float position, float[] crossingCenters, float[] crossingWidths)
    {
        for (int i = 0; i < crossingCenters.Length; i++)
        {
            if (Mathf.Abs(position - crossingCenters[i]) < crossingWidths[i] * 0.62f)
                return true;
        }

        return false;
    }

    private void AddCurbSegment(Vector3 size, Vector3 position, string name)
    {
        var curb = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = StableNameIndex(name) % 2 == 0 ? _curbWhiteMaterial : _curbRedMaterial,
            Position = position
        };

        AddChild(curb);
        curb.Name = name;
    }

    private void AddCrosswalk(Vector3 size, Vector3 position, string name)
    {
        var crosswalk = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = size },
            MaterialOverride = _crosswalkMaterial,
            Position = position
        };

        AddChild(crosswalk);
        crosswalk.Name = name;
    }

    private static void AddLightCandidate(List<LightCandidate> candidates, Vector3 position, Vector3 forward)
    {
        candidates.Add(new LightCandidate(position, forward.Normalized()));
    }

    private Vector3 BlockCenter(int column, int row)
    {
        float x0 = VerticalStreetCoordinate(column) + VerticalStreetWidth(column) * 0.5f;
        float x1 = VerticalStreetCoordinate(column + 1) - VerticalStreetWidth(column + 1) * 0.5f;
        float z0 = HorizontalStreetCoordinate(row) + HorizontalStreetWidth(row) * 0.5f;
        float z1 = HorizontalStreetCoordinate(row + 1) - HorizontalStreetWidth(row + 1) * 0.5f;
        return new Vector3((x0 + x1) * 0.5f, 0.0f, (z0 + z1) * 0.5f);
    }

    private Vector2 BlockInteriorSize(int column, int row)
    {
        float x0 = VerticalStreetCoordinate(column) + VerticalStreetWidth(column) * 0.5f;
        float x1 = VerticalStreetCoordinate(column + 1) - VerticalStreetWidth(column + 1) * 0.5f;
        float z0 = HorizontalStreetCoordinate(row) + HorizontalStreetWidth(row) * 0.5f;
        float z1 = HorizontalStreetCoordinate(row + 1) - HorizontalStreetWidth(row + 1) * 0.5f;
        return new Vector2(Mathf.Max(2.0f, x1 - x0), Mathf.Max(2.0f, z1 - z0));
    }

    private Vector3 BuildingSlotOffset(int column, int row, int slot)
    {
        Vector2 blockSize = BlockInteriorSize(column, row);
        float xSign = slot % 2 == 0 ? -1.0f : 1.0f;
        float zSign = slot / 2 % 2 == 0 ? -1.0f : 1.0f;
        float marginX = Mathf.Max(2.0f, Mathf.Min(BuildingSetback, blockSize.X * 0.2f));
        float marginZ = Mathf.Max(2.0f, Mathf.Min(BuildingSetback, blockSize.Y * 0.2f));

        return new Vector3(
            xSign * Mathf.Max(0.0f, blockSize.X * 0.25f - marginX * 0.25f) + _rng.RandfRange(-BuildingJitter, BuildingJitter) * 0.35f,
            0.0f,
            zSign * Mathf.Max(0.0f, blockSize.Y * 0.25f - marginZ * 0.25f) + _rng.RandfRange(-BuildingJitter, BuildingJitter) * 0.35f
        );
    }

    private float OrthogonalRotation()
    {
        return _rng.RandiRange(0, 3) * Mathf.Pi * 0.5f;
    }

    private static int StableNameIndex(string name)
    {
        int result = 0;
        for (int i = 0; i < name.Length; i++)
        {
            if (char.IsDigit(name[i]))
                result = result * 10 + name[i] - '0';
        }

        return result;
    }

    private int CityColumnCount()
    {
        return Math.Max(1, CityColumns);
    }

    private int CityRowCount()
    {
        return Math.Max(1, CityRows);
    }

    private float VerticalStreetCoordinate(int column)
    {
        return _verticalStreetCenters[Mathf.Clamp(column, 0, _verticalStreetCenters.Length - 1)];
    }

    private float HorizontalStreetCoordinate(int row)
    {
        return _horizontalStreetCenters[Mathf.Clamp(row, 0, _horizontalStreetCenters.Length - 1)];
    }

    private float VerticalStreetWidth(int column)
    {
        return _verticalStreetWidths[Mathf.Clamp(column, 0, _verticalStreetWidths.Length - 1)];
    }

    private float HorizontalStreetWidth(int row)
    {
        return _horizontalStreetWidths[Mathf.Clamp(row, 0, _horizontalStreetWidths.Length - 1)];
    }

    private float CityMinX()
    {
        return _verticalStreetCenters[0] - _verticalStreetWidths[0] * 0.5f;
    }

    private float CityMaxX()
    {
        return _verticalStreetCenters[^1] + _verticalStreetWidths[^1] * 0.5f;
    }

    private float CityMinZ()
    {
        return _horizontalStreetCenters[0] - _horizontalStreetWidths[0] * 0.5f;
    }

    private float CityMaxZ()
    {
        return _horizontalStreetCenters[^1] + _horizontalStreetWidths[^1] * 0.5f;
    }

    private float CityCenterX()
    {
        return (CityMinX() + CityMaxX()) * 0.5f;
    }

    private float CityCenterZ()
    {
        return (CityMinZ() + CityMaxZ()) * 0.5f;
    }

    private float CityWidth()
    {
        return CityMaxX() - CityMinX();
    }

    private float CityDepth()
    {
        return CityMaxZ() - CityMinZ();
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

    private readonly record struct LightCandidate(Vector3 Position, Vector3 Forward);
}
