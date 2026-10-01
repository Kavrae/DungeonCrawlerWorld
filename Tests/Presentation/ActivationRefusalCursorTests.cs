using Engine.ECS.Components;
using Game.Effects;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Tags;
using Game.Views;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

/// <summary>The disabled cursor shows over a hotbar slot the player can't use, and over the map while the armed action can't be confirmed -- and goes away under a stationary mouse once that changes.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ActivationRefusalCursorTests
{
    private const int PlayerEntityId = TestMapWindows.PlayerEntityId;
    private static readonly Vector2 ScreenSize = new(2000, 2000);
    private static readonly KeyboardState NoKeys = new();
    private static readonly Guid SpellId = new("7c2e9a41-5d3b-4f86-9e10-b4a7d2c8f365");

    private static MouseState MouseAt(Point position) =>
        new(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static UiInputController Controller(ComponentManager componentManager, Game.World.World world, Element? baseElement = null, Element? staticHudElement = null)
    {
        var layers = new UiLayerStack();
        if (baseElement is not null)
        {
            layers.Add(UiLayer.Base, baseElement);
        }

        if (staticHudElement is not null)
        {
            layers.Add(UiLayer.StaticHud, staticHudElement);
        }

        return TestUiInputController.Create(layers, ScreenSize, componentManager, world, new EventBus(), new ItemCatalog());
    }

    [TestMethod]
    public void HoveringAHotbarSlotThePlayerCannotUse_ShowsTheDisabledCursor_UntilItIsUsableAgain()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(QuickAttackAction.Id, overrideDefinition: null));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, QuickAttackAction.Id));
        componentManager.Merge(PlayerEntityId, new MeleeDisabledComponent());

        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(QuickAttackAction.Build());
        var itemCatalog = new ItemCatalog();
        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var hotbar = new HotbarContent(
            world, new MapViewState(), new HotkeyBindingView(componentManager), new InventoryView(componentManager, itemCatalog), TestActionStateViews.Over(componentManager, actionCatalog, itemCatalog), new HotkeyBindingCommands(componentManager, itemCatalog, new EventBus()), actionCatalog, itemCatalog,
            fontService, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), ScreenSize, simulationClock: new SimulationClock());
        var hotbarWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(500, 0), Size = hotbar.Size, DisplayMode = ElementDisplayMode.Fixed },
        });
        hotbarWindow.SetContent(hotbar);
        hotbarWindow.Initialize();
        hotbar.Update(new GameTime());

        var controller = Controller(componentManager, world, staticHudElement: hotbarWindow);
        var slotCenter = hotbar.GetSlotBounds(HotkeySlot.Slot1).Center;

        controller.Update(NoKeys, MouseAt(Point.Zero));
        controller.Update(NoKeys, MouseAt(slotCenter));
        Assert.AreEqual(MouseCursor.No, controller.CurrentCursor);

        componentManager.GetPackedPool<MeleeDisabledComponent>().Remove(PlayerEntityId);
        hotbar.Update(new GameTime());
        controller.Update(NoKeys, MouseAt(slotCenter));
        Assert.AreEqual(MouseCursor.Arrow, controller.CurrentCursor, "The mouse didn't move, but the slot is usable again.");
    }

    [TestMethod]
    public void HoveringTheMapWhileTheArmedActionCannotBeConfirmed_ShowsTheDisabledCursor_UntilItCan()
    {
        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(new ActionDefinition(
            SpellId, "Test Self Spell", null, "*", Color.White, [GameTags.TargetingSelf],
            Effects: [Effect.None],
            Activator: new SpellActivator(new TargetingSpec(TargetShape.Self, Range: 0), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 30, CooldownFrames: 600))));
        var harness = TestMapWindows.Create(20, 20, 1, playerPosition: new Vector3Int(5, 5, 0), actionCatalog);
        var componentManager = harness.ComponentManager;
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: 0));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(SpellId, overrideDefinition: null));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, SpellId));
        var actions = TestActionStateViews.EntityActions(componentManager, actionCatalog);
        actions.SetCooldown(PlayerEntityId, SpellId, 600, now: 0);

        harness.ActionTargetingController.HandleHotkeySlotPress(HotkeySlot.Slot1);
        Assert.AreEqual(SpellId, harness.MapViewState.ArmedActionId, "Sanity check: a slot on cooldown still arms.");

        var controller = Controller(componentManager, harness.World, baseElement: harness.MapWindow);
        var mapCenter = harness.MapWindow.ContentRectangle.Center;

        controller.Update(NoKeys, MouseAt(Point.Zero));
        controller.Update(NoKeys, MouseAt(mapCenter));
        Assert.AreEqual(MouseCursor.No, controller.CurrentCursor);

        actions.SetCooldown(PlayerEntityId, SpellId, 0, now: 0);
        controller.Update(NoKeys, MouseAt(mapCenter));
        Assert.AreEqual(MouseCursor.Arrow, controller.CurrentCursor, "The mouse didn't move, but the armed action can be confirmed now.");
    }

    [TestMethod]
    public void HotbarSummary_CarriesTheReasonTheSlotIsUnusable()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        componentManager.Merge(PlayerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 5));
        componentManager.Merge(PlayerEntityId, new ActionInstanceComponent(QuickAttackAction.Id, overrideDefinition: null));
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot1, QuickAttackAction.Id));
        var actionCatalog = new ActionCatalog();
        actionCatalog.Register(QuickAttackAction.Build());
        var itemCatalog = new ItemCatalog();
        var hotbar = new HotbarContent(
            world, new MapViewState(), new HotkeyBindingView(componentManager), new InventoryView(componentManager, itemCatalog), TestActionStateViews.Over(componentManager, actionCatalog, itemCatalog), new HotkeyBindingCommands(componentManager, itemCatalog, new EventBus()), actionCatalog, itemCatalog,
            TestFonts.Shared, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer(), ScreenSize, simulationClock: new SimulationClock());

        Assert.IsTrue(hotbar.TryGetSlotSummary(HotkeySlot.Slot1, out _, out _, out var usableBlocker));
        Assert.AreEqual(ActivationBlocker.None, usableBlocker);
        Assert.IsNull(ActivationBlockerText.RowsFor(usableBlocker));

        componentManager.Merge(PlayerEntityId, new MeleeDisabledComponent());

        Assert.IsTrue(hotbar.TryGetSlotSummary(HotkeySlot.Slot1, out _, out _, out var blocker));
        Assert.AreEqual(ActivationBlocker.MeleeDisabled, blocker);
        var rows = ActivationBlockerText.RowsFor(blocker);
        Assert.IsNotNull(rows);
        Assert.AreEqual("No usable arms or hands", rows[^1].LeftText);
    }
}
