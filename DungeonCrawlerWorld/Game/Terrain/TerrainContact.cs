using Engine.Tags;
using Game.Effects;
using Game.Modules.Health.Components;

namespace Game.Terrain;

/// <summary>What a terrain does to whatever stands on it: its effects, applied when an entity steps onto a cell and, if RepeatEveryFrames is set, again at that interval for as long as it stays.</summary>
/// <remarks>
/// <para>
/// The effects are the same Effect lists an action or item holds, so a terrain can do anything
/// those can. They are applied with no source entity -- nothing of a caster's scales them, and
/// damage never crits -- and attributed to the terrain (ActionSource.FromTerrain).
/// </para>
/// <para>
/// A buff or protection that holds while an entity is in an area is this with a repeat and a
/// StatModifierGrant that refreshes (StatModifierStacking.RefreshFromSameSource): each application
/// moves its expiry, so it lasts for its own duration after the entity leaves.
/// </para>
/// </remarks>
/// <param name="Effects">Applied in order, every time the contact applies.</param>
/// <param name="Tags">What kind of contact this is (GameTags.DamageFire for lava), for modifiers conditioned on a tag.</param>
/// <param name="RepeatEveryFrames">How often the effects apply again while the entity stays on the terrain; null for a contact that applies only on stepping on.</param>
/// <param name="GroundContactBodyPart">The body part that touches this ground, for the effects that ask for it (BodyPartTargeting.GroundContact): lava's damage and burn land on it. The bottommost part when absent or when the entity has no such part. An effect that doesn't ask is unaffected -- Holy Ground's blessing is the whole entity's.</param>
public sealed record TerrainContact(
    IReadOnlyList<Effect> Effects,
    GameplayTagSet Tags = default,
    ushort? RepeatEveryFrames = null,
    BodyPartType? GroundContactBodyPart = null);
