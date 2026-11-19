using Engine.ECS.Components;
using Engine.ECS.Properties;

namespace Game.Modules.TerrainContacts.Components;

/// <summary>
/// Present on an entity only while it stands on terrain whose contact repeats -- added/refreshed on
/// contact, removed when it steps off (see TerrainContactSystem). Holds which terrain it is
/// standing on and the next repeat's frame; the effects and the interval are read from that
/// terrain's definition every time they are needed.
/// </summary>
/// <remarks>A timer-wheel timer (IScheduledTimer): writing NextTickFrame is all it takes to schedule it.</remarks>
[DescribedComponent]
public partial struct TerrainContactExposureComponent(uint nextTickFrame, ushort terrainTypeId) : IScheduledTimer
{
    private uint _timerWheelMark;

    /// <summary>The simulation frame the terrain's contact next applies again (FrameDeadline).</summary>
    [PropertyMeaning(ComponentPropertyMeaningNames.Frame)]
    public uint NextTickFrame { get; set; } = nextTickFrame;

    /// <summary>The TerrainRegistry id of the terrain the entity is standing on.</summary>
    [PropertyMeaning(GamePropertyMeanings.TerrainTypeId)]
    public ushort TerrainTypeId { get; set; } = terrainTypeId;

    /// <summary>Whether the contact's latest application landed nothing because the entity refused it (an immunity). While set, the refusal is not reported again.</summary>
    public bool Refused { get; set; }

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }
}
