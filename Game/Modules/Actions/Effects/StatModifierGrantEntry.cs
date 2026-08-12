using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;

namespace Game.Modules.Actions.Effects;

/// <summary>
/// Grants one StatModifierComponent to whichever entity Recipient names -- Target (default,
/// every existing grant's behavior) or Source, see GrantRecipient's own doc comment. No-op when
/// context.StatModifiers isn't wired.
/// </summary>
public sealed record StatModifierGrantEntry(
    StatModifierTarget Target,
    StatModifierOperation Operation,
    StatModifierPolarity Polarity,
    bool CanModify,
    float Magnitude,
    int DurationFrames,
    GrantRecipient Recipient = GrantRecipient.Target) : IActionEffectEntry
{
    public void Apply(ActionEffectContext context)
    {
        if (context.StatModifiers is null)
        {
            return;
        }

        var recipientEntityId = Recipient == GrantRecipient.Source
            ? context.SourceEntityId
            : context.TargetEntityId;

        context.StatModifiers.Add(recipientEntityId, new StatModifierComponent(
            Target, Operation, Polarity, CanModify, Magnitude, DurationFrames, StatusEffectSource.FromEntity(context.SourceEntityId)));
    }
}
