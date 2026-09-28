using Game.Modules.StatusEffects;
using Game.World;
using Microsoft.Xna.Framework;

namespace Presentation.UI.ColorPalettes;

/// <summary>The fill colors of floating text.</summary>
/// <remarks>Status effect colors are this consumer's own, matched to the map's dark background, the same way HealthWindow and the HUD each keep theirs (see IStatusEffectDisplay).</remarks>
internal static class FloatingTextPalette
{
    public static readonly Color DamageTakenColor = Color.Red;
    public static readonly Color StatusEffectDamageTakenColor = Color.Orange;
    public static readonly Color HealedColor = Color.LimeGreen;
    public static readonly Color RegeneratedColor = Color.LightGreen;
    public static readonly Color MissedColor = Color.White;
    public static readonly Color CriticalMarkerColor = Color.Yellow;

    public static Color GetColor(FloatingTextKind kind, StatusEffectType effectType) => kind switch
    {
        FloatingTextKind.DamageTaken => DamageTakenColor,
        FloatingTextKind.StatusEffectDamageTaken => StatusEffectDamageTakenColor,
        FloatingTextKind.Healed => HealedColor,
        FloatingTextKind.Regenerated => RegeneratedColor,
        FloatingTextKind.StatusEffectStacksAdded => GetStatusEffectColor(effectType),
        _ => MissedColor,
    };

    public static Color GetStatusEffectColor(StatusEffectType effectType) => effectType switch
    {
        StatusEffectType.Burning => Color.Red,
        StatusEffectType.Poison => Color.LightGreen,
        StatusEffectType.Paralysis => Color.Yellow,
        _ => Color.White,
    };
}
