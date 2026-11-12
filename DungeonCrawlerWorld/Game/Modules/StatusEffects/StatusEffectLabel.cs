namespace Game.Modules.StatusEffects;

/// <summary>The shared "Status Effect (X)" label for damage or healing a status effect does, so every effect formats it identically.</summary>
public static class StatusEffectLabel
{
    private static readonly Dictionary<StatusEffectType, string> LabelsByEffectType =
        Enum.GetValues<StatusEffectType>().ToDictionary(static effectType => effectType, Format);

    public static string Describe(StatusEffectType effectType) =>
        LabelsByEffectType.TryGetValue(effectType, out var label) ? label : Format(effectType);

    private static string Format(StatusEffectType effectType) => $"Status Effect ({StatusEffectTypeFacts.DisplayNameOf(effectType)})";
}
