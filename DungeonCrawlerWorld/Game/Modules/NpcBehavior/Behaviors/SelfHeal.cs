using Engine.Decisions;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Tags;

namespace Game.Modules.NpcBehavior.Behaviors;

/// <summary>Heals itself when its health falls below the grant's Parameter, with the best self-heal it can use now.</summary>
/// <remarks>
/// A self-heal is an action or an item tagged Effect.Healing and Targeting.Self. Renewable before non-renewable, stronger
/// before weaker: the strongest spell (an action it holds), then the strongest consumable (an item it holds, any but a
/// toggle) -- mana comes back and a potion doesn't. Strength is the health its DirectHeal entries restore to it (percent
/// of its maximum, plus flat); equal strengths keep the order it holds them in. Only one it can use now counts (no
/// blocker -- mana for a spell, a null zone, no cooldown), so a creature in Anti-Literacy drinks rather than reads, and
/// one out of mana doesn't try to cast. Nothing is rolled.
/// </remarks>
public sealed class SelfHeal() : NpcBehavior(BuiltInBehaviorIds.SelfHeal, "Self-heal", [NpcDrives.Caution])
{
    public override BehaviorEvaluation Evaluate(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        if (context.HealthFraction() >= grant.Parameter || !TryChoose(ref context))
        {
            return BehaviorEvaluation.DoesNotApply;
        }

        return Applies(grant);
    }

    public override void Act(ref NpcDecisionContext context, NpcBehaviorGrant grant)
    {
        var services = context.Services;
        var entityId = context.EntityId;
        if (context.Findings.SelfHealActionId != Guid.Empty)
        {
            if (services.Actions.TryGetEffectiveAction(entityId, context.Findings.SelfHealActionId, out var action))
            {
                context.QueueAction(action.Id, services.Targeting.SelectEntity(entityId, action.Activator, entityId));
            }

            return;
        }

        if (InventoryQueries.TryFindByStackInstanceId(services.Stacks, entityId, context.Findings.SelfHealStackInstanceId, out var stack) &&
            InventoryQueries.TryResolveEffectiveItem(services.Items, in stack, out var item) &&
            item.Activator is { } activator)
        {
            context.QueueItemActivation(stack.StackInstanceId, services.Targeting.SelectEntity(entityId, activator, entityId));
        }
    }

    /// <summary>Finds the self-heal it would use, keeping it in Findings; false when it has none it can use now.</summary>
    private static bool TryChoose(ref NpcDecisionContext context)
    {
        var services = context.Services;
        var entityId = context.EntityId;
        var maximumHealth = HealthQueries.TryGetEffectiveMaximum(services.Health, services.BodyParts, services.Effects.StatModifiers, entityId, out var maximum) ? maximum : 0f;

        var bestStrength = float.NegativeInfinity;
        services.CollectActions(entityId, GameTags.EffectHealing, GameTags.TargetingSelf, services.CandidateActions);
        foreach (var action in services.CandidateActions)
        {
            var strength = HealStrength(action, maximumHealth);
            if (strength > bestStrength && services.IsUsable(entityId, action, context.Now))
            {
                bestStrength = strength;
                context.Findings.SelfHealActionId = action.Id;
            }
        }

        if (context.Findings.SelfHealActionId != Guid.Empty)
        {
            return true;
        }

        var chosenItem = false;
        var stacks = services.Stacks;
        for (var denseIndex = stacks.GetFirstDenseIndex(entityId); denseIndex != -1; denseIndex = stacks.GetNextDenseIndex(denseIndex))
        {
            ref readonly var stack = ref stacks.GetReadonlyByDenseIndex(denseIndex);
            if (!InventoryQueries.TryResolveEffectiveItem(services.Items, in stack, out var item) ||
                item.Activator is null or ToggleItemActivator ||
                !item.Tags.Has(GameTags.EffectHealing) || !item.Tags.Has(GameTags.TargetingSelf))
            {
                continue;
            }

            var strength = HealStrength(item, maximumHealth);
            if (strength > bestStrength && services.IsUsable(entityId, in stack, item, context.Now))
            {
                bestStrength = strength;
                context.Findings.SelfHealStackInstanceId = stack.StackInstanceId;
                chosenItem = true;
            }
        }

        return chosenItem;
    }

    /// <summary>The health definition's DirectHeal entries restore to an entity of maximumHealth, before modifiers.</summary>
    private static float HealStrength(ActivatableDefinition definition, float maximumHealth)
    {
        var strength = 0f;
        foreach (var effect in definition.Effects)
        {
            foreach (var entry in effect.Entries)
            {
                if (entry is DirectHeal heal)
                {
                    strength += (heal.PercentOfMaxHealth * maximumHealth) + heal.FlatAmount;
                }
            }
        }

        return strength;
    }
}
