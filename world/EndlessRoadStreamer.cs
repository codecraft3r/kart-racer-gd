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
    private readonly List<EndlessRoadChunk> _chunkPool = new();
    private readonly Dictionary<int, EndlessRoadChunk> _chunksByIndex = new();
    private int _firstIndex;
    private float _playerZ;
    private bool _initialized;
    private int _runSeed = 1;

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

        foreach (EndlessRoadChunk chunk in _chunkPool)
        {
            if (chunk != null && IsInstanceValid(chunk))
            {
                chunk.ReleaseGeneratedContent();
                chunk.QueueFree();
            }
        }

        _chunkPool.Clear();
    }

    public void Initialize(int seed)
    {
        Clear();

        _runSeed = seed;
        // Keep the settings resource honest about which run is loaded; chunk layout is
        // derived from _runSeed so streaming order never changes the generated road.
        if (Settings != null)
            Settings.RunSeed = seed;

        _firstIndex = 0;
        _playerZ = 0.0f;

        for (int i = _firstIndex; i < Settings.ActiveChunksAhead; i++)
            AddChunk(i);

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
                EndlessRoadChunk chunk = _activeChunks[0];
                if (chunk != null && IsInstanceValid(chunk))
                {
                    chunk.Visible = false;
                    chunk.ProcessMode = ProcessModeEnum.Disabled;
                    chunk.GetParent()?.RemoveChild(chunk);
                    _chunkPool.Add(chunk);
                }

                _chunksByIndex.Remove(_firstIndex);
                _activeChunks.RemoveAt(0);
                _firstIndex++;
            }
        }

        for (int i = lastIndex + 1; i <= lastDesired; i++)
            AddChunk(i);
    }

    public void Clear()
    {
        _initialized = false;
        for (int i = 0; i < _activeChunks.Count; i++)
        {
            var chunk = _activeChunks[i];
            if (chunk != null && IsInstanceValid(chunk))
            {
                chunk.ReleaseGeneratedContent();
                chunk.QueueFree();
            }
        }

        // Clear is also called immediately before the director queues this streamer for
        // deletion. Detached pooled nodes would outlive that owner and trigger ObjectDB
        // leak diagnostics, so only stream-boundary removals are pooled.
        foreach (EndlessRoadChunk chunk in _chunkPool)
        {
            if (chunk != null && IsInstanceValid(chunk))
            {
                chunk.ReleaseGeneratedContent();
                chunk.QueueFree();
            }
        }

        _activeChunks.Clear();
        _chunkPool.Clear();
        _chunksByIndex.Clear();
        _firstIndex = 0;
        _playerZ = 0.0f;
        EndlessRoadChunk.ReleaseSharedGeometry();
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

    private void AddChunk(int index)
    {
        using var perf = PerfProbe.Measure(PerfHotspot.EndlessChunkCreate);
        if (_chunksByIndex.ContainsKey(index))
            return;

        EndlessRoadChunk chunk;
        if (_chunkPool.Count > 0)
        {
            chunk = _chunkPool[_chunkPool.Count - 1];
            _chunkPool.RemoveAt(_chunkPool.Count - 1);
        }
        else if (ChunkScene != null)
        {
            chunk = ChunkScene.Instantiate<EndlessRoadChunk>();
        }
        else
        {
            chunk = new EndlessRoadChunk();
        }

        chunk.ChunkIndex = index;
        chunk.Initialize(Settings, _runSeed);
        chunk.Position = new Vector3(0.0f, 0.0f, index * Settings.ChunkLength);
        chunk.Visible = true;
        chunk.ProcessMode = ProcessModeEnum.Inherit;

        if (RoadRoot != null)
            RoadRoot.AddChild(chunk);
        else
            AddChild(chunk);

        _activeChunks.Add(chunk);
        _chunksByIndex[index] = chunk;
    }

    private int FloorChunkIndex(float z)
    {
        float chunkLength = Settings?.ChunkLength ?? 80.0f;
        if (chunkLength <= 0.0f)
            chunkLength = 80.0f;

        return (int)Math.Floor(z / chunkLength);
    }
}
