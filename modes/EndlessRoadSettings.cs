using Godot;

public partial class EndlessRoadSettings : Resource
{
    [Export] public int RunSeed = -1;
    [Export] public float OpeningSpeed = 24.0f;
    [Export] public float MaxSpeed = 44.0f;
    [Export] public float SpeedRampPerSecond = 0.08f;
    [Export] public float LaneWidth = 3.2f;
    [Export] public int LaneCount = 5;
    [Export] public float ChunkLength = 80.0f;
    [Export] public int ActiveChunksAhead = 5;
    [Export] public int ActiveChunksBehind = 2;
    [Export] public float VehicleHealth = 100.0f;
    [Export] public float ImpactDamageGlance = 6.0f;
    [Export] public float ImpactDamageBump = 14.0f;
    [Export] public float ImpactDamageCrash = 28.0f;
    [Export] public float InvulnerabilitySeconds = 1.2f;
    [Export] public float OffroadSlowFactor = 0.35f;
    [Export] public float OffroadDamagePerSecond = 8.0f;
    [Export] public float BoostMaxCharge = 1.0f;
    [Export] public float BoostConsumePerSecond = 0.55f;
    [Export] public float BoostRechargePerSecond = 0.18f;
    [Export] public float BoostSpeedFactor = 1.35f;
    [Export] public float BoostAccelerationFactor = 1.6f;
    [Export] public float ChainWindowSeconds = 3.5f;
    [Export] public float ChainDecayPerSecond = 0.25f;
    [Export] public int MaxMultiplier = 8;
    [Export] public int StartingTrafficPerChunk = 3;
    [Export] public int MaxTrafficPerChunk = 5;
    [Export] public int StartingRivals = 1;
    [Export] public int MaxRivals = 4;
    [Export] public float RivalSkillStart = 0.25f;
    [Export] public float RivalSkillMax = 0.85f;
    [Export] public float FirstHazardDelay = 8.0f;
    [Export] public float FirstHazardMinimumDistance = 45.0f;
}
