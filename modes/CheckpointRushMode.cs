using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CheckpointRushMode : Node3D
{
    public static CheckpointRushMode Instance { get; private set; }

    public event Action<int, int, int> ScoreboardChanged;
    public event Action<int, Vector3> CheckpointChanged;
    public event Action<double, bool, int> MatchStateChanged;

    [Export] public double MatchDurationSeconds = 180.0;
    [Export] public int WinningScore = 7;

    private readonly Dictionary<int, int> _scores = new();
    private readonly Vector3[] _checkpointPositions =
    {
        new(-72, 1.0f, -54),
        new(0, 1.0f, -72),
        new(72, 1.0f, -54),
        new(84, 1.0f, 0),
        new(56, 1.0f, 58),
        new(0, 1.0f, 72),
        new(-58, 1.0f, 48),
        new(-84, 1.0f, 0)
    };

    private Area3D _checkpointArea;
    private Node3D _checkpointVisual;
    private int _activeCheckpointIndex;
    private double _timeRemaining;
    private bool _matchActive;
    private int _winnerPeerId;

    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
        _timeRemaining = MatchDurationSeconds;
        BuildCheckpoint();
        ApplyCheckpointPosition();
        PublishLocalEvents();
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Multiplayer.IsServer() || !_matchActive)
            return;

        _timeRemaining = Math.Max(0.0, _timeRemaining - delta);
        if (_timeRemaining <= 0.0)
            EndMatch(FindLeader());
        else
            Rpc(nameof(SyncMatchStateRpc), _timeRemaining, _matchActive, _winnerPeerId);
    }

    public void StartMatch()
    {
        if (!Multiplayer.IsServer())
            return;

        _scores.Clear();
        foreach (int peerId in GetConnectedPeerIds())
            _scores[peerId] = 0;

        _timeRemaining = MatchDurationSeconds;
        _winnerPeerId = 0;
        _matchActive = true;
        PickNextCheckpoint(-1);
        BroadcastFullState();
    }

    public void ResetScores()
    {
        _scores.Clear();
        foreach (int peerId in GetConnectedPeerIds())
            _scores[peerId] = 0;
        PublishLocalEvents();
    }

    public void RegisterPlayer(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        if (!_scores.ContainsKey(peerId))
            _scores[peerId] = 0;

        BroadcastFullState();
    }

    public void RemovePlayer(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        if (_scores.Remove(peerId))
            BroadcastFullState();
    }

    public void SyncToPeer(int peerId)
    {
        if (!Multiplayer.IsServer())
            return;

        int[] peerIds = _scores.Keys.OrderBy(id => id).ToArray();
        int[] scores = peerIds.Select(id => _scores[id]).ToArray();
        RpcId(peerId, nameof(SyncFullStateRpc), _timeRemaining, _matchActive, _winnerPeerId, _activeCheckpointIndex, peerIds, scores);
    }

    public IReadOnlyDictionary<int, int> Scores => _scores;
    public bool MatchActive => _matchActive;
    public double TimeRemaining => _timeRemaining;
    public int WinnerPeerId => _winnerPeerId;
    public Vector3 ActiveCheckpointPosition => _checkpointPositions[Mathf.Clamp(_activeCheckpointIndex, 0, _checkpointPositions.Length - 1)];

    private void BuildCheckpoint()
    {
        _checkpointArea = new Area3D { Name = "ActiveCheckpointArea", Monitoring = true, Monitorable = false };
        var shape = new CollisionShape3D { Name = "CheckpointShape" };
        shape.Shape = new CylinderShape3D { Radius = 5.0f, Height = 3.0f };
        _checkpointArea.AddChild(shape);
        _checkpointArea.BodyEntered += OnCheckpointBodyEntered;
        AddChild(_checkpointArea);

        _checkpointVisual = new Node3D { Name = "CheckpointVisual" };
        _checkpointArea.AddChild(_checkpointVisual);

        var beaconMesh = new CylinderMesh
        {
            TopRadius = 5.0f,
            BottomRadius = 5.0f,
            Height = 0.18f,
            RadialSegments = 48
        };
        var beaconMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.0f, 0.95f, 1.0f, 0.55f),
            EmissionEnabled = true,
            Emission = new Color(0.0f, 0.8f, 1.0f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };

        var ring = new MeshInstance3D
        {
            Name = "CheckpointRing",
            Mesh = beaconMesh,
            MaterialOverride = beaconMaterial,
            Position = new Vector3(0, 0.1f, 0)
        };
        _checkpointVisual.AddChild(ring);

        var light = new OmniLight3D
        {
            Name = "CheckpointGlow",
            LightColor = new Color(0.0f, 0.95f, 1.0f),
            LightEnergy = 1.4f,
            OmniRange = 18.0f,
            Position = new Vector3(0, 3.0f, 0),
            ShadowEnabled = false
        };
        _checkpointVisual.AddChild(light);
    }

    private bool IsNetworked()
    {
        return Multiplayer.HasMultiplayerPeer() && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;
    }

    private void OnCheckpointBodyEntered(Node body)
    {
        if (IsNetworked() && !_matchActive)
            return;

        if (!Multiplayer.IsServer())
            return;

        if (body is not Kart kart)
            return;

        int peerId = kart.OwnerPeerId;
        if (peerId <= 0)
            return;

        _scores.TryAdd(peerId, 0);
        _scores[peerId]++;

        if (IsNetworked() && _scores[peerId] >= WinningScore)
            EndMatch(peerId);
        else
            PickNextCheckpoint(_activeCheckpointIndex);

        BroadcastFullState();
    }

    private void PickNextCheckpoint(int previousIndex)
    {
        if (_checkpointPositions.Length == 0)
            return;

        int next = GD.RandRange(0, _checkpointPositions.Length - 1);
        if (_checkpointPositions.Length > 1 && next == previousIndex)
            next = (next + 1) % _checkpointPositions.Length;

        _activeCheckpointIndex = next;
        ApplyCheckpointPosition();
    }

    private void ApplyCheckpointPosition()
    {
        if (_checkpointArea == null || _checkpointPositions.Length == 0)
            return;

        _checkpointArea.GlobalPosition = ActiveCheckpointPosition;
    }

    private void EndMatch(int winnerPeerId)
    {
        _matchActive = false;
        _winnerPeerId = winnerPeerId;
        _timeRemaining = Math.Max(0.0, _timeRemaining);
    }

    private int FindLeader()
    {
        if (_scores.Count == 0)
            return 0;

        return _scores.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).First().Key;
    }

    private IEnumerable<int> GetConnectedPeerIds()
    {
        yield return Multiplayer.GetUniqueId();
        foreach (int peerId in Multiplayer.GetPeers())
            yield return peerId;
    }

    private void BroadcastFullState()
    {
        int[] peerIds = _scores.Keys.OrderBy(id => id).ToArray();
        int[] scores = peerIds.Select(id => _scores[id]).ToArray();
        Rpc(nameof(SyncFullStateRpc), _timeRemaining, _matchActive, _winnerPeerId, _activeCheckpointIndex, peerIds, scores);
        SyncFullStateRpc(_timeRemaining, _matchActive, _winnerPeerId, _activeCheckpointIndex, peerIds, scores);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncFullStateRpc(double timeRemaining, bool matchActive, int winnerPeerId, int checkpointIndex, int[] peerIds, int[] scores)
    {
        _timeRemaining = timeRemaining;
        _matchActive = matchActive;
        _winnerPeerId = winnerPeerId;
        _activeCheckpointIndex = Mathf.Clamp(checkpointIndex, 0, _checkpointPositions.Length - 1);
        _scores.Clear();

        int count = Math.Min(peerIds.Length, scores.Length);
        for (int i = 0; i < count; i++)
            _scores[peerIds[i]] = scores[i];

        ApplyCheckpointPosition();
        PublishLocalEvents();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    private void SyncMatchStateRpc(double timeRemaining, bool matchActive, int winnerPeerId)
    {
        _timeRemaining = timeRemaining;
        _matchActive = matchActive;
        _winnerPeerId = winnerPeerId;
        MatchStateChanged?.Invoke(_timeRemaining, _matchActive, _winnerPeerId);
    }

    private void PublishLocalEvents()
    {
        CheckpointChanged?.Invoke(_activeCheckpointIndex, ActiveCheckpointPosition);
        MatchStateChanged?.Invoke(_timeRemaining, _matchActive, _winnerPeerId);
        foreach (KeyValuePair<int, int> entry in _scores)
            ScoreboardChanged?.Invoke(entry.Key, entry.Value, GetRank(entry.Key));
    }

    public int GetScore(int peerId)
    {
        return _scores.TryGetValue(peerId, out int score) ? score : 0;
    }

    public int GetRank(int peerId)
    {
        int rank = 1;
        int score = GetScore(peerId);
        foreach (KeyValuePair<int, int> entry in _scores)
        {
            if (entry.Value > score)
                rank++;
        }
        return rank;
    }
}
