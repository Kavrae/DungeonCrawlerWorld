using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tiles;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Tags;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using Tests.UIEngine;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>An item's "Arm" option and its double-click are one path: enabled and arming while the action lock is up (as a hotbar press arms), shown disabled and inert while the item is blocked.</summary>
[TestClass]
[DoNotParallelize]
public sealed class InventoryGridContentActivateTests
{
    private const int PlayerEntityId = 1;
    private static readonly Guid MeleeItemId = Guid.NewGuid();

    private sealed record Fixture(InventoryGridContent Grid, Window HostWindow, ComponentManager ComponentManager, ContextMenuController ContextMenuController, List<uint> ActivateRequests);

    private static Fixture Build(uint unlockedAtFrame = 0)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20));
        componentManager.Merge(PlayerEntityId, new ActionLockComponent(currentLockTotalFrames: 0, unlockedAtFrame: unlockedAtFrame));

        var fontService = TestFonts.Shared;
        var windowService = TestElementPoolServiceFactory.Create(fontService);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver);
        windowService.RegisterFactory<InventoryItemStackCell>(() => new InventoryItemStackCell(TestElementServices.For(windowService, fontService), spriteSheetService));
        windowService.RegisterFactory<Tooltip>(() => new Tooltip(TestElementServices.For(windowService, fontService)));

        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var contextMenuController = TestElementPoolServiceFactory.CreateContextMenuController(windowService, new UiLayerStack());

        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(
            MeleeItemId, "Test Brass Knuckles", null, "k", Color.White, Tags: [GameTags.DeliveryMelee], Effects: [],
            Activator: new PotionActivator(new TargetingSpec(TargetShape.Self, MaximumRange: 0, AreaSize: 0, Anchor: TargetAnchor.Caster), new ActionTiming(ActionTimingCategory.Immediate, 60, null))));

        var tooltipController = new TooltipController();
        tooltipController.Initialize(windowService, new UiLayerStack());

        var activateRequests = new List<uint>();
        var grid = new InventoryGridContent(world, TestInventoryServices.Over(componentManager, itemCatalog, world), windowService, contextMenuController, PlayerEntityId, filterTag: Engine.Tags.GameplayTag.None, static () => null, new MapViewState(), static (_, _) => { }, (_, stackInstanceId) => activateRequests.Add(stackInstanceId), static _ => { });

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

    private static Button ArmOption(Fixture fixture)
    {
        fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Single().OnRightClicked!.Invoke(Point.Zero);
        return fixture.ContextMenuController.Menu.ChildElements.OfType<Button>().Single(button => button.LeftText == "Arm");
    }

    private static void DoubleClickCell(Fixture fixture)
    {
        var cell = fixture.HostWindow.ChildElements.OfType<InventoryItemStackCell>().Single();
        cell.HandleDoubleClick(cell.Rectangle.Center);
    }

    [TestMethod]
    public void UsableItem_ArmIsEnabled_AndDoubleClickRequestsIt()
    {
        var fixture = Build();

        Assert.IsTrue(ArmOption(fixture).Enabled);

        DoubleClickCell(fixture);
        Assert.HasCount(1, fixture.ActivateRequests);
    }

    [TestMethod]
    public void BlockedItem_ArmIsShownButDisabled_AndDoubleClickDoesNothing()
    {
        var fixture = Build();
        fixture.ComponentManager.Merge(PlayerEntityId, new MeleeDisabledComponent());

        Assert.IsFalse(ArmOption(fixture).Enabled);

        DoubleClickCell(fixture);
        Assert.IsEmpty(fixture.ActivateRequests);
    }

    [TestMethod]
    public void WhileTheActionLockIsUp_ArmIsEnabled_AndDoubleClickArmsAsAHotbarPressDoes()
    {
        var fixture = Build(unlockedAtFrame: 10_000);

        Assert.IsTrue(ArmOption(fixture).Enabled);

        DoubleClickCell(fixture);
        Assert.HasCount(1, fixture.ActivateRequests);
    }
}
