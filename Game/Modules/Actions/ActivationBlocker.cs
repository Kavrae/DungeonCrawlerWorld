namespace Game.Modules.Actions;

/// <summary>Why an entity can't use an action or item right now, or None when it can.</summary>
/// <remarks>Only state the entity must change before the action works. Cooldown and action lock are timers, not blockers (see ActivationQueries.FramesUntilReady).</remarks>
public enum ActivationBlocker : byte
{
    None,
    NotActivatable,
    MeleeDisabled,
    NotEnoughMana,
}
