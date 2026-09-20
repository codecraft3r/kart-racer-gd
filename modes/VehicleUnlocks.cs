using System;

/// <summary>
/// Progression rules for the garage. Cars unlock from records the player already earns, so
/// each car has one clear target and there is no second currency to balance or migrate.
/// Keep the table in sync with Kart's vehicle roster: a car added without a rule here
/// inherits the last entry.
/// </summary>
public static class VehicleUnlocks
{
    public readonly record struct Requirement(string Description, int RequiredCash, int RequiredEndlessDistance)
    {
        public bool MetBy(RunRecordManager.RunRecordData records) =>
            records.HighestTotalCash >= RequiredCash &&
            records.BestEndlessDistance >= RequiredEndlessDistance;
    }

    // Indexed by the same order as Kart's vehicle roster.
    private static readonly Requirement[] Requirements =
    {
        new("STARTER CAB", 0, 0),
        new("EARN $2,500 IN A RUN", 2500, 0),
        new("DRIVE 1,500 m ENDLESS", 0, 1500),
        new("DRIVE 4,000 m ENDLESS", 0, 4000),
        new("EARN $25,000 IN A RUN", 25000, 0)
    };

    public static int Count => Requirements.Length;

    public static Requirement Get(int option) => Requirements[Math.Clamp(option, 0, Requirements.Length - 1)];

    public static bool IsUnlocked(int option, RunRecordManager.RunRecordData records) => Get(option).MetBy(records);

    /// <summary>
    /// Index of the next car still locked, or -1 once every car is available. Used to tell
    /// the player what the garage is waiting for.
    /// </summary>
    public static int NextLocked(RunRecordManager.RunRecordData records)
    {
        for (int option = 0; option < Requirements.Length; option++)
        {
            if (!IsUnlocked(option, records))
                return option;
        }

        return -1;
    }
}
