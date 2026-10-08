using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.AbilityScores;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Actions.Activators;

namespace Game.Modules.Inventory;

/// <summary>
/// Grant-time entry point for a wand -- bakes the recipient's current Intelligence into
/// MaxCharges/Charges once, at the moment of grant ("scales with the user's Intelligence at the
/// time of obtaining the item"), never recomputed later even if Intelligence changes afterward.
/// Falls back to Intelligence 1 (WandActivationEffects' own floor) when AbilityScoresModule isn't
/// wired or the recipient has no Intelligence score -- the same defensive shape
/// ItemActivationSystem.ComputeScrollScaleMultiplier already uses for Scroll scaling.
/// </summary>
public static class WandGrantEffects
{
    private const ushort FallbackIntelligenceTotal = 1;

    /// <returns>The StackInstanceId of whichever stack the last granted wand ended up in.</returns>
    public static uint Grant(ComponentManager componentManager, PackedComponentPool<AbilityScoresComponent> abilityScores, int entityId, ItemDefinition baseDefinition, ushort quantity)
    {
        var intelligenceTotal = AbilityScoreQueries.TryGetComponent(abilityScores, entityId, AbilityScoreType.Intelligence, out var intelligence)
            ? intelligence.Total
            : FallbackIntelligenceTotal;

        var maxCharges = WandActivationEffects.ComputeMaxCharges(intelligenceTotal);
        var baseActivator = (WandActivator)baseDefinition.Activator!;
        var grantedDefinition = baseDefinition with { Activator = baseActivator with { Charges = maxCharges, MaxCharges = maxCharges } };

        return InventoryActions.AddItemWithOverride(componentManager, entityId, grantedDefinition, quantity);
    }
}
