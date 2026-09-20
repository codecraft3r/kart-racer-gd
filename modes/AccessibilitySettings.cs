/// <summary>
/// Accessibility preferences that other systems read while they draw. Keeping them in one
/// place means camera shake, explosion flashes, and prompt pulses all read the same answer
/// instead of each holding a private flag.
/// </summary>
public static class AccessibilitySettings
{
    /// <summary>
    /// When set, transient flashes and camera shake are damped. Timing, damage, and scoring
    /// are untouched; only the intensity of the visual response changes.
    /// </summary>
    public static bool ReducedMotion { get; set; }
}
