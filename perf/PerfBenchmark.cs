using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

public partial class PerfBenchmark : Node
{
    private readonly record struct StatSummary(double avg, double min, double max, double p50, double p95, double p99);

    private readonly struct FrameSample
    {
        public readonly double FrameMs;
        public readonly double Fps;
        public readonly double ProcessMs;
        public readonly double PhysicsMs;
        public readonly double RenderCpuMs;
        public readonly double GpuMs;
        public readonly double DrawCalls;
        public readonly double RenderObjects;
        public readonly double Nodes;
        public readonly double StaticMemoryBytes;
        public readonly double VideoMemoryBytes;
        public readonly double ActivePhysicsObjects;
        public readonly double CollisionPairs;

        public FrameSample(double frameMs, Rid viewport)
        {
            FrameMs = frameMs;
            Fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
            ProcessMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
            PhysicsMs = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
            RenderCpuMs = RenderingServer.ViewportGetMeasuredRenderTimeCpu(viewport) + RenderingServer.GetFrameSetupTimeCpu();
            GpuMs = RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport);
            DrawCalls = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            RenderObjects = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
            Nodes = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            StaticMemoryBytes = Performance.GetMonitor(Performance.Monitor.MemoryStatic);
            VideoMemoryBytes = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed);
            ActivePhysicsObjects = Performance.GetMonitor(Performance.Monitor.Physics3DActiveObjects);
            CollisionPairs = Performance.GetMonitor(Performance.Monitor.Physics3DCollisionPairs);
        }
    }

    private const int MaxSamples = 30000;
    private readonly FrameSample[] _samples = new FrameSample[MaxSamples];
    private int _sampleCount;
    private string _scenario = string.Empty;
    private string _outputDirectory = string.Empty;
    private double _warmupSeconds = 5.0;
    private double _durationSeconds = 15.0;
    private double _readySeconds;
    private double _measureSeconds;
    private bool _active;
    private bool _measuring;
    private bool _initialized;
    private long _managedAllocationStart;
    private int _gc0Start;
    private int _gc1Start;
    private int _gc2Start;
    private long _workingSetStart;
    private Rid _viewportRid;
    private int _activityStage;
    private bool _audioStress;
    private double _audioStressAccumulator;
    private Vector3 _lastActivityTarget = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
    private IReadOnlyList<PerfProbeSnapshot> _startupHotspots = Array.Empty<PerfProbeSnapshot>();

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        ParseArguments();
        if (string.IsNullOrWhiteSpace(_scenario))
        {
            SetProcess(false);
            SetPhysicsProcess(false);
            return;
        }

        _active = true;
        _viewportRid = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, true);
        PerfProbe.Enabled = true;
        GD.Print($"PERF benchmark requested: scenario={_scenario}, warmup={_warmupSeconds:F1}s, duration={_durationSeconds:F1}s");
        CallDeferred(nameof(InitializeScenario));
    }

    public override void _ExitTree()
    {
        if (_active && _viewportRid.IsValid)
            RenderingServer.ViewportSetMeasureRenderTime(_viewportRid, false);
    }

    public void InitializeScenario()
    {
        if (_initialized)
            return;

        _initialized = true;
        if (_scenario is "solo-default" or "pickup-dropoff" or "solo-max" or "visual-worst")
        {
            GameManager manager = GameManager.Instance;
            RetroNeonCabShell shell = GetParent()?.GetNodeOrNull<RetroNeonCabShell>("RetroNeonCabShell");
            if (manager == null || shell == null)
            {
                GD.PushError("PERF benchmark could not find GameManager or RetroNeonCabShell.");
                GetTree().Quit(2);
                return;
            }

            manager.SoloAiCount = _scenario is "solo-max" or "visual-worst" ? 6 : 2;
            shell.StartRun();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_active || _scenario != "pickup-dropoff" || TaxiMode.Instance?.MatchActive != true)
            return;

        Kart kart = GameManager.Instance?.GetKart(1);
        if (!IsInstanceValid(kart))
            return;

        Vector3 target = kart.HasPassenger()
            ? TaxiMode.Instance.GetPlayerDestination(1)
            : TaxiMode.Instance.GetNearestPickupPosition(kart.GlobalPosition);
        if (target == Vector3.Zero)
            return;

        if (_lastActivityTarget.DistanceSquaredTo(target) > 0.25f)
        {
            kart.GlobalPosition = target + Vector3.Up * 0.65f;
            kart.Rotation = Vector3.Zero;
            _lastActivityTarget = target;
            _activityStage++;
        }

        kart.LinearVelocity = Vector3.Zero;
        kart.AngularVelocity = Vector3.Zero;
    }

    public override void _Process(double delta)
    {
        if (!_active)
            return;

        if (!_measuring)
        {
            if (!ScenarioIsReady())
                return;

            _readySeconds += delta;
            if (_readySeconds >= _warmupSeconds)
                BeginMeasurement();
            return;
        }

        if (_audioStress)
        {
            _audioStressAccumulator += delta;
            if (_audioStressAccumulator >= 0.1)
            {
                _audioStressAccumulator -= 0.1;
                Vector3 position = GameManager.Instance?.GetKart(1)?.GlobalPosition ?? Vector3.Zero;
                AudioManager.Instance?.PlayWorld(AudioManager.Cue.CollisionMedium, position, -12.0f);
            }
        }

        if (_sampleCount < MaxSamples)
            _samples[_sampleCount++] = new FrameSample(delta * 1000.0, _viewportRid);

        _measureSeconds += delta;
        if (_measureSeconds >= _durationSeconds)
            FinishMeasurement();
    }

    private bool ScenarioIsReady()
    {
        if (TaxiMode.Instance?.MatchActive != true || GameManager.Instance == null)
            return false;

        return _scenario switch
        {
            "solo-default" => GameManager.Instance.GetRegisteredPlayerCount() >= 3,
            "pickup-dropoff" => GameManager.Instance.GetRegisteredPlayerCount() >= 3,
            "solo-max" => GameManager.Instance.GetRegisteredPlayerCount() >= 7,
            "visual-worst" => GameManager.Instance.GetRegisteredPlayerCount() >= 7,
            "multiplayer-host" => Multiplayer.IsServer() && Multiplayer.GetPeers().Length >= 1 && GameManager.Instance.GetRegisteredPlayerCount() >= 2,
            "multiplayer-client" => !Multiplayer.IsServer() && Multiplayer.GetUniqueId() > 1 && GameManager.Instance.GetRegisteredPlayerCount() >= 2,
            _ => false
        };
    }

    private void BeginMeasurement()
    {
        _startupHotspots = PerfProbe.Capture();
        _ = CaptureNetworkStatistics();
        PerfProbe.Reset();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        _managedAllocationStart = GC.GetAllocatedBytesForCurrentThread();
        _gc0Start = GC.CollectionCount(0);
        _gc1Start = GC.CollectionCount(1);
        _gc2Start = GC.CollectionCount(2);
        _workingSetStart = Process.GetCurrentProcess().WorkingSet64;
        _measuring = true;
        GD.Print($"PERF measurement started: {_scenario}");
    }

    private void FinishMeasurement()
    {
        _active = false;
        _measuring = false;

        long managedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - _managedAllocationStart;
        int gc0 = GC.CollectionCount(0) - _gc0Start;
        int gc1 = GC.CollectionCount(1) - _gc1Start;
        int gc2 = GC.CollectionCount(2) - _gc2Start;
        long workingSetEnd = Process.GetCurrentProcess().WorkingSet64;
        var network = CaptureNetworkStatistics();
        IReadOnlyList<PerfProbeSnapshot> hotspots = PerfProbe.Capture();

        Directory.CreateDirectory(_outputDirectory);
        string safeScenario = _scenario.Replace(' ', '-');
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string csvPath = Path.Combine(_outputDirectory, $"{safeScenario}-{stamp}-frames.csv");
        string jsonPath = Path.Combine(_outputDirectory, $"{safeScenario}-{stamp}-summary.json");
        WriteCsv(csvPath);

        var summary = new
        {
            scenario = _scenario,
            utc = DateTime.UtcNow,
            build = IsDebugBuild() ? "Debug C# assembly" : "Release C# assembly",
            godot = Engine.GetVersionInfo()["string"].AsString(),
            renderer = RenderingServer.GetCurrentRenderingDriverName(),
            display = DisplayServer.GetName(),
            samples = _sampleCount,
            measured_seconds = _measureSeconds,
            activity_transitions = _activityStage,
            audio_stress = _audioStress,
            metrics = new
            {
                fps = Stats(s => s.Fps),
                frame_ms = Stats(s => s.FrameMs),
                main_process_ms = Stats(s => s.ProcessMs),
                physics_ms = Stats(s => s.PhysicsMs),
                render_cpu_ms = Stats(s => s.RenderCpuMs),
                gpu_ms = Stats(s => s.GpuMs),
                draw_calls = Stats(s => s.DrawCalls),
                rendered_objects = Stats(s => s.RenderObjects),
                node_count = Stats(s => s.Nodes),
                static_memory_bytes = Stats(s => s.StaticMemoryBytes),
                video_memory_bytes = Stats(s => s.VideoMemoryBytes),
                active_physics_objects = Stats(s => s.ActivePhysicsObjects),
                collision_pairs = Stats(s => s.CollisionPairs)
            },
            managed = new
            {
                allocated_bytes = managedAllocatedBytes,
                allocated_bytes_per_second = managedAllocatedBytes / Math.Max(0.001, _measureSeconds),
                gc_gen0 = gc0,
                gc_gen1 = gc1,
                gc_gen2 = gc2,
                working_set_start_bytes = _workingSetStart,
                working_set_end_bytes = workingSetEnd
            },
            network,
            rpc_events = Enum.GetValues<PerfEvent>().ToDictionary(e => e.ToString(), PerfProbe.GetEventCount),
            startup_hotspots = _startupHotspots,
            hotspots
        };

        File.WriteAllText(jsonPath, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"PERF_RESULT scenario={_scenario} fps_avg={summary.metrics.fps.avg:F2} frame_p95_ms={summary.metrics.frame_ms.p95:F3} process_avg_ms={summary.metrics.main_process_ms.avg:F3} physics_avg_ms={summary.metrics.physics_ms.avg:F3} render_cpu_avg_ms={summary.metrics.render_cpu_ms.avg:F3} gpu_avg_ms={summary.metrics.gpu_ms.avg:F3} draw_calls_avg={summary.metrics.draw_calls.avg:F1} alloc_Bps={summary.managed.allocated_bytes_per_second:F0}");
        GD.Print($"PERF_OUTPUT {jsonPath}");
        GetTree().Quit();
    }

    private object CaptureNetworkStatistics()
    {
        if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer enet || enet.Host == null)
            return new { active = false, sent_packets = 0L, received_packets = 0L, sent_bytes = 0L, received_bytes = 0L };

        long sentBytes = (long)enet.Host.PopStatistic(ENetConnection.HostStatistic.SentData);
        long sentPackets = (long)enet.Host.PopStatistic(ENetConnection.HostStatistic.SentPackets);
        long receivedBytes = (long)enet.Host.PopStatistic(ENetConnection.HostStatistic.ReceivedData);
        long receivedPackets = (long)enet.Host.PopStatistic(ENetConnection.HostStatistic.ReceivedPackets);
        return new
        {
            active = true,
            sent_packets = sentPackets,
            received_packets = receivedPackets,
            sent_bytes = sentBytes,
            received_bytes = receivedBytes,
            sent_packets_per_second = sentPackets / Math.Max(0.001, _measureSeconds),
            received_packets_per_second = receivedPackets / Math.Max(0.001, _measureSeconds),
            sent_bytes_per_second = sentBytes / Math.Max(0.001, _measureSeconds),
            received_bytes_per_second = receivedBytes / Math.Max(0.001, _measureSeconds)
        };
    }

    private StatSummary Stats(Func<FrameSample, double> selector)
    {
        if (_sampleCount == 0)
            return new StatSummary(0.0, 0.0, 0.0, 0.0, 0.0, 0.0);

        double[] values = new double[_sampleCount];
        double sum = 0.0;
        for (int i = 0; i < _sampleCount; i++)
        {
            values[i] = selector(_samples[i]);
            sum += values[i];
        }
        Array.Sort(values);
        return new StatSummary(
            sum / _sampleCount,
            values[0],
            values[^1],
            Percentile(values, 0.50),
            Percentile(values, 0.95),
            Percentile(values, 0.99));
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        int index = Mathf.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1);
        return sorted[index];
    }

    private void WriteCsv(string path)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("frame_ms,fps,main_process_ms,physics_ms,render_cpu_ms,gpu_ms,draw_calls,rendered_objects,node_count,static_memory_bytes,video_memory_bytes,active_physics_objects,collision_pairs");
        for (int i = 0; i < _sampleCount; i++)
        {
            FrameSample s = _samples[i];
            writer.WriteLine(FormattableString.Invariant($"{s.FrameMs:F6},{s.Fps:F3},{s.ProcessMs:F6},{s.PhysicsMs:F6},{s.RenderCpuMs:F6},{s.GpuMs:F6},{s.DrawCalls:F0},{s.RenderObjects:F0},{s.Nodes:F0},{s.StaticMemoryBytes:F0},{s.VideoMemoryBytes:F0},{s.ActivePhysicsObjects:F0},{s.CollisionPairs:F0}"));
        }
    }

    private void ParseArguments()
    {
        foreach (string arg in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()).Distinct())
        {
            if (arg.StartsWith("--perf-scenario=", StringComparison.OrdinalIgnoreCase))
                _scenario = arg["--perf-scenario=".Length..].Trim().ToLowerInvariant();
            else if (arg.StartsWith("--perf-output=", StringComparison.OrdinalIgnoreCase))
                _outputDirectory = arg["--perf-output=".Length..].Trim();
            else if (arg.StartsWith("--perf-warmup=", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(arg["--perf-warmup=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double warmup))
                _warmupSeconds = Math.Max(0.0, warmup);
            else if (arg.StartsWith("--perf-duration=", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(arg["--perf-duration=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out double duration))
                _durationSeconds = Math.Max(1.0, duration);
            else if (arg.Equals("--perf-audio-stress", StringComparison.OrdinalIgnoreCase))
                _audioStress = true;
        }

        if (string.IsNullOrWhiteSpace(_outputDirectory))
            _outputDirectory = ProjectSettings.GlobalizePath("res://artifacts/performance");
    }

    private static bool IsDebugBuild()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
