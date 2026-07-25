namespace Game.Modules.StatusEffects;

/// <summary>
/// Shared "Status Effect (X)" damage-type label, so every status effect that deals damage
/// formats it identically instead of each hardcoding its own string. 
/// </summary>
public static class StatusEffectDamageType
{
    public static string Describe(StatusEffectType effectType) => $"Status Effect ({effectType})";
}
