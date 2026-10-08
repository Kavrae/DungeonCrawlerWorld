using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.Events;
using Engine.Math;
using Game.Effects;
using Game.Modules.Mana.Components;
using Game.Modules.StatModifiers.Components;
using Game.Modules.AbilityScores.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.World;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Views;

namespace Tests;

/// <summary>Builds an ActionStateView and the EntityActions under it over a test's own pools and catalogs, with no tiers and no blueprints.</summary>
internal static class TestActionStateViews
{
    public static EntityActions EntityActions(ComponentManager componentManager, ActionCatalog? actionCatalog = null) =>
        Game.Modules.Actions.EntityActions.For(componentManager, actionCatalog ?? new ActionCatalog(), new BlueprintRegistry());

    public static ActionStateView Over(ComponentManager componentManager, ActionCatalog? actionCatalog = null, ItemCatalog? itemCatalog = null) =>
        new(componentManager, EntityActions(componentManager, actionCatalog), itemCatalog ?? new ItemCatalog(), localTierRoster: null, EffectServicesOver(componentManager));

    /// <summary>The services an ActionStateView asks activation effects through, over a test's own mana and stat modifier pools (empty ones where it registered none).</summary>
    public static EffectServices EffectServicesOver(ComponentManager componentManager) =>
        TestActionEffects.Services(componentManager, new EntityKeys(), new EventBus(), new MathUtility(),
            statModifiers: componentManager.IsRegistered<StatModifierComponent>() ? componentManager.GetMultiPool<StatModifierComponent>() : null,
            mana: componentManager.IsRegistered<ManaComponent>() ? componentManager.GetPackedPool<ManaComponent>() : null);

    /// <summary>A TargetingView over a test's own pools and map, resolving through the same TargetResolution activations do. A pool the test didn't register reads as empty; with no key table nothing can be marked, so every selection lands as Ground.</summary>
    public static TargetingView Targeting(ComponentManager componentManager, IMapQuery mapQuery, ActionCatalog? actionCatalog = null, ItemCatalog? itemCatalog = null, EntityKeys? entityKeys = null) =>
        new(new TargetResolution(mapQuery,
                componentManager.IsRegistered<TransformComponent>() ? componentManager.GetDirectPool<TransformComponent>() : EmptyPools.Direct<TransformComponent>(),
                entityKeys ?? new EntityKeys(),
                componentManager.IsRegistered<DeadComponent>() ? componentManager.GetPackedPool<DeadComponent>() : EmptyPools.Packed<DeadComponent>(),
                componentManager.IsRegistered<NonBlockingComponent>() ? componentManager.GetMultiPool<NonBlockingComponent>() : EmptyPools.Multi<NonBlockingComponent>(),
                componentManager.IsRegistered<AbilityScoresComponent>() ? componentManager.GetPackedPool<AbilityScoresComponent>() : EmptyPools.Packed<AbilityScoresComponent>()),
            EntityActions(componentManager, actionCatalog), itemCatalog ?? new ItemCatalog(), componentManager);
}
