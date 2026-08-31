using Engine.ECS.Components;

namespace Game.Modules.StatusEffects;

/// <summary>
/// Lets a concrete status-effect module (Burning, Poison, Paralysis, or any future effect) plug
/// its own glyph and remaining-duration formula into a shared display lookup, so no central
/// Presentation code needs to hardcode a switch over every concrete effect type (see
/// HealthWindow/PlayerStatusEffectsContent's own doc comments for the two consumers). Registered
/// via StatusEffectDisplayRegistry during IGameModule.Configure (see BurningModule/PoisonModule/
/// ParalysisModule) -- the exact same shape as IStatusEffectAuraApplier, for display instead of
/// application.
///
/// Deliberately does NOT include Color: each display consumer's icon/text has its own genuinely
/// different contrast requirement against its own genuinely different background
/// (PlayerStatusEffectsContent's icon reads against a white tile, HealthWindow's text reads
/// against WindowPalette.PanelBackgroundColor's dark background), so color stays a small local
/// switch in each consumer -- low-risk if a case is missed (falls back to a default color, not a
/// missing/wrong duration or icon). Only true facts about the effect itself (identity glyph, real
/// duration) belong here, not a presentation choice that legitimately varies per consumer.
/// </summary>
public interface IStatusEffectDisplay
{
    StatusEffectType EffectType { get; }
    string Glyph { get; }

    /// <summary>This entity's current remaining duration for EffectType, in frames, or null if it isn't actually active on this entity (no timer component present).</summary>
    int? GetRemainingDurationFrames(ComponentManager componentManager, int entityId);

    /// <summary>This entity's current stack count for EffectType, or 0 if it isn't active.</summary>
    int GetStackCount(ComponentManager componentManager, int entityId);
}
