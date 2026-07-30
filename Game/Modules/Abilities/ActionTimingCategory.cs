namespace Game.Modules.Abilities;

/// <summary>
/// How an ability's effect timing relates to the shared ActionLock (see
/// Game.Modules.Core.Components.ActionLockGate): Immediate applies its effect then locks;
/// Delayed locks first and applies its effect only once the lock ends (cancellable meanwhile);
/// FreeCast bypasses the shared lock entirely and is gated only by its own
/// AbilityTiming.CooldownFrames, if any.
/// </summary>
public enum ActionTimingCategory
{
    Immediate,
    Delayed,
    FreeCast,
}
