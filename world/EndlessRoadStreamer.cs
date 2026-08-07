using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Deterministic endless road streamer that recycles fixed-length road chunks
/// ahead of and behind the player.
/// </summary>
public partial class EndlessRoadStreamer : Node3D
{
    public static EndlessRoadStreamer Instance { get; private set; }

    [Export] public EndlessRoadSettings Settings = new();
    [Export] public PackedScene ChunkScene;
    [Export] public Node3D RoadRoot;

    private readonly List<EndlessRoadChunk> _activeChunks = new();
    private readonly Dictionary<int, EndlessRoadChunk> _chunksByIndex = new();
    private int _firstIndex;
    private float _playerZ;
    private bool _initialized;

    public int ActiveChunkCount => _activeChunks.Count;
    public IReadOnlyList<EndlessRoadChunk> ActiveChunks => _activeChunks;

    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Initialize(int seed)
    {
        Clear();

        var rng = new RandomNumberGenerator();
        rng.Seed = (ulong)seed;

        _firstIndex = 0;
        _playerZ = 0.0f;

        for (int i = _firstIndex; i < Settings.ActiveChunksAhead; i++)
            AddChunk(i, rng);

        _initialized = true;
        GD.Print($"EndlessRoadStreamer initialized with seed={seed}.");
    }

    public void UpdateStream(float playerZ)
    {
        if (!_initialized || Settings == null)
            return;

        _playerZ = playerZ;
        int lastIndex = _firstIndex + _activeChunks.Count - 1;
        int firstDesired = FloorChunkIndex(playerZ) - Settings.ActiveChunksBehind;
        int lastDesired = firstDesired + Settings.ActiveChunksAhead + Settings.ActiveChunksBehind;

        if (firstDesired > _firstIndex)
        {
            int removeCount = firstDesired - _firstIndex;
            for (int i = 0; i < removeCount && _activeChunks.Count > 0; i++)
            {
                if (_activeChunks[0] != null && IsInstanceValid(_activeChunks[0]))
                    _activeChunks[0].QueueFree();

                _chunksByIndex.Remove(_firstIndex);
                _activeChunks.RemoveAt(0);
                _firstIndex++;
            }
        }

        for (int i = lastDesired; i >= lastIndex + 1; i--)
        {
            var rng = new RandomNumberGenerator();
            rng.Seed = (ulong)(Settings.RunSeed + i);
            AddChunk(i, rng);
        }
    }

    public void Clear()
    {
        _initialized = false;
        foreach (var chunk in _activeChunks.ToArray())
        {
            if (chunk != null && IsInstanceValid(chunk))
                chunk.QueueFree();
        }

        _activeChunks.Clear();
        _chunksByIndex.Clear();
        _firstIndex = 0;
        _playerZ = 0.0f;
    }

    public float GetChunkCenterZ(int index)
    {
        return index * Settings.ChunkLength;
    }

    public EndlessRoadChunk GetChunkForZ(float z)
    {
        int index = FloorChunkIndex(z);
        return _chunksByIndex.TryGetValue(index, out var chunk) ? chunk : null;
    }

    public EndlessRoadChunk GetChunkByIndex(int index)
    {
        return _chunksByIndex.TryGetValue(index, out var chunk) ? chunk : null;
    }

    private void AddChunk(int index, RandomNumberGenerator rng)
    {
        if (_chunksByIndex.ContainsKey(index))
            return;

        EndlessRoadChunk chunk;
        if (ChunkScene != null)
        {
            chunk = ChunkScene.Instantiate<EndlessRoadChunk>();
        }
        else
        {
            chunk = new EndlessRoadChunk();
        }

        chunk.ChunkIndex = index;
        chunk.Initialize(Settings, rng);
        chunk.Position = new Vector3(0.0f, 0.0f, index * Settings.ChunkLength);

        if (RoadRoot != null)
            RoadRoot.AddChild(chunk);
        else
            AddChild(chunk);

        _activeChunks.Add(chunk);
        _chunksByIndex[index] = chunk;
    }

    private static int FloorChunkIndex(float z)
    {
        float chunkLength = EndlessRoadMode.Instance?.Settings.ChunkLength ?? 80.0f;
        if (chunkLength <= 0.0f)
            chunkLength = 80.0f;

        return (int)Math.Floor(z / chunkLength);
    }
}
