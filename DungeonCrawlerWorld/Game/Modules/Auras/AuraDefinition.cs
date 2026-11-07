using Engine.Tiles;
using Engine.Tags;
using Game.Effects;
using Microsoft.Xna.Framework;

namespace Game.Modules.Auras;

/// <summary>How an aura's value at an entity's position reaches its effects.</summary>
public enum AuraMagnitude : byte
{
    /// <summary>Effects are scaled by the aura's value there (EffectContext.Magnitude), so they follow its falloff and add where sources overlap.</summary>
    Power,

    /// <summary>Effects apply at their own amounts anywhere the aura reaches, however strong it is there.</summary>
    Flat,
}

/// <summary>One kind of aura: what it is called, the colour it glows, and what it does to an entity standing inside it.</summary>
/// <remarks>
/// <para>
/// A source radiates an aura by naming its Id with a power and a size (AuraSourceComponent, TerrainAura,
/// AuraSourceGrant); everything else about the aura is read from here, so every source of one aura
/// glows the same colour, falls off the same way and has the same effects.
/// </para>
/// <para>
/// The effects are the same Effect lists an action or item holds, so an aura can do anything those
/// can. AuraSystem applies them from an exposure's tick, once every AuraEffects.TickIntervalFrames
/// while an entity stays in range, with no source entity, credited to the strongest contributor there
/// (AuraField.Attribute). An entry that should hold a level rather than pile up says so itself:
/// StatusEffectGrant with TopUpTo, StatModifierGrant with RefreshFromSameSource.
/// </para>
/// </remarks>
/// <param name="Id">Stable, mod-safe identity: a later content layer's record with it replaces this one when a session loads, and within a session its first registration stands (AuraCatalog).</param>
/// <param name="Effects">What the aura does each tick to an entity in range; none for an aura that only glows.</param>
/// <param name="Tags">What kind of aura this is, for modifiers conditioned on a tag.</param>
/// <param name="Falloff">How each source's value changes from its power at the source to the edge of its size.</param>
/// <param name="Description">What being in the aura does to whoever stands in it, written for the player; empty for none.</param>
/// <param name="GlowOpacityMultiplier">The glow's opacity where the aura is at full power, scaled down with its power below that; half by default, so whatever the glow lies on still reads through. From 0 to 1; the glow clamps anything outside that.</param>
public sealed record AuraDefinition(
    Guid Id,
    string Name,
    Color GlowColor,
    IReadOnlyList<Effect>? Effects = null,
    GameplayTagSet Tags = default,
    AuraMagnitude Magnitude = AuraMagnitude.Power,
    AuraFalloff Falloff = AuraFalloff.Linear,
    float GlowOpacityMultiplier = 0.5f,
    string Description = "")
{
    public IReadOnlyList<Effect> Effects { get; init; } = Effects ?? [];

    /// <summary>Whether an entity in range holds an exposure to this aura at all.</summary>
    public bool HasEffects => Effects.Count > 0;

    /// <summary>What the aura's damage and healing are named after: its name and "Aura".</summary>
    public string EffectName { get; } = $"{Name} Aura";

    /// <summary>Equal when every field is, and the effects are, in order, whichever lists hold them.</summary>
    public bool Equals(AuraDefinition? other) =>
        other is not null &&
        Id == other.Id &&
        Name == other.Name &&
        Description == other.Description &&
        GlowColor == other.GlowColor &&
        Tags == other.Tags &&
        Magnitude == other.Magnitude &&
        Falloff == other.Falloff &&
        GlowOpacityMultiplier == other.GlowOpacityMultiplier &&
        Effects.SequenceEqual(other.Effects);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Name);
        hash.Add(Description);
        hash.Add(GlowColor);
        hash.Add(Tags);
        hash.Add(Magnitude);
        hash.Add(Falloff);
        hash.Add(GlowOpacityMultiplier);
        foreach (var effect in Effects)
        {
            hash.Add(effect);
        }

        return hash.ToHashCode();
    }
}
