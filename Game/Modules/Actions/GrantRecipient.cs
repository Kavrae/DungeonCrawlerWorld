namespace Game.Modules.Actions;

/// <summary>
/// Who a StatModifierGrantEntry's grant lands on. Target (default) is every existing grant's
/// behavior today -- whoever the action resolved against. Source lets an entry buff the caster
/// instead, e.g. a self-targeted, stacking crit-chance buff granted by the attacker's own attack
/// landing (see PLAN-action-effect-activator.md's "Double Tap" worked example) -- there was no
/// way to express this before Recipient existed, since every grant always targeted the resolved
/// target.
/// </summary>
public enum GrantRecipient
{
    Target,
    Source,
}
