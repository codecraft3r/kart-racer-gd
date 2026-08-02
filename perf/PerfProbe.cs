using System;
using System.Collections.Generic;
using System.Diagnostics;

public enum PerfHotspot
{
    CameraRainProcess,
    PickupZonePhysics,
    AiControllerPhysics,
    TaxiDropoffSettles,
    AudioWorldOneShot,
    KartSceneSpawn,
    KartGroundRaycasts,
    NetworkInputRpc,
    RetroSpeedometerDraw,
    DrivingVfxProcess,
    VehicleAudioProcess,
    KartSceneLoad
}

public enum PerfEvent
{
    GroundRaycastQuery,
    InputRpcSent,
    SnapshotRpcSent,
    MatchStateRpcSent,
    FullStateRpcSent,
    AudioRpcSent,
    WorldAudioPlayerCreated
}

public readonly record struct PerfProbeSnapshot(
    PerfHotspot Hotspot,
    long Calls,
    double TotalMilliseconds,
    double MaxMilliseconds,
    long AllocatedBytes);

public readonly struct PerfScope : IDisposable
{
    private readonly PerfHotspot _hotspot;
    private readonly long _startTimestamp;
    private readonly long _allocationStart;

    public PerfScope(PerfHotspot hotspot)
    {
        _hotspot = hotspot;
        _startTimestamp = PerfProbe.Begin(out _allocationStart);
    }

    public void Dispose() => PerfProbe.End(_hotspot, _startTimestamp, _allocationStart);
}

/// <summary>
/// Command-line benchmark counters. Disabled during normal play so probes add only one branch.
/// </summary>
public static class PerfProbe
{
    private static readonly long[] Calls = new long[Enum.GetValues<PerfHotspot>().Length];
    private static readonly long[] TotalTicks = new long[Enum.GetValues<PerfHotspot>().Length];
    private static readonly long[] MaxTicks = new long[Enum.GetValues<PerfHotspot>().Length];
    private static readonly long[] AllocatedBytes = new long[Enum.GetValues<PerfHotspot>().Length];
    private static readonly long[] Events = new long[Enum.GetValues<PerfEvent>().Length];

    public static bool Enabled { get; set; }

    public static PerfScope Measure(PerfHotspot hotspot) => new(hotspot);

    public static long Begin(out long allocationStart)
    {
        if (!Enabled)
        {
            allocationStart = 0;
            return 0;
        }

        allocationStart = GC.GetAllocatedBytesForCurrentThread();
        return Stopwatch.GetTimestamp();
    }

    public static void End(PerfHotspot hotspot, long startTimestamp, long allocationStart)
    {
        if (!Enabled || startTimestamp == 0)
            return;

        long elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        int index = (int)hotspot;
        Calls[index]++;
        TotalTicks[index] += elapsed;
        if (elapsed > MaxTicks[index])
            MaxTicks[index] = elapsed;
        AllocatedBytes[index] += GC.GetAllocatedBytesForCurrentThread() - allocationStart;
    }

    public static void Count(PerfEvent perfEvent, long amount = 1)
    {
        if (Enabled)
            Events[(int)perfEvent] += amount;
    }

    public static long GetEventCount(PerfEvent perfEvent) => Events[(int)perfEvent];

    public static IReadOnlyList<PerfProbeSnapshot> Capture()
    {
        var result = new List<PerfProbeSnapshot>(Calls.Length);
        double tickToMs = 1000.0 / Stopwatch.Frequency;
        for (int i = 0; i < Calls.Length; i++)
        {
            result.Add(new PerfProbeSnapshot(
                (PerfHotspot)i,
                Calls[i],
                TotalTicks[i] * tickToMs,
                MaxTicks[i] * tickToMs,
                AllocatedBytes[i]));
        }
        return result;
    }

    public static void Reset()
    {
        Array.Clear(Calls);
        Array.Clear(TotalTicks);
        Array.Clear(MaxTicks);
        Array.Clear(AllocatedBytes);
        Array.Clear(Events);
    }
}
