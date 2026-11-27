using Engine.ECS.Components;
using Engine.ECS.Properties;

namespace Game.Modules.Auras.Components;

/// <summary>When an entity standing inside auras next has them applied: one tick for every aura it is exposed to.</summary>
/// <remarks>
/// Held by an entity while it holds any AuraExposureComponent. Once a second (AuraEffects.TickIntervalFrames),
/// staggered by entity id, AuraSystem applies each aura the entity is exposed to and drops each that no longer reaches
/// it; with none left, it removes this. A timer-wheel timer (IScheduledTimer), so only the entities due on a frame are
/// touched, whatever their tier.
/// </remarks>
/// <param name="nextTickFrame">The simulation frame of the entity's next tick (FrameDeadline).</param>
[DescribedComponent]
public partial struct AuraExposureTickComponent(uint nextTickFrame) : IScheduledTimer
{
    private uint _timerWheelMark;

    /// <summary>The simulation frame of the entity's next tick (FrameDeadline).</summary>
    [PropertyMeaning(ComponentPropertyMeaningNames.Frame)]
    public uint NextTickFrame { get; set; } = nextTickFrame;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }
}
