using Engine.Tags;
using Game.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Auras;

/// <summary>How an aura's strength at an entity's position reaches its effects.</summary>
public enum AuraMagnitude : byte
{
    /// <summary>Effects are scaled by the aura's strength there (EffectContext.Magnitude), so they weaken with distance from a source and add where sources overlap.</summary>
    Strength,

    /// <summary>Effects apply at their own amounts anywhere the aura reaches, however strong it is there.</summary>
    Flat,
}

/// <summary>One kind of aura: what it is called, the colour it glows, and what it does to an entity standing inside it.</summary>
/// <remarks>
/// <para>
/// A source radiates an aura by naming its Id with a strength (AuraSourceComponent, TerrainAura,
/// AuraSourceGrant); everything else about the aura is read from here, so every source of one aura
/// glows the same colour and has the same effects.
/// </para>
/// <para>
/// The effects are the same Effect lists an action or item holds, so an aura can do anything those
/// can. AuraSystem applies them from an exposure's tick, once every AuraEffects.TickIntervalFrames
/// while an entity stays in range, with no source entity and attributed to the aura
/// (ActionSource.FromAura). An entry that should hold a level rather than pile up says so itself:
/// StatusEffectGrant with TopUpTo, StatModifierGrant with RefreshFromSameSource.
/// </para>
/// </remarks>
/// <param name="Id">Stable, mod-safe identity. Registering it again replaces the definition.</param>
/// <param name="Effects">What the aura does each tick to an entity in range; none for an aura that only glows.</param>
/// <param name="Tags">What kind of aura this is, for modifiers conditioned on a tag.</param>
public sealed record AuraDefinition(
    Guid Id,
    string Name,
    Color GlowColor,
    IReadOnlyList<Effect>? Effects = null,
    GameplayTagSet Tags = default,
    AuraMagnitude Magnitude = AuraMagnitude.Strength)
{
    public IReadOnlyList<Effect> Effects { get; init; } = Effects ?? [];

    /// <summary>Whether an entity in range holds an exposure to this aura at all.</summary>
    public bool HasEffects => Effects.Count > 0;

    /// <summary>What the aura's damage and healing are named after: its name and "Aura".</summary>
    public string EffectName { get; } = $"{Name} Aura";
}
