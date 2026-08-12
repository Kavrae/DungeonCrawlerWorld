namespace Game.Modules.Actions;

/// <summary>
/// Immediate applies its effect immediately then sets the shared ActionLock; Delayed sets the
/// lock first (windup) and only applies once the lock reaches 0, cancellable meanwhile; FreeCast
/// bypasses the shared ActionLock entirely, gated only by its own CooldownFrames if set. Shared
/// by every IActionActivator kind, since a PotionActivator also needs a timing category (always
/// Immediate today).
/// </summary>
public enum ActionTimingCategory
{
    Immediate,
    Delayed,
    FreeCast,
}
