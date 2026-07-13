using Godot;

public partial class VehicleAudioController : Node3D
{
    private Kart _kart;
    private AudioStreamPlayer3D _idlePlayer;
    private AudioStreamPlayer3D _drivePlayer;
    private AudioStreamPlayer3D _skidPlayer;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Pausable;
        _kart = GetParentOrNull<Kart>();
        if (_kart == null)
        {
            SetProcess(false);
            return;
        }

        _idlePlayer = CreateLoopPlayer("EngineIdle", "res://assets/audio/vehicles/engine_idle.wav", -8.0f, 1.0f, 72.0f);
        _drivePlayer = CreateLoopPlayer("EngineDrive", "res://assets/audio/vehicles/engine_drive.wav", -48.0f, 0.8f, 82.0f);
        _skidPlayer = CreateLoopPlayer("TireSkid", "res://assets/audio/vehicles/tire_skid.wav", -60.0f, 1.0f, 65.0f);
        PlayEngineStart();
    }

    public override void _Process(double delta)
    {
        if (!IsInstanceValid(_kart))
            return;

        float dt = (float)delta;
        float speed = _kart.LinearVelocity.Length();
        float speedRatio = Mathf.Clamp(speed / Mathf.Max(1.0f, _kart.MaxForwardSpeed), 0.0f, 1.15f);
        float driveBlend = Mathf.SmoothStep(0.08f, 0.82f, speedRatio);

        float idleTarget = _kart.ControlsEnabled ? Mathf.Lerp(-7.0f, -24.0f, speedRatio) : -24.0f;
        float driveTarget = _kart.ControlsEnabled ? Mathf.Lerp(-42.0f, -7.0f, driveBlend) : -55.0f;

        Vector3 localVelocity = _kart.GlobalTransform.Basis.Inverse() * _kart.LinearVelocity;
        float lateralSlip = Mathf.Abs(localVelocity.X);
        float skidIntensity = speed > 5.0f ? Mathf.Clamp((lateralSlip - 2.0f) / 7.0f, 0.0f, 1.0f) : 0.0f;
        float skidTarget = Mathf.Lerp(-60.0f, -7.0f, skidIntensity);

        float blend = 1.0f - Mathf.Exp(-8.0f * dt);
        _idlePlayer.VolumeDb = Mathf.Lerp(_idlePlayer.VolumeDb, idleTarget, blend);
        _drivePlayer.VolumeDb = Mathf.Lerp(_drivePlayer.VolumeDb, driveTarget, blend);
        _skidPlayer.VolumeDb = Mathf.Lerp(_skidPlayer.VolumeDb, skidTarget, blend);

        _idlePlayer.PitchScale = Mathf.Clamp(0.78f + speedRatio * 0.48f, 0.72f, 1.38f);
        _drivePlayer.PitchScale = Mathf.Clamp(0.72f + speedRatio * 0.72f, 0.68f, 1.55f);
        _skidPlayer.PitchScale = Mathf.Lerp(0.88f, 1.18f, skidIntensity);
    }

    private AudioStreamPlayer3D CreateLoopPlayer(string name, string path, float volumeDb, float pitch, float maxDistance)
    {
        AudioStream stream = GD.Load<AudioStream>(path);
        if (stream is AudioStreamWav wav)
            wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        else if (stream is AudioStreamOggVorbis ogg)
            ogg.Loop = true;

        var player = new AudioStreamPlayer3D
        {
            Name = name,
            Stream = stream,
            Bus = "Vehicles",
            VolumeDb = volumeDb,
            PitchScale = pitch,
            MaxDistance = maxDistance,
            UnitSize = 6.0f
        };
        AddChild(player);
        player.Play();
        return player;
    }

    private void PlayEngineStart()
    {
        AudioStream stream = GD.Load<AudioStream>("res://assets/audio/vehicles/engine_start.wav");
        if (stream == null)
            return;

        var player = new AudioStreamPlayer3D
        {
            Name = "EngineStart",
            Stream = stream,
            Bus = "Vehicles",
            VolumeDb = -5.0f,
            MaxDistance = 72.0f,
            UnitSize = 6.0f
        };
        AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }
}
