using Engine.ECS.Components;

namespace Game.Modules.ContactDamage.Components;

/// <summary>
/// Present on an entity only while it stands on terrain with a ContactHazard -- added/refreshed on
/// contact, removed when it steps off (see ContactDamageSystem). Caches only which terrain granted
/// the exposure and the next tick's frame; the damage and interval are read from that terrain's
/// definition when needed, since a definition never changes during a session.
/// </summary>
/// <remarks>A timer-wheel timer (IScheduledTimer): writing NextTickFrame is all it takes to schedule it.</remarks>
public struct ContactDamageExposureComponent(uint nextTickFrame, ushort hazardTerrainTypeId) : IScheduledTimer
{
    private uint _timerWheelMark;

    /// <summary>The simulation frame of the next contact-damage tick (FrameDeadline).</summary>
    public uint NextTickFrame { get; set; } = nextTickFrame;

    /// <summary>The TerrainRegistry id of the hazard terrain the entity is standing on.</summary>
    public ushort HazardTerrainTypeId { get; set; } = hazardTerrainTypeId;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"NextTickFrame : {NextTickFrame}\nHazardTerrainTypeId : {HazardTerrainTypeId}";
}
