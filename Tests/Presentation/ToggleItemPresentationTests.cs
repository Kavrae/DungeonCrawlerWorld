using Engine.ECS.Components;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Blueprints.Composites;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Inventory.Definitions;
using Game.Spawning;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

/// <summary>A toggle item in the UI: pressed or activated it is queued at once and never armed, its menu option names what it will do, and a lit stack keeps a cell of its own.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ToggleItemPresentationTests
{
    private const int GridPlayerEntityId = 1;
    private const uint LockedUntilFrame = 1000;

    private static readonly Vector3Int Start = new(10, 10, (int)MapLayer.Ground);

    private sealed class InputHarness
    {
        public required EcsContext Ecs { get; init; }
        public required ItemCatalog Items { get; init; }
        public required int PlayerEntityId { get; init; }
        public required ActionTargetingController ActionTargeting { get; init; }
        public required MapViewState MapViewState { get; init; }

        private long _frame;

        public void Frame()
        {
            _frame++;
            Ecs.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: _frame));
        }

        public List<InventoryItemStackComponent> IdolStacks()
        {
            var stacks = new List<InventoryItemStackComponent>();
            InventoryQueries.CopyStacksForEntity(Ecs.ComponentManager.GetMultiPool<InventoryItemStackComponent>(), PlayerEntityId, stacks);
            return stacks.Where(stack => stack.ItemDefinitionId == ToxicIdol.Id).ToList();
        }

        public bool IsIdolLit => IdolStacks().Any(stack => stack.Override?.Activator is ToggleItemActivator { IsToggledOn: true });

        public bool IsPlayerLocked => ActionLockGate.IsBlocked(Ecs.ComponentManager.GetPackedPool<ActionLockComponent>(), PlayerEntityId, _frame);

        public void FrameUntil(Func<bool> condition, int maximumFrames = 300)
        {
            for (var index = 0; index < maximumFrames && !condition(); index++)
            {
                Frame();
            }

            Assert.IsTrue(condition(), $"Not reached within {maximumFrames} frames.");
        }

        public void LockPlayer() =>
            Ecs.ComponentManager.GetPackedPool<ActionLockComponent>().TryUpdate(PlayerEntityId, static (ref ActionLockComponent actionLock) => actionLock.UnlockedAtFrame = LockedUntilFrame);
    }

    private static InputHarness BuildInput()
    {
        var map = new Map(new Vector3Int(40, 40, 3));
        var session = GameBootstrapper.Build(ValidatedMods.None, map, new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);
        var world = session.World;
        var ecs = session.EcsContext;
        var components = ecs.ComponentManager;
        session.Internals.ProcessingTierResolver.SetReferencePosition(Start);

        var playerEntityId = ecs.EntityManager.CreateEntity();
        session.Internals.ProcessingTierResolver.PinLocalAndNotify(playerEntityId);
        session.Internals.Factory.Spawn(SpawnRequest.At(session.Catalogs.Definitions.GetId(Player.Id), Start) with { Seed = 1, ReservedEntityId = playerEntityId });
        world.PlayerEntityId = playerEntityId;
        components.Merge(playerEntityId, new HotkeyExpansionUnlockComponent(unlockedSlotCount: 10));

        var mapViewState = new MapViewState();
        var entityActions = EntityActions.For(components, session.Catalogs.ActionCatalog, session.Catalogs.Definitions);
        var actionTargeting = new ActionTargetingController(
            world,
            mapViewState,
            new MapCamera(world),
            new UiLayerStack(),
            session.Catalogs.ActionCatalog,
            session.Catalogs.ItemCatalog,
            new TransformView(components),
            new HotkeyBindingView(components),
            new InventoryView(components, session.Catalogs.ItemCatalog),
            new ActionStateView(components, entityActions, session.Catalogs.ItemCatalog, localTierRoster: null, TestActionStateViews.EffectServicesOver(components)),
            session.Views.TargetingView,
            session.Commands.PlayerCommands,
            simulationClock: ecs.SystemManager.Clock);

        return new InputHarness { Ecs = ecs, Items = session.Catalogs.ItemCatalog, PlayerEntityId = playerEntityId, ActionTargeting = actionTargeting, MapViewState = mapViewState };
    }

    [TestMethod]
    public void HotbarPressOnAToggleItem_WindsUpThenTurnsItOn_AndTheNextPressTurnsItOff_WithoutArming()
    {
        var harness = BuildInput();
        harness.FrameUntil(() => !harness.IsPlayerLocked);

        harness.ActionTargeting.HandleHotkeySlotPress(HotkeySlot.Slot6);
        Assert.IsNull(harness.MapViewState.ArmedItemStackInstanceId);
        Assert.IsNull(harness.MapViewState.ArmedSlot);
        harness.Frame();

        Assert.IsFalse(harness.IsIdolLit, "The Toxic Idol is Delayed: it winds up first.");
        Assert.IsTrue(harness.Ecs.ComponentManager.GetPackedPool<PendingWindupComponent>().Has(harness.PlayerEntityId));

        harness.FrameUntil(() => harness.IsIdolLit);
        Assert.IsFalse(harness.IsPlayerLocked, "Lit the frame its windup's lock ends.");

        harness.ActionTargeting.HandleHotkeySlotPress(HotkeySlot.Slot6);
        harness.Frame();

        Assert.IsFalse(harness.IsIdolLit, "The slot follows the unit it lit, and putting it out is at once.");
        Assert.AreEqual(1, harness.IdolStacks().Sum(stack => stack.Quantity));
        Assert.IsNull(harness.MapViewState.ArmedSlot);
    }

    [TestMethod]
    public void HotbarPressOnAToggleItem_LeavesAnArmedActionArmed()
    {
        var harness = BuildInput();
        harness.FrameUntil(() => !harness.IsPlayerLocked);
        harness.ActionTargeting.HandleHotkeySlotPress(HotkeySlot.Base1);
        Assert.AreEqual(PowerAttackAction.Id, harness.MapViewState.ArmedActionId);

        harness.ActionTargeting.HandleHotkeySlotPress(HotkeySlot.Slot6);
        harness.FrameUntil(() => harness.IsIdolLit);

        Assert.AreEqual(PowerAttackAction.Id, harness.MapViewState.ArmedActionId);
    }

    [TestMethod]
    public void HotbarPressOnAToggleAction_QueuesItWithoutArming_EvenWhileActionLocked()
    {
        var harness = BuildInput();
        var components = harness.Ecs.ComponentManager;
        components.GetMultiPool<ActionHotkeyBindingComponent>().Add(harness.PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot8, ToxicAuraAction.Id));
        components.GetPackedPool<Game.Modules.Mana.Components.ManaComponent>().TryUpdate(harness.PlayerEntityId, static (ref Game.Modules.Mana.Components.ManaComponent mana) =>
        {
            mana.MaximumMana = 50;
            mana.CurrentMana = 50;
        });
        harness.LockPlayer();

        harness.ActionTargeting.HandleHotkeySlotPress(HotkeySlot.Slot8);
        Assert.IsNull(harness.MapViewState.ArmedActionId);
        Assert.IsNull(harness.MapViewState.ArmedSlot);
        harness.Frame();

        Assert.AreEqual(1, components.GetMultiPool<ActiveToggleComponent>().CountForEntity(harness.PlayerEntityId));
    }

    [TestMethod]
    public void ActivatingAToggleItemFromTheInventory_QueuesItWithoutArming()
    {
        var harness = BuildInput();
        harness.FrameUntil(() => !harness.IsPlayerLocked);

        harness.ActionTargeting.ArmItemFromStack(harness.IdolStacks().Single().StackInstanceId);
        Assert.IsNull(harness.MapViewState.ArmedItemStackInstanceId);
        harness.FrameUntil(() => harness.IsIdolLit);

        Assert.IsTrue(harness.IsIdolLit);
    }

    private sealed record GridFixture(InventoryGridContent Grid, Window HostWindow, ComponentManager ComponentManager, ItemCatalog ItemCatalog, ContextMenuController ContextMenuController, List<uint> ActivateRequests)
    {
        public List<InventoryItemStackCell> Cells => HostWindow.ChildElements.OfType<InventoryItemStackCell>().ToList();

        public void Refresh() => Grid.Update(new GameTime());
    }

    private static GridFixture BuildGrid(uint unlockedAtFrame = 0)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20));
        componentManager.Merge(GridPlayerEntityId, new ActionLockComponent(standardLockFrames: ActionLockGate.StandardLockFrames, currentLockTotalFrames: 0, unlockedAtFrame: unlockedAtFrame));

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));
        windowService.RegisterFactory<ContextMenu>(() => new ContextMenu(fontService, windowService, labelRenderer));

        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: GridPlayerEntityId);
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());

        var itemCatalog = new ItemCatalog();
        // FreeCast, so the grid's lock rules for a FreeCast item can be tested; nothing else here depends on timing.
        itemCatalog.Register(ToxicIdol.Build() with { Activator = new ToggleItemActivator(new ActionTiming(ActionTimingCategory.FreeCast)) });

        var tooltipController = new TooltipController();
        tooltipController.Initialize(windowService, new UiLayerStack());

        var activateRequests = new List<uint>();
        var grid = new InventoryGridContent(world, TestInventoryServices.Over(componentManager, itemCatalog, world), windowService, contextMenuController, GridPlayerEntityId, filterTag: Engine.Tags.GameplayTag.None, tooltipController, static () => null, new MapViewState(), static (_, _) => { }, static (_, _) => { }, (_, stackInstanceId) => activateRequests.Add(stackInstanceId), static _ => { }, new SimulationClock());

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.Initialize();
        grid.Initialize(hostWindow);

        return new GridFixture(grid, hostWindow, componentManager, itemCatalog, contextMenuController, activateRequests);
    }

    private static Button MenuOption(GridFixture fixture, InventoryItemStackCell cell, string label)
    {
        cell.OnRightClicked!.Invoke(Point.Zero);
        return fixture.ContextMenuController.Menu.ChildElements.OfType<Button>().Single(button => button.LeftText == label);
    }

    [TestMethod]
    public void LitStack_KeepsACellOfItsOwn_WhileStacksAreGrouped()
    {
        var fixture = BuildGrid();
        var plainStackId = InventoryActions.AddItem(fixture.ComponentManager, GridPlayerEntityId, ToxicIdol.Id, quantity: 3);
        Assert.IsTrue(ToggleItemActions.TryToggle(fixture.ComponentManager, fixture.ItemCatalog, GridPlayerEntityId, plainStackId, out var litStackId));
        Assert.IsTrue(fixture.Grid.GroupDivergedStacks);

        fixture.Refresh();

        var cells = fixture.Cells;
        Assert.HasCount(2, cells);
        CollectionAssert.AreEquivalent(new uint?[] { plainStackId, litStackId }, cells.Select(cell => cell.StackInstanceId).ToArray());
        Assert.IsFalse(cells.Any(cell => cell.MergedStackBadgeVisible));
    }

    [TestMethod]
    public void LitCell_CarriesTheToggleMarker_AndTheUnlitCellDoesNot()
    {
        var fixture = BuildGrid();
        var plainStackId = InventoryActions.AddItem(fixture.ComponentManager, GridPlayerEntityId, ToxicIdol.Id, quantity: 2);
        Assert.IsTrue(ToggleItemActions.TryToggle(fixture.ComponentManager, fixture.ItemCatalog, GridPlayerEntityId, plainStackId, out var litStackId));

        fixture.Refresh();

        Assert.IsTrue(fixture.Cells.Single(cell => cell.StackInstanceId == litStackId).IsToggledOn);
        Assert.IsFalse(fixture.Cells.Single(cell => cell.StackInstanceId == plainStackId).IsToggledOn);

        Assert.IsTrue(ToggleItemActions.TryToggle(fixture.ComponentManager, fixture.ItemCatalog, GridPlayerEntityId, litStackId, out _));
        fixture.Refresh();

        Assert.IsFalse(fixture.Cells.Any(cell => cell.IsToggledOn), "A pooled cell that was lit is not left marked once it shows an unlit stack.");
    }

    [TestMethod]
    public void MenuOption_ReadsTurnOnForAnUnlitUnit_AndTurnOffForALitOne()
    {
        var fixture = BuildGrid();
        var plainStackId = InventoryActions.AddItem(fixture.ComponentManager, GridPlayerEntityId, ToxicIdol.Id, quantity: 2);
        Assert.IsTrue(ToggleItemActions.TryToggle(fixture.ComponentManager, fixture.ItemCatalog, GridPlayerEntityId, plainStackId, out var litStackId));
        fixture.Refresh();

        Assert.IsTrue(MenuOption(fixture, fixture.Cells.Single(cell => cell.StackInstanceId == plainStackId), "Turn on").Enabled);
        Assert.IsTrue(MenuOption(fixture, fixture.Cells.Single(cell => cell.StackInstanceId == litStackId), "Turn off").Enabled);
    }

    [TestMethod]
    public void FreeCastToggleItem_CanBeActivatedFromTheGridWhileActionLocked()
    {
        var fixture = BuildGrid(unlockedAtFrame: LockedUntilFrame);
        var stackId = InventoryActions.AddItem(fixture.ComponentManager, GridPlayerEntityId, ToxicIdol.Id, quantity: 1);
        fixture.Refresh();
        var cell = fixture.Cells.Single();

        Assert.IsTrue(MenuOption(fixture, cell, "Turn on").Enabled);

        cell.RaiseDoubleClicked();

        CollectionAssert.AreEqual(new[] { stackId }, fixture.ActivateRequests);
    }
}
