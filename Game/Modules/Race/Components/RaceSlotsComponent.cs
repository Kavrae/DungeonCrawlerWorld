using Game.Blueprints;

namespace Game.Modules.Race.Components;

/// <summary>The races an entity is, as BlueprintRegistry race ids -- two slots, since a creature has one or two races.</summary>
/// <remarks>
/// Ids, not names: a race's name, description and appearance live on its race definition, shared by
/// every creature of that race, and repeating them per entity cost 12 MB for four distinct values at
/// the entity counts GameLoop.InitialEntityCapacity is sized for. BlueprintRegistry.None (0) is an
/// empty slot. A third race is dropped rather than stored -- more than two is a case that does not
/// exist today (see TODO.md's deferred-build entry), and growing the component for it would cost
/// every creature.
/// </remarks>
public struct RaceSlotsComponent(ushort race1, ushort race2 = BlueprintRegistry.None)
{
    public const ushort Empty = BlueprintRegistry.None;

    public ushort Race1 { get; set; } = race1;

    public ushort Race2 { get; set; } = race2;

    /// <summary>The race a creature counts as when only one can be meant -- who it looks like.</summary>
    public readonly ushort Primary => Race1;

    public readonly bool Has(ushort raceId) => raceId != Empty && (Race1 == raceId || Race2 == raceId);

    /// <summary>Fills the first empty slot with raceId, unless it is already held or both slots are full.</summary>
    public void Add(ushort raceId)
    {
        if (raceId == Empty || Has(raceId))
        {
            return;
        }

        if (Race1 == Empty)
        {
            Race1 = raceId;
        }
        else if (Race2 == Empty)
        {
            Race2 = raceId;
        }
    }

    public override readonly string ToString() => $"Race1 : {Race1}\nRace2 : {Race2}";
}
