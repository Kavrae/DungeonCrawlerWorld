using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Tags;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

/// <summary>An item the player can't use right now keeps "Activate" in its menu, disabled, and a double-click doesn't arm it.</summary>
[TestClass]
[DoNotParallelize]
public sealed class InventoryGridContentActivateTests
{
    private const int PlayerEntityId = 1;
    private static readonly Guid MeleeItemId = Guid.NewGuid();

    private sealed record Fixture(InventoryGridContent Grid, Window HostWindow, ComponentManager ComponentManager, ContextMenuController ContextMenuController, List<uint> ActivateRequests);

    private static Fixture Build()
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: 0));

        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var windowService = TestElementPoolServiceFactory.Create(fontService, labelRenderer);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets");
        var spriteRenderer = new SpriteRenderer();
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(fontService, windowService, labelRenderer, spriteSheetService, spriteRenderer));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(fontService, windowService, labelRenderer));
        windowService.RegisterFactory<ContextMenu>(() => new ContextMenu(fontService, windowService, labelRenderer));

        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(
            MeleeItemId, "Test Brass Knuckles", null, "k", Color.White, Tags: [GameTags.DeliveryMelee], Effects: [],
            Activator: new PotionActivator(new TargetingSpec(TargetShape.Self, Range: 0, AreaSize: 0), new ActionTiming(ActionTimingCategory.Immediate, 60, null))));

        var tooltipController = new TooltipController();
        tooltipController.Initialize(windowService, new UiLayerStack());

        var activateRequests = new List<uint>();
        var grid = new InventoryGridContent(world, TestInventoryServices.Over(componentManager, itemCatalog, world), windowService, contextMenuController, PlayerEntityId, filterTag: Engine.Tags.GameplayTag.None, tooltipController, static () => null, new MapViewState(), static (_, _) => { }, static (_, _) => { }, (_, stackInstanceId) => activateRequests.Add(stackInstanceId), static _ => { }, new SimulationClock());

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(400, 200), DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.ContentPadding = Vector2.Zero;
        hostWindow.Initialize();
        grid.Initialize(hostWindow);

        InventoryActions.AddItem(componentManager, PlayerEntityId, MeleeItemId, quantity: 1);
        grid.Update(new GameTime());

        return new Fixture(grid, hostWindow, componentManager, contextMenuController, activateRequests);
    }

    private static Button ActivateOption(Fixture fixture)
    {
        fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Single().OnRightClicked!.Invoke(Point.Zero);
        return fixture.ContextMenuController.Menu.ChildElements.OfType<Button>().Single(button => button.LeftText == "Activate");
    }

    [TestMethod]
    public void UsableItem_ActivateIsEnabled_AndDoubleClickRequestsIt()
    {
        var fixture = Build();

        Assert.IsTrue(ActivateOption(fixture).Enabled);

        fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Single().RaiseDoubleClicked();
        Assert.HasCount(1, fixture.ActivateRequests);
    }

    [TestMethod]
    public void BlockedItem_ActivateIsShownButDisabled_AndDoubleClickDoesNothing()
    {
        var fixture = Build();
        fixture.ComponentManager.Merge(PlayerEntityId, new MeleeDisabledComponent());

        Assert.IsFalse(ActivateOption(fixture).Enabled);

        fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Single().RaiseDoubleClicked();
        Assert.IsEmpty(fixture.ActivateRequests);
    }
}
