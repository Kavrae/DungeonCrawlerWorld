using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Engine.Tiles;
using Game.Effects;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using Tests.UIEngine;
using UIEngine.Rendering;

namespace Tests.Presentation;

/// <summary>The hotbar and targeting read the action the player actually has -- its override -- not the catalog entry.</summary>
[TestClass]
public sealed class EffectiveActionPresentationTests
{
    private const int PlayerEntityId = TestMapWindows.PlayerEntityId;
    private static readonly Guid SpellId = new("3f1d8b62-0a4e-4c97-b5d3-8e26f1a7c049");

    private static readonly ActionDefinition CatalogSpell = new(
        SpellId, "Catalog Spell", null, "*", Color.White, [],
        Effects: [Effect.None],
        Activator: new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 1), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: 0)));

    private static ActionCatalog Catalog()
    {
        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(CatalogSpell);
        return actionCatalog;
    }

    [TestMethod]
    public void HotbarSummary_NamesThePlayersOverride()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(SpellId, overrideDefinition: CatalogSpell with { Name = "Overridden Spell" }));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, SpellId));
        var itemCatalog = new ItemCatalog();
        var hotbar = new HotbarContent(
            world, new MapViewState(), new HotkeyBindingView(componentManager), new InventoryView(componentManager, itemCatalog), TestActionStateViews.Over(componentManager, Catalog(), itemCatalog), new HotkeyBindingCommands(componentManager, itemCatalog, new EventBus()), itemCatalog,
            TestFonts.Shared, new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver), new Vector2(2000, 2000), simulationClock: new SimulationClock());

        Assert.IsTrue(hotbar.TryGetSlotSummary(HotkeySlot.Slot1, out var title, out _, out _));
        Assert.AreEqual("Overridden Spell", title);
    }

    [TestMethod]
    public void HotbarSlot_BoundToAnActionThePlayerDoesNotHave_DescribesNothing()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, SpellId));
        var itemCatalog = new ItemCatalog();
        var hotbar = new HotbarContent(
            world, new MapViewState(), new HotkeyBindingView(componentManager), new InventoryView(componentManager, itemCatalog), TestActionStateViews.Over(componentManager, Catalog(), itemCatalog), new HotkeyBindingCommands(componentManager, itemCatalog, new EventBus()), itemCatalog,
            TestFonts.Shared, new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver), new Vector2(2000, 2000), simulationClock: new SimulationClock());

        Assert.IsFalse(hotbar.TryGetSlotSummary(HotkeySlot.Slot1, out _, out _, out _));
    }

    [TestMethod]
    public void ArmingAnAction_HighlightsTheOverridesRange()
    {
        var harness = TestMapWindows.Create(20, 20, 1, playerPosition: new Vector3Int(5, 5, 0), Catalog());
        var overrideSpell = CatalogSpell with
        {
            Activator = new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 4), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: 0)),
        };
        harness.ComponentManager.Merge(PlayerEntityId, new ActionInstanceComponent(SpellId, overrideDefinition: overrideSpell));
        harness.ComponentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, SpellId));

        harness.ActionTargetingController.HandleHotkeySlotPress(HotkeySlot.Slot1);

        Assert.AreEqual(SpellId, harness.MapViewState.ArmedActionId);
        Assert.IsNotNull(harness.MapViewState.TargetableTiles);
        Assert.Contains(new Vector3Int(9, 5, 0), harness.MapViewState.TargetableTiles, "The catalog's range is 1; the player's override reaches 4.");
    }
}
