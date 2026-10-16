namespace UIEngine.Rendering;

/// <summary>The "current / maximum" text drawn on a resource bar.</summary>
/// <remarks>
/// Takes numbers already rounded for display, as the caller's rules round them. The displayed current is clamped to
/// [0, displayed maximum]. A bar keeps it in a KeyedCache keyed on the pair, so it is built only when a number changes.
/// </remarks>
public static class ResourceBarValueText
{
    public static string Format(int displayedCurrent, int displayedMaximum)
    {
        displayedMaximum = Math.Max(0, displayedMaximum);
        displayedCurrent = Math.Clamp(displayedCurrent, 0, displayedMaximum);
        return $"{displayedCurrent} / {displayedMaximum}";
    }
}
