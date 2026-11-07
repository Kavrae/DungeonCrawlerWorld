namespace Game.Modules.StatusEffects;

/// <summary>The shared "Status Effect (X)" label for damage or healing a status effect does, so every effect formats it identically.</summary>
public static class StatusEffectLabel
{
    public static string Describe(StatusEffectType effectType) => $"Status Effect ({StatusEffectTypeFacts.DisplayNameOf(effectType)})";
}
