using Engine.ECS.Components;
using Engine.ECS.Properties;

namespace Game.Modules.TerrainContacts.Components;

/// <summary>
/// One terrain whose contact repeats that an entity stands on -- added or refreshed when it steps on, removed when no
/// tile of its footprint is on that terrain any more (see TerrainContactSystem). Holds which terrain it is and the next
/// repeat's frame; the effects and the interval are read from that terrain's definition every time they are needed.
/// </summary>
/// <remarks>
/// A Multi pool, one instance per (entity, terrain): a large entity standing on lava with one tile and on Holy Ground
/// with another holds one of each, each repeating at its own terrain's interval. A keyed timer-wheel timer
/// (IKeyedScheduledTimer), keyed by TerrainTypeId: writing NextTickFrame is all it takes to schedule it.
/// </remarks>
[DescribedComponent]
public partial struct TerrainContactExposureComponent(uint nextTickFrame, ushort terrainTypeId) : IKeyedScheduledTimer
{
    private uint _timerWheelMark;

    /// <summary>The simulation frame the terrain's contact next applies again (FrameDeadline).</summary>
    [PropertyMeaning(ComponentPropertyMeaningNames.Frame)]
    public uint NextTickFrame { get; set; } = nextTickFrame;

    /// <summary>The TerrainRegistry id of the terrain the entity is standing on.</summary>
    [ReadOnlyProperty]
    [PropertyMeaning(GamePropertyMeanings.TerrainTypeId)]
    public ushort TerrainTypeId { get; set; } = terrainTypeId;

    /// <summary>Whether the contact's latest application landed nothing because the entity refused it (an immunity). While set, the refusal is not reported again.</summary>
    public bool Refused { get; set; }

    readonly int IKeyedScheduledTimer.TimerKey => TerrainTypeId;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }
}
