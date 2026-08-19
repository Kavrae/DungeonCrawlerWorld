namespace Presentation.UI;

/// <summary>
/// Name&lt;-&gt;UiLayer conversion for anywhere a layer needs to survive outside this process (e.g.
/// a saved window's persisted layer, see UiLayer's own doc comment on why name, not the raw int,
/// is the actual save-compat contract) -- centralizes on Enum.TryParse rather than scattering it
/// across every save/load call site, so a future need (a renamed-tier alias table, a safe
/// fallback for a tier an old save references that's since been removed) only has one place to
/// add it.
/// </summary>
public static class UiLayerNameParser
{
    /// <summary>
    /// Parses a persisted layer name back to its UiLayer, falling back to fallback if name is
    /// null, empty, or not a currently-recognized tier. Guards against Enum.TryParse's own
    /// gotcha of also accepting a bare numeric string (e.g. "2000") and returning it as a valid
    /// UiLayer even when that number isn't actually one of this enum's declared members --
    /// Enum.IsDefined closes that hole. A save load should degrade a window to a plausible
    /// default layer rather than throw and abort the whole load over one stale/corrupted value.
    /// </summary>
    public static UiLayer Parse(string? name, UiLayer fallback = UiLayer.DynamicHud) =>
        !string.IsNullOrEmpty(name) && Enum.TryParse<UiLayer>(name, ignoreCase: false, out var layer) && Enum.IsDefined(layer)
            ? layer
            : fallback;

    /// <summary>The stable, persisted name for layer -- just its own enum member name, but named and centralized so a save writer doesn't need to know that's the mechanism, and so a future rename/alias policy has one place to live instead of every writer calling ToString() directly.</summary>
    public static string ToPersistedName(UiLayer layer) => layer.ToString();
}
