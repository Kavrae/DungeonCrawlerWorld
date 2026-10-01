using Engine.ECS.Components;
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
        new(componentManager, EntityActions(componentManager, actionCatalog), itemCatalog ?? new ItemCatalog(), localTierRoster: null);
}
