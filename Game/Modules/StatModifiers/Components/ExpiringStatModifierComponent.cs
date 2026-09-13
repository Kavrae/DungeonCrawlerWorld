using Engine.ECS.Components;

namespace Game.Modules.StatModifiers.Components;

/// <summary>
/// One per entity that holds at least one expiring StatModifierComponent: the earliest
/// ExpiresAtFrame among them, and the only timer-wheel entry the entity's modifiers need
/// (IScheduledTimer, driven by StatModifierExpirySystem). When it fires, that system sweeps the
/// entity's whole modifier chain, removes everything actually due, and re-arms this to the next
/// earliest deadline -- or lets it be removed when nothing expiring is left.
///
/// One entry per entity rather than per modifier, because a MultiComponentPool instance has no
/// stable identity a wheel entry could name (see IKeyedScheduledTimer) and nothing distinguishes
/// two otherwise-identical modifiers from different sources. It also keeps the wheel's cost
/// proportional to entities with something expiring, not to how many modifiers each one carries.
///
/// Nothing outside StatModifierExpirySystem writes this: it is maintained from the
/// StatModifierComponent pool's own change notification, so every path that grants a timed
/// modifier -- StatModifierEffects.Apply, an action's StatModifierGrant, anything added later --
/// is scheduled without having to remember to. An entity holding only permanent modifiers (e.g.
/// every Goblin's racial damage reduction, see Goblin.Build) never gets one at all.
/// </summary>
/// <param name="nextTickFrame">The earliest ExpiresAtFrame among the entity's modifiers.</param>
public struct ExpiringStatModifierComponent(uint nextTickFrame) : IScheduledTimer
{
    private uint _timerWheelMark;

    /// <summary>The next frame any of this entity's modifiers is due to expire on.</summary>
    public uint NextTickFrame { get; set; } = nextTickFrame;

    uint IScheduledTimer.TimerWheelMark { readonly get => _timerWheelMark; set => _timerWheelMark = value; }

    public override readonly string ToString() => $"NextModifierExpiryFrame : {NextTickFrame}";
}
