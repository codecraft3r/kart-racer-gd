using Godot;

/// <summary>
/// Owns the Endless Road's world pieces (streamer + score/rival systems).
/// Sits on the root so it survives TaxiMode resets.
/// </summary>
public partial class EndlessRoadDirector : Node
{
    public static EndlessRoadDirector Instance { get; private set; }

    private EndlessRoadStreamer _streamer;
    private EndlessRoadScoreSystem _score;
    private EndlessRoadRivalDirector _rivals;
    private Kart _kart;

    public static void EnsureInTree(Node owner)
    {
        if (Instance != null && GodotObject.IsInstanceValid(Instance)) return;
        var d = new EndlessRoadDirector { Name = "EndlessRoadDirector" };
        // Add to the scene root so it doesn't get swept on UI resets.
        Node root = owner.GetTree().CurrentScene ?? owner;
        root.AddChild(d);
    }

    public override void _Ready()
    {
        if (Instance != null && Instance != this) { QueueFree(); return; }
        Instance = this;
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }

    public void Activate(Kart kart, bool resetRoad)
    {
        _kart = kart;
        if (_streamer == null || !IsInstanceValid(_streamer))
        {
            _streamer = new EndlessRoadStreamer();
            AddChild(_streamer);
        }
        if (_score == null || !IsInstanceValid(_score))
        {
            _score = new EndlessRoadScoreSystem();
            AddChild(_score);
        }
        if (_rivals == null || !IsInstanceValid(_rivals))
        {
            _rivals = new EndlessRoadRivalDirector();
            AddChild(_rivals);
        }
        _score.BindKart(kart);
        _rivals.BindKart(kart);
        if (resetRoad && EndlessRoadMode.Instance != null)
        {
            EndlessRoadSettings s = EndlessRoadMode.Instance.Settings;
            float seed = EndlessRoadMode.Instance.RunSeed;
            _streamer.Settings = s;
            _streamer.Initialize(Mathf.RoundToInt(seed));
        }
    }

    public void Deactivate()
    {
        _rivals?.Clear();
        _score?.QueueFree(); _score = null;
        _rivals?.QueueFree(); _rivals = null;
        _streamer?.Clear(); _streamer?.QueueFree(); _streamer = null;
        _kart = null;
    }

    public void Tick(float dt)
    {
        if (_streamer == null || _kart == null || !IsInstanceValid(_kart)) return;
        _streamer.UpdateStream(_kart.GlobalPosition.Z);
    }
}
