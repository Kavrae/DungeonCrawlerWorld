namespace Game.Modules.Actions;

/// <summary>Why an entity can't use an action or item right now, or None when it can.</summary>
/// <remarks>
/// Only state the entity must change before the action works. Cooldown and action lock are timers, not
/// blockers (see ActivationQueries.FramesUntilReady). Every EffectRefusal an activation effect can give has a
/// blocker of its own (ActivationQueries.BlockerFor), so each is shown with its own text.
/// </remarks>
public enum ActivationBlocker : byte
{
    None,
    NotActivatable,
    MeleeDisabled,
    NoManaPool,
    NotEnoughMana,
    NoHealth,
    NotEnoughHealth,
}
