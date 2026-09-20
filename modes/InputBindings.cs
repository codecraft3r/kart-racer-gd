using Godot;

/// <summary>
/// Player-editable keyboard bindings, stored in the same config file as the other
/// settings. Only keyboard events are replaced: joypad events declared in project.godot
/// are left alone, so rebinding a key never costs a pad player their controls.
/// </summary>
public static class InputBindings
{
    private const string SettingsPath = "user://pain_taxi_settings.cfg";
    private const string Section = "input";
    private static string ResolvedSettingsPath => HarnessProfile.Resolve(SettingsPath);

    public readonly record struct Binding(string Action, string Label, Key DefaultKey);

    // fire_weapon is declared here rather than in project.godot because it was missing from
    // the input map entirely, which made every IsActionPressed check on it report an error.
    private static readonly Binding[] Bindings =
    {
        new("move_forward", "ACCELERATE", Key.W),
        new("move_backward", "BRAKE / REVERSE", Key.S),
        new("move_left", "STEER LEFT", Key.A),
        new("move_right", "STEER RIGHT", Key.D),
        new("drift", "DRIFT", Key.Space),
        new("boost", "BOOST", Key.Shift),
        new("fire_weapon", "FIRE WEAPON", Key.F)
    };

    public static int Count => Bindings.Length;

    public static Binding Get(int index) => Bindings[Mathf.Clamp(index, 0, Bindings.Length - 1)];

    private static Key GetBoundKey(string action)
    {
        if (!InputMap.HasAction(action))
            return Key.None;

        foreach (InputEvent existing in InputMap.ActionGetEvents(action))
        {
            if (existing is InputEventKey keyEvent && keyEvent.PhysicalKeycode != Key.None)
                return keyEvent.PhysicalKeycode;
        }

        return Key.None;
    }

    public static string Describe(int index) => DescribeAction(Get(index).Action);

    public static string DescribeAction(string action)
    {
        Key key = GetBoundKey(action);
        return key == Key.None ? "UNBOUND" : OS.GetKeycodeString(key).ToUpperInvariant();
    }

    /// <summary>
    /// Applies the saved bindings, falling back to the defaults for anything never saved.
    /// Safe to call more than once.
    /// </summary>
    public static void LoadAndApply()
    {
        var config = new ConfigFile();
        bool loaded = config.Load(ResolvedSettingsPath) == Error.Ok;

        foreach (Binding binding in Bindings)
        {
            Key key = binding.DefaultKey;
            if (loaded && config.HasSectionKey(Section, binding.Action))
                key = (Key)config.GetValue(Section, binding.Action, (int)binding.DefaultKey).AsInt32();

            ApplyKey(binding.Action, key);
        }
    }

    public static void Save()
    {
        var config = new ConfigFile();
        // Merge rather than overwrite so the video and audio sections survive.
        config.Load(ResolvedSettingsPath);

        foreach (Binding binding in Bindings)
            config.SetValue(Section, binding.Action, (int)GetBoundKey(binding.Action));

        Error error = config.Save(ResolvedSettingsPath);
        if (error != Error.Ok)
            GD.PushWarning($"InputBindings: could not save bindings ({error}).");
    }

    /// <summary>
    /// Rebinds one action, refusing a key another action already uses. Returns false and
    /// names the clashing action so the UI can say which one holds the key.
    /// </summary>
    public static bool TryRebind(int index, Key key, out string conflictLabel)
    {
        conflictLabel = string.Empty;
        if (key == Key.None)
            return false;

        for (int other = 0; other < Bindings.Length; other++)
        {
            if (other == index)
                continue;

            if (GetBoundKey(Bindings[other].Action) == key)
            {
                conflictLabel = Bindings[other].Label;
                return false;
            }
        }

        ApplyKey(Bindings[index].Action, key);
        Save();
        return true;
    }

    public static void ResetToDefaults()
    {
        foreach (Binding binding in Bindings)
            ApplyKey(binding.Action, binding.DefaultKey);

        Save();
    }

    private static void ApplyKey(string action, Key key)
    {
        if (!InputMap.HasAction(action))
            InputMap.AddAction(action);

        // ActionGetEvents returns a copy, so erasing while iterating is safe.
        foreach (InputEvent existing in InputMap.ActionGetEvents(action))
        {
            if (existing is InputEventKey)
                InputMap.ActionEraseEvent(action, existing);
        }

        if (key != Key.None)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = key });
    }
}
