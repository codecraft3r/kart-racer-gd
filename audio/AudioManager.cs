using Godot;
using System;
using System.Collections.Generic;

public partial class AudioManager : Node
{
    public enum MusicContext
    {
        Menu,
        Lobby,
        Gameplay,
        Results
    }

    public enum Cue
    {
        UiHover,
        UiConfirm,
        UiBack,
        UiToggle,
        CountdownTick,
        CountdownGo,
        PickupEnter,
        BoardingTick,
        PassengerBoard,
        FareComplete,
        Cash,
        PassengerBailout,
        Respawn,
        Warning,
        MatchWin,
        MatchLose,
        CollisionLight,
        CollisionMedium,
        CollisionHeavy,
        Destroyed,
        AssaultFire,
        RocketLaunch,
        Explosion,
        WeaponPickup
    }

    private sealed class CueDefinition
    {
        public string Bus { get; }
        public AudioStream[] Streams { get; }

        public CueDefinition(string bus, params string[] paths)
        {
            Bus = bus;
            Streams = new AudioStream[paths.Length];
            for (int i = 0; i < paths.Length; i++)
                Streams[i] = GD.Load<AudioStream>(paths[i]);
        }
    }

    public static AudioManager Instance { get; private set; }

    private const int LocalPoolSize = 12;
    private readonly Dictionary<Cue, CueDefinition> _cues = new();
    private readonly List<AudioStreamPlayer> _localPlayers = new();
    private int _nextLocalPlayer;

    private AudioStreamPlayer _cityAmbience;
    private AudioStreamPlayer _neonAmbience;
    private AudioStreamPlayer _industrialAmbience;
    private AudioStreamPlayer _musicA;
    private AudioStreamPlayer _musicB;
    private AudioStreamPlayer _activeMusic;
    private readonly Dictionary<AudioStreamPlayer, string> _musicNextTracks = new();
    private string _currentMusicPath = string.Empty;
    private MusicContext _musicContext = MusicContext.Menu;
    private bool _gameplayMix;
    private float _hoverCooldown;

    private bool _hasObservedMatchPhase;
    private TaxiMode.MatchPhase _lastMatchPhase;
    private int _lastCountdownSecond = -1;

    public override void _Ready()
    {
        if (Instance != null && Instance != this)
        {
            QueueFree();
            return;
        }

        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        EnsureAudioBuses();
        LoadCueLibrary();
        BuildLocalPlayerPool();
        StartAmbience();
        BuildMusicPlayers();
        SetMusicContext(MusicContext.Menu);
    }

    public override void _ExitTree()
    {
        if (Instance == this)
            Instance = null;
    }

    public override void _Process(double delta)
    {
        _hoverCooldown = Mathf.Max(0.0f, _hoverCooldown - (float)delta);
        UpdateAmbienceMix((float)delta);
        UpdateMatchCues();
    }

    public void SetGameplayMix(bool gameplay)
    {
        _gameplayMix = gameplay;
    }

    public void SetMusicContext(MusicContext context)
    {
        if (_activeMusic != null && context == _musicContext)
            return;

        _musicContext = context;
        switch (context)
        {
            case MusicContext.Lobby:
                PlayMusic("res://assets/audio/music/game/PTX_02_DispatchAfterDark_A.ogg", true);
                break;
            case MusicContext.Gameplay:
                PlayMusic(
                    "res://assets/audio/music/game/PTX_03_FlagfallFever_B.ogg",
                    false,
                    "res://assets/audio/music/game/PTX_04_RushHourRiot_B.ogg");
                break;
            case MusicContext.Results:
            case MusicContext.Menu:
            default:
                PlayMusic("res://assets/audio/music/game/PTX_01_MeterGlow_B.ogg", true);
                break;
        }
    }

    public void PlayUiHover()
    {
        if (_hoverCooldown > 0.0f)
            return;

        _hoverCooldown = 0.045f;
        PlayLocal(Cue.UiHover, -5.0f, (float)GD.RandRange(0.96, 1.04));
    }

    public void PlayUiConfirm() => PlayLocal(Cue.UiConfirm, -3.0f, (float)GD.RandRange(0.98, 1.03));
    public void PlayUiBack() => PlayLocal(Cue.UiBack, -4.0f);
    public void PlayUiToggle() => PlayLocal(Cue.UiToggle, -4.0f);

    public void PlayLocal(Cue cue, float volumeDb = 0.0f, float pitchScale = 1.0f)
    {
        if (!_cues.TryGetValue(cue, out CueDefinition definition))
            return;

        AudioStream stream = PickStream(definition);
        if (stream == null)
            return;

        AudioStreamPlayer player = AcquireLocalPlayer();
        player.Stop();
        player.Stream = stream;
        player.Bus = definition.Bus;
        player.VolumeDb = volumeDb;
        player.PitchScale = pitchScale;
        player.Play();
    }

    public void PlayWorld(Cue cue, Vector3 position, float volumeDb = 0.0f, float pitchScale = 1.0f, float maxDistance = 80.0f)
    {
        if (!_cues.TryGetValue(cue, out CueDefinition definition))
            return;

        AudioStream stream = PickStream(definition);
        if (stream == null)
            return;

        var player = new AudioStreamPlayer3D
        {
            Name = $"OneShot_{cue}",
            Stream = stream,
            Bus = definition.Bus,
            VolumeDb = volumeDb,
            PitchScale = pitchScale,
            MaxDistance = maxDistance,
            UnitSize = 7.0f,
            ProcessMode = ProcessModeEnum.Pausable
        };

        AddChild(player);
        player.GlobalPosition = position;
        player.Finished += player.QueueFree;
        player.Play();
    }

    private void LoadCueLibrary()
    {
        AddCue(Cue.UiHover, "UI", "res://assets/audio/ui/hover.ogg");
        AddCue(Cue.UiConfirm, "UI", "res://assets/audio/ui/confirm.ogg");
        AddCue(Cue.UiBack, "UI", "res://assets/audio/ui/back.ogg");
        AddCue(Cue.UiToggle, "UI", "res://assets/audio/ui/toggle.ogg");

        AddCue(Cue.CountdownTick, "UI", "res://assets/audio/gameplay/countdown_tick.ogg");
        AddCue(Cue.CountdownGo, "UI", "res://assets/audio/gameplay/countdown_go.ogg");
        AddCue(Cue.PickupEnter, "UI", "res://assets/audio/gameplay/pickup_enter.ogg");
        AddCue(Cue.BoardingTick, "UI", "res://assets/audio/gameplay/boarding_tick.ogg");
        AddCue(Cue.PassengerBoard, "UI", "res://assets/audio/gameplay/passenger_board.ogg");
        AddCue(Cue.FareComplete, "UI", "res://assets/audio/gameplay/fare_complete.ogg");
        AddCue(Cue.Cash, "UI", "res://assets/audio/gameplay/cash.ogg");
        AddCue(Cue.PassengerBailout, "UI", "res://assets/audio/gameplay/passenger_bailout.ogg");
        AddCue(Cue.Respawn, "UI", "res://assets/audio/gameplay/respawn.ogg");
        AddCue(Cue.Warning, "UI", "res://assets/audio/gameplay/warning.ogg");
        AddCue(Cue.MatchWin, "UI", "res://assets/audio/gameplay/match_win.ogg");
        AddCue(Cue.MatchLose, "UI", "res://assets/audio/gameplay/match_lose.ogg");

        AddCue(Cue.CollisionLight, "Impacts", "res://assets/audio/vehicles/collision_light.ogg");
        AddCue(Cue.CollisionMedium, "Impacts",
            "res://assets/audio/vehicles/collision_medium_01.ogg",
            "res://assets/audio/vehicles/collision_medium_02.ogg",
            "res://assets/audio/vehicles/collision_medium_03.ogg");
        AddCue(Cue.CollisionHeavy, "Impacts",
            "res://assets/audio/vehicles/collision_heavy_01.ogg",
            "res://assets/audio/vehicles/collision_heavy_02.ogg",
            "res://assets/audio/vehicles/collision_heavy_03.ogg");
        AddCue(Cue.Destroyed, "Impacts", "res://assets/audio/vehicles/destroyed.ogg");

        AddCue(Cue.AssaultFire, "Weapons", "res://assets/audio/weapons/assault_fire.ogg");
        AddCue(Cue.RocketLaunch, "Weapons", "res://assets/audio/weapons/rocket_launch.ogg");
        AddCue(Cue.Explosion, "Weapons", "res://assets/audio/weapons/explosion_arcade.ogg");
        AddCue(Cue.WeaponPickup, "UI", "res://assets/audio/weapons/pickup.ogg");
    }

    private void AddCue(Cue cue, string bus, params string[] paths)
    {
        _cues[cue] = new CueDefinition(bus, paths);
    }

    private static AudioStream PickStream(CueDefinition definition)
    {
        if (definition.Streams.Length == 0)
            return null;

        int index = definition.Streams.Length == 1 ? 0 : GD.RandRange(0, definition.Streams.Length - 1);
        return definition.Streams[index];
    }

    private void BuildLocalPlayerPool()
    {
        for (int i = 0; i < LocalPoolSize; i++)
        {
            var player = new AudioStreamPlayer
            {
                Name = $"LocalOneShot{i:00}",
                ProcessMode = ProcessModeEnum.Always
            };
            AddChild(player);
            _localPlayers.Add(player);
        }
    }

    private AudioStreamPlayer AcquireLocalPlayer()
    {
        foreach (AudioStreamPlayer player in _localPlayers)
        {
            if (!player.Playing)
                return player;
        }

        AudioStreamPlayer fallback = _localPlayers[_nextLocalPlayer];
        _nextLocalPlayer = (_nextLocalPlayer + 1) % _localPlayers.Count;
        return fallback;
    }

    private void StartAmbience()
    {
        _cityAmbience = CreateAmbiencePlayer("CityTraffic", "res://assets/audio/ambience/city_traffic.ogg", -34.0f);
        _neonAmbience = CreateAmbiencePlayer("NeonCity", "res://assets/audio/ambience/neon_city.ogg", -24.0f);
        _industrialAmbience = CreateAmbiencePlayer("IndustrialMachine", "res://assets/audio/ambience/industrial_machine.ogg", -46.0f);
    }

    private void BuildMusicPlayers()
    {
        _musicA = CreateMusicPlayer("MusicA");
        _musicB = CreateMusicPlayer("MusicB");
        _musicA.Finished += () => OnMusicFinished(_musicA);
        _musicB.Finished += () => OnMusicFinished(_musicB);
    }

    private AudioStreamPlayer CreateMusicPlayer(string name)
    {
        var player = new AudioStreamPlayer
        {
            Name = name,
            Bus = "Music",
            VolumeDb = -50.0f,
            ProcessMode = ProcessModeEnum.Always
        };
        AddChild(player);
        _musicNextTracks[player] = string.Empty;
        return player;
    }

    private void PlayMusic(string path, bool loop, string nextPath = "", float crossfadeSeconds = 1.2f)
    {
        if (_activeMusic != null && _currentMusicPath == path && _activeMusic.Playing)
            return;

        AudioStream stream = GD.Load<AudioStream>(path);
        if (stream == null)
        {
            GD.PushWarning($"AudioManager could not load music track: {path}");
            return;
        }

        if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = loop;
        else if (stream is AudioStreamWav wav)
            wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled;

        AudioStreamPlayer incoming = _activeMusic == _musicA ? _musicB : _musicA;
        AudioStreamPlayer outgoing = _activeMusic;

        incoming.Stop();
        incoming.Stream = stream;
        incoming.VolumeDb = -50.0f;
        incoming.Play();
        _musicNextTracks[incoming] = nextPath ?? string.Empty;

        _activeMusic = incoming;
        _currentMusicPath = path;

        Tween tween = CreateTween().SetParallel(true);
        tween.TweenProperty(incoming, "volume_db", 0.0f, crossfadeSeconds)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.Out);
        if (IsInstanceValid(outgoing) && outgoing.Playing)
        {
            _musicNextTracks[outgoing] = string.Empty;
            tween.TweenProperty(outgoing, "volume_db", -50.0f, crossfadeSeconds)
                .SetTrans(Tween.TransitionType.Sine)
                .SetEase(Tween.EaseType.In);
            tween.Chain().TweenCallback(Callable.From(outgoing.Stop));
        }
    }

    private void OnMusicFinished(AudioStreamPlayer player)
    {
        if (player != _activeMusic || !_musicNextTracks.TryGetValue(player, out string nextPath) || string.IsNullOrEmpty(nextPath))
            return;

        _musicNextTracks[player] = string.Empty;
        PlayMusic(nextPath, true, string.Empty, 0.45f);
    }

    private AudioStreamPlayer CreateAmbiencePlayer(string name, string path, float volumeDb)
    {
        AudioStream stream = LoadLoop(path);
        if (stream == null)
            return null;

        var player = new AudioStreamPlayer
        {
            Name = name,
            Stream = stream,
            Bus = "Ambience",
            VolumeDb = volumeDb,
            ProcessMode = ProcessModeEnum.Always
        };
        AddChild(player);
        player.Play();
        return player;
    }

    private static AudioStream LoadLoop(string path)
    {
        AudioStream stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = true;
        else if (stream is AudioStreamWav wav)
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        return stream;
    }

    private void UpdateAmbienceMix(float delta)
    {
        float blend = 1.0f - Mathf.Exp(-4.0f * delta);
        SetPlayerVolume(_cityAmbience, _gameplayMix ? -24.0f : -34.0f, blend);
        SetPlayerVolume(_neonAmbience, _gameplayMix ? -31.0f : -24.0f, blend);
        SetPlayerVolume(_industrialAmbience, _gameplayMix ? -34.0f : -46.0f, blend);
    }

    private static void SetPlayerVolume(AudioStreamPlayer player, float targetDb, float blend)
    {
        if (IsInstanceValid(player))
            player.VolumeDb = Mathf.Lerp(player.VolumeDb, targetDb, blend);
    }

    private void UpdateMatchCues()
    {
        TaxiMode mode = TaxiMode.Instance;
        if (mode == null)
        {
            _hasObservedMatchPhase = false;
            _lastCountdownSecond = -1;
            return;
        }

        TaxiMode.MatchPhase phase = mode.Phase;
        if (!_hasObservedMatchPhase)
        {
            _hasObservedMatchPhase = true;
            _lastMatchPhase = phase;
        }

        if (phase == TaxiMode.MatchPhase.Countdown)
        {
            int second = Mathf.Max(1, Mathf.CeilToInt((float)mode.CountdownRemaining));
            if (second != _lastCountdownSecond)
            {
                _lastCountdownSecond = second;
                float pitch = 1.0f + Mathf.Max(0, 3 - second) * 0.08f;
                PlayLocal(Cue.CountdownTick, -1.5f, pitch);
            }
        }

        if (phase != _lastMatchPhase)
        {
            if (phase == TaxiMode.MatchPhase.Active)
            {
                PlayLocal(Cue.CountdownGo, 0.0f, 1.0f);
                _lastCountdownSecond = -1;
            }
            else if (phase == TaxiMode.MatchPhase.Finished && mode.WinnerPeerId > 0)
            {
                int localPeerId = Multiplayer.GetUniqueId();
                PlayLocal(mode.WinnerPeerId == localPeerId ? Cue.MatchWin : Cue.MatchLose, -1.0f);
            }

            _lastMatchPhase = phase;
        }
    }

    private static void EnsureAudioBuses()
    {
        EnsureBus("Music", -4.0f);
        EnsureBus("Ambience", -3.0f);
        EnsureBus("Vehicles", -2.0f);
        EnsureBus("Weapons", -1.0f);
        EnsureBus("Impacts", -1.0f);
        EnsureBus("UI", -2.0f);
        EnsureBus("Voice", 0.0f);
    }

    private static void EnsureBus(string name, float volumeDb)
    {
        int index = AudioServer.GetBusIndex(name);
        if (index < 0)
        {
            AudioServer.AddBus();
            index = AudioServer.BusCount - 1;
            AudioServer.SetBusName(index, name);
            AudioServer.SetBusSend(index, "Master");
        }
        AudioServer.SetBusVolumeDb(index, volumeDb);
    }
}
