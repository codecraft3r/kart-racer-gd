using Godot;
using System;
using System.Collections;
using System.Reflection;

/// <summary>
/// Small, opt-in C# surface shared by scene harnesses and runtime exploration. It is
/// deliberately a normal Node rather than an autoload: callers instantiate it and call
/// ConfigureScene before adding the game scene to the tree.
/// </summary>
public partial class HarnessProbe : Node
{
    public const string DefaultSetupMode = "fixture";

    public int ConfiguredSeed { get; private set; } = -1;
    public string ProfileDirectory { get; private set; } = string.Empty;
    public string SetupMode { get; private set; } = DefaultSetupMode;

    /// <summary>
    /// Configure values that must be in place before any scene _Ready methods run.
    /// TrackBuilder.Seed is set on the instantiated scene before its parent is added.
    /// </summary>
    public void ConfigureScene(Node scene, int seed, string profileDir)
    {
        ConfiguredSeed = seed;
        ProfileDirectory = profileDir?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(ProfileDirectory))
            HarnessProfile.Configure(ProfileDirectory);

        // These are process-local defaults. A fresh explicit profile has no settings file,
        // while normal gameplay never enters this method.
        AccessibilitySettings.ReducedMotion = false;

        if (scene == null)
            return;

        TrackBuilder track = FindNode<TrackBuilder>(scene, "TrackBuilder");
        if (track != null && seed >= 0)
            track.Seed = seed;
    }

    /// <summary>Records the setup mode for metadata without changing production behavior.</summary>
    public void SetSetupMode(string setupMode)
    {
        if (!string.IsNullOrWhiteSpace(setupMode))
            SetupMode = setupMode.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Flush managed Godot wrappers after a test has explicitly freed its scene and
    /// autoloads. This is test-only and is never reached by normal gameplay.
    /// </summary>
    public void CollectManagedResources()
    {
        // Godot's managed wrappers can release native resources in more than one
        // finalizer wave (an AudioStream playback wrapper may retain its stream).
        // Drain those waves synchronously so test shutdown does not depend on log I/O.
        for (int pass = 0; pass < 3; pass++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    /// <summary>
    /// Clear AudioManager's private managed caches after its players have been stopped.
    /// This is intentionally exposed only through the opt-in test probe so normal
    /// gameplay keeps its audio cache lifetime unchanged.
    /// </summary>
    public void ReleaseAudioManagerResources()
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null)
            return;

        foreach (Node child in audio.GetChildren())
        {
            if (child is AudioStreamPlayer player)
            {
                player.Stop();
                player.Stream = null;
            }
            else if (child is AudioStreamPlayer3D player3D)
            {
                player3D.Stop();
                player3D.Stream = null;
            }
        }

        ClearManagedField(audio, "_cues");
        ClearManagedField(audio, "_musicNextTracks");
        ClearManagedField(audio, "_localPlayers");
        ClearManagedField(audio, "_worldPlayers");
        SetManagedField(audio, "_cityAmbience", null);
        SetManagedField(audio, "_neonAmbience", null);
        SetManagedField(audio, "_industrialAmbience", null);
        SetManagedField(audio, "_musicA", null);
        SetManagedField(audio, "_musicB", null);
        SetManagedField(audio, "_activeMusic", null);
    }

    /// <summary>
    /// Return typed gameplay state from the shell's normal public observation API. A
    /// missing shell is explicit and never represented as a successful empty capture.
    /// </summary>
    public Godot.Collections.Dictionary Observe(Node scene)
    {
        RetroNeonCabShell shell = FindNode<RetroNeonCabShell>(scene, "RetroNeonCabShell");
        Godot.Collections.Dictionary result = shell?.ObserveHarnessState() ?? new Godot.Collections.Dictionary();
        result["harness_observed"] = shell != null;
        result["configured_seed"] = ConfiguredSeed;
        result["profile_directory"] = ProfileDirectory;
        result["setup_mode"] = SetupMode;
        return result;
    }

    public bool StartTaxiRun(Node scene)
    {
        RetroNeonCabShell shell = FindNode<RetroNeonCabShell>(scene, "RetroNeonCabShell");
        if (shell == null)
            return false;
        shell.StartRun();
        return true;
    }

    public bool StartSeededEndless(Node scene, int seed)
    {
        RetroNeonCabShell shell = FindNode<RetroNeonCabShell>(scene, "RetroNeonCabShell");
        if (shell == null)
            return false;
        shell.StartEndlessRoadWithSeed(seed);
        return true;
    }

    /// <summary>Arrange a passenger fixture by using Kart's production passenger API.</summary>
    public bool ArrangePassenger(Node scene, bool boarding = false)
    {
        Kart kart = FindNode<Kart>(scene, "Kart");
        if (kart == null)
            return false;

        var customer = new GameManager.CustomerData
        {
            Distance = GameManager.CustomerDistance.Moderate,
            Wealth = GameManager.CustomerWealth.Medium,
            Archetype = GameManager.CustomerArchetype.Standard,
            MaxAcceptableDamage = 30,
            GroupSize = 1,
            LoadTime = 1.0f
        };
        kart.BoardPassenger(customer);
        // Keep the fixture on the same production path as PickupZone: boarding also
        // chooses a destination and spawns the drop-off beacon.
        FindNode<TaxiMode>(scene, "TaxiMode")?.OnPassengerBoarded(1, customer);
        kart.SetBoardingProgress(boarding ? 0.55f : 0.0f);
        return true;
    }

    /// <summary>Arrange the taxi at its production destination after ensuring a passenger.</summary>
    public bool ArrangeDropoff(Node scene)
    {
        Kart kart = FindNode<Kart>(scene, "Kart");
        TaxiMode mode = FindNode<TaxiMode>(scene, "TaxiMode");
        if (kart == null || mode == null)
            return false;
        if (!kart.ActivePassenger.HasValue && !ArrangePassenger(scene))
            return false;

        Vector3 destination = mode.GetPlayerDestination(1);
        if (destination == Vector3.Zero)
            return false;
        kart.GlobalPosition = destination + Vector3.Up * 0.65f;
        kart.LinearVelocity = Vector3.Zero;
        return true;
    }

    /// <summary>Arrange a damaged taxi at the nearest production repair shop.</summary>
    public bool ArrangeRepair(Node scene)
    {
        Kart kart = FindNode<Kart>(scene, "Kart");
        TrackBuilder track = FindNode<TrackBuilder>(scene, "TrackBuilder");
        if (kart == null || track == null)
            return false;

        GameManager.Instance?.AwardPayout(1, 500);
        GameManager.Instance?.ApplyVehicleDamage(1, 40);
        RepairShop shop = track.GetNearestRepairShop(kart.GlobalPosition);
        if (shop == null)
            return false;
        kart.GlobalPosition = shop.GlobalPosition + Vector3.Up * 0.5f;
        kart.LinearVelocity = Vector3.Zero;
        return true;
    }

    public bool ArrangeResults(Node scene)
    {
        RetroNeonCabShell shell = FindNode<RetroNeonCabShell>(scene, "RetroNeonCabShell");
        if (shell == null)
            return false;
        shell.ShowResults(1);
        return true;
    }

    private static T FindNode<T>(Node scene, string name) where T : Node
    {
        if (scene == null)
            return null;
        if (scene is T typed && (string.IsNullOrEmpty(name) || typed.Name == name))
            return typed;
        return scene.FindChild(name, true, false) as T;
    }

    private static void ClearManagedField(object target, string fieldName)
    {
        FieldInfo field = typeof(AudioManager).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        object value = field?.GetValue(target);
        if (value is IDictionary dictionary)
            dictionary.Clear();
        else if (value is IList list)
            list.Clear();
        else
            field?.SetValue(target, null);
    }

    private static void SetManagedField(object target, string fieldName, object value)
    {
        FieldInfo field = typeof(AudioManager).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        field?.SetValue(target, value);
    }
}
