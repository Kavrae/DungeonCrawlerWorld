using Engine.ECS.Components;
using Engine.ECS.Properties;
using Game.World;

namespace Game.Modules.Auras.Components;

/// <summary>One aura an entity is standing inside.</summary>
/// <remarks>
/// One instance per (entity, aura): an entity inside several auras holds one each. AuraSystem adds it when the entity
/// comes into range of the field, or a pulse reaches it, and the entity's one tick (AuraExposureTickComponent) applies
/// every aura it holds and removes each that no longer reaches it. The field's power is read fresh at every tick, and so
/// are the effects, from the aura's definition; what a pulse source gives is held here, as the pulse wrote it, until
/// its record runs out (PulseExpiresAtFrame). Found by aura with AuraExposureMatcher.
/// </remarks>
[DescribedComponent]
public partial struct AuraExposureComponent(byte auraId)
{
    [ReadOnlyProperty]
    [PropertyMeaning(GamePropertyMeanings.AuraId)]
    public byte AuraId { get; set; } = auraId;

    /// <summary>Whether the aura's latest tick landed nothing because the entity refused it (an immunity). While set, the refusal is not reported again.</summary>
    public bool Refused { get; set; }

    /// <summary>The strongest value a pulse source of the aura gave the entity while its record is live (PulseExpiresAtFrame); the tick takes it or the field's value, whichever is stronger.</summary>
    public ushort PulsePower { get; set; }

    /// <summary>The frame the pulse record stops counting: one pulse period of its source after the pulse, plus one, so a target ticks it every second however slowly its source pulses.</summary>
    [PropertyMeaning(ComponentPropertyMeaningNames.Frame)]
    public uint PulseExpiresAtFrame { get; set; }

    /// <summary>Whom the pulse record's power is credited to: the carrier of the source that wrote it, as of that pulse.</summary>
    public ActionSource PulseCredit { get; set; }
}

/// <summary>Matches an entity's exposure to one aura.</summary>
public readonly struct AuraExposureMatcher(byte auraId) : IComponentMatcher<AuraExposureComponent>
{
    public bool Matches(in AuraExposureComponent exposure) => exposure.AuraId == auraId;
}
