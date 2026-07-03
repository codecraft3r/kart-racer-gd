using Godot;
using System;
using System.Collections.Generic;

public partial class TrackBuilder : Node3D
{
    [Export] public int BuildingCount = 40;
    [Export] public float TrackRadius = 50.0f;
    [Export] public float TrackWidth = 14.0f;
    [Export] public float WallHeightVariance = 1.0f;
    [Export] public int Seed = -1;

    private PackedScene[] _buildingScenes;
    private RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        if (Seed >= 0)
            _rng.Seed = (ulong)Seed;
        else
            _rng.Randomize();

        LoadBuildingScenes();
        GenerateTrack();
    }

    private void LoadBuildingScenes()
    {
        var dir = DirAccess.Open("res://assets/kenney_industrial");
        if (dir == null) return;

        var scenes = new List<PackedScene>();
        dir.ListDirBegin();
        string fileName = dir.GetNext();
        while (fileName != "")
        {
            if (fileName.EndsWith(".glb"))
            {
                var scene = GD.Load<PackedScene>($"res://assets/kenney_industrial/{fileName}");
                if (scene != null)
                    scenes.Add(scene);
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
        _buildingScenes = scenes.ToArray();
    }

    private void GenerateTrack()
    {
        if (_buildingScenes == null || _buildingScenes.Length == 0) return;

        float angleStep = Mathf.Tau / BuildingCount;

        for (int i = 0; i < BuildingCount; i++)
        {
            float angle = angleStep * i;
            float side = _rng.RandfRange(0, 1) > 0.5f ? 1.0f : -1.0f;
            float offset = TrackWidth * 0.5f + _rng.RandfRange(0, 3.0f);

            float x = Mathf.Cos(angle) * (TrackRadius + offset * side);
            float z = Mathf.Sin(angle) * (TrackRadius + offset * side);

            var scene = _buildingScenes[_rng.RandiRange(0, _buildingScenes.Length - 1)];
            var building = scene.Instantiate<Node3D>();
            building.Position = new Vector3(x, 0, z);
            building.RotateY(_rng.RandfRange(0, Mathf.Tau));
            AddChild(building);
        }

        PlaceDecorations();
    }

    private void PlaceDecorations()
    {
        for (int i = 0; i < 20; i++)
        {
            float angle = _rng.RandfRange(0, Mathf.Tau);
            float dist = _rng.RandfRange(TrackRadius - 8, TrackRadius + 8);
            float x = Mathf.Cos(angle) * dist;
            float z = Mathf.Sin(angle) * dist;

            var scene = _buildingScenes[_rng.RandiRange(0, _buildingScenes.Length - 1)];
            var deco = scene.Instantiate<Node3D>();
            deco.Position = new Vector3(x, 0, z);
            deco.RotateY(_rng.RandfRange(0, Mathf.Tau));
            AddChild(deco);
        }
    }
}
