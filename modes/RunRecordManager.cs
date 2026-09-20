using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

public static class RunRecordManager
{
    private const string RecordsFilePath = "user://pain_taxi_records.json";
    private static string ResolvedRecordsFilePath => HarnessProfile.Resolve(RecordsFilePath);

    public class RunRecordData
    {
        public int BestShiftNumber { get; set; } = 1;
        public int HighestTotalCash { get; set; } = 0;
        public int HighestSingleFare { get; set; } = 0;
        public int TotalFaresDelivered { get; set; } = 0;
        public int TotalStyleTipsEarned { get; set; } = 0;

        /// <summary>
        /// Best endless distance per run seed, keyed by the seed as text. Seeded runs are
        /// repeatable, so a seed is a fair yardstick for comparing attempts.
        /// </summary>
        public Dictionary<string, int> BestEndlessDistanceBySeed { get; set; } = new();

        /// <summary>Best endless distance across every seed, used for garage unlocks.</summary>
        public int BestEndlessDistance { get; set; } = 0;
    }

    public static RunRecordData Load()
    {
        if (!FileAccess.FileExists(ResolvedRecordsFilePath))
            return new RunRecordData();

        using var file = FileAccess.Open(ResolvedRecordsFilePath, FileAccess.ModeFlags.Read);
        if (file == null)
            return new RunRecordData();

        string json = file.GetAsText();
        try
        {
            RunRecordData data = JsonSerializer.Deserialize<RunRecordData>(json) ?? new RunRecordData();
            // Records written before endless tracking existed have no map for it.
            data.BestEndlessDistanceBySeed ??= new Dictionary<string, int>();
            return data;
        }
        catch
        {
            return new RunRecordData();
        }
    }

    public static void Save(RunRecordData data)
    {
        using var file = FileAccess.Open(ResolvedRecordsFilePath, FileAccess.ModeFlags.Write);
        if (file == null)
            return;

        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        file.StoreString(json);
    }

    /// <summary>
    /// Records an endless run against its seed and reports the previous best, so the
    /// results screen can say whether the attempt beat it.
    /// </summary>
    public static int RecordEndlessRun(int seed, int distanceMeters)
    {
        RunRecordData current = Load();
        string key = seed.ToString(CultureInfo.InvariantCulture);

        current.BestEndlessDistanceBySeed.TryGetValue(key, out int previousBest);
        if (distanceMeters > previousBest)
            current.BestEndlessDistanceBySeed[key] = distanceMeters;

        if (distanceMeters > current.BestEndlessDistance)
            current.BestEndlessDistance = distanceMeters;

        Save(current);
        return previousBest;
    }

    public static int GetEndlessBest(int seed)
    {
        RunRecordData current = Load();
        string key = seed.ToString(CultureInfo.InvariantCulture);
        return current.BestEndlessDistanceBySeed.TryGetValue(key, out int best) ? best : 0;
    }

    public static bool CheckAndRecordRun(int shiftNumber, int totalCash, int singleFare, int faresCount, int styleTips, out List<string> newRecords)
    {
        newRecords = new List<string>();
        RunRecordData current = Load();
        bool isNewRecord = false;

        if (shiftNumber > current.BestShiftNumber)
        {
            current.BestShiftNumber = shiftNumber;
            newRecords.Add($"BEST SHIFT: SHIFT {shiftNumber}");
            isNewRecord = true;
        }

        if (totalCash > current.HighestTotalCash)
        {
            current.HighestTotalCash = totalCash;
            newRecords.Add($"HIGHEST CASH: ${totalCash:N0}");
            isNewRecord = true;
        }

        if (singleFare > current.HighestSingleFare)
        {
            current.HighestSingleFare = singleFare;
            newRecords.Add($"BEST SINGLE FARE: ${singleFare:N0}");
            isNewRecord = true;
        }

        current.TotalFaresDelivered += faresCount;
        current.TotalStyleTipsEarned += styleTips;

        Save(current);
        return isNewRecord;
    }
}
