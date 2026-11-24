using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Actions;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using Tests.UIEngine;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>A hotbar slot is highlighted while a context menu is open for it, or for anything showing the stack it is bound to; it shows only that stack of the player's.</summary>
[TestClass]
[DoNotParallelize]
public sealed class HotbarContextMenuTargetTests
{
    private const int PlayerEntityId = 1;
    private static readonly Guid AppleId = Guid.NewGuid();

    private sealed class ShowsOneStack(ElementServices services, uint stackInstanceId) : Element(services), IShowsItemStacks
    {
        public bool ShowsStack(int entityId, uint shownStackInstanceId) => entityId == PlayerEntityId && shownStackInstanceId == stackInstanceId;
    }

    private sealed record Fixture(HotbarContent Hotbar, ContextMenuController ContextMenus, ElementPoolService Pool, uint BoundStack, uint OtherStack);

    private static Fixture Build()
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(10, 10, 1)), playerEntityId: PlayerEntityId);
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));
        var itemCatalog = new ItemCatalog();
        itemCatalog.Register(new ItemDefinition(AppleId, "Apple", null, "a", Color.White, Tags: [], Effects: []));
        var boundStack = InventoryActions.AddItem(componentManager, PlayerEntityId, AppleId, quantity: 1);
        var otherStack = InventoryActions.AddDivergentItem(componentManager, PlayerEntityId, new ItemDefinition(AppleId, "Apple", "bruised", "a", Color.White, Tags: [], Effects: []));
        componentManager.Merge(PlayerEntityId, new ItemHotkeyBindingComponent(HotkeySlot.Slot2, boundStack));

        var fontService = TestFonts.Shared;
        var pool = TestElementPoolServiceFactory.Create(fontService);
        var contextMenus = TestElementPoolServiceFactory.CreateContextMenuController(pool, new UiLayerStack());
        var hotbar = new HotbarContent(world, new MapViewState(), new HotkeyBindingView(componentManager), new InventoryView(componentManager, itemCatalog), TestActionStateViews.Over(componentManager, new ActionCatalog(), itemCatalog), new HotkeyBindingCommands(componentManager, itemCatalog, new EventBus()), itemCatalog, fontService, new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver), new Vector2(1920, 1080), simulationClock: new SimulationClock(), contextMenus: contextMenus);
        return new Fixture(hotbar, contextMenus, pool, boundStack, otherStack);
    }

    private static HotbarSlotElement SlotElement(Fixture fixture, HotkeySlot slot)
    {
        var element = new HotbarSlotElement(TestElementServices.For(fixture.Pool, TestFonts.Shared));
        element.Configure(fixture.Hotbar, slot);
        return element;
    }

    private static void OpenFor(Fixture fixture, Element target) =>
        fixture.ContextMenus.Open(Vector2.Zero, [new DropdownOption("Drop", null, true, static () => { })], target);

    [TestMethod]
    public void ABoundSlot_ShowsItsStackOfThePlayersAlone()
    {
        var fixture = Build();
        var slot = SlotElement(fixture, HotkeySlot.Slot2);

        Assert.IsTrue(slot.ShowsStack(PlayerEntityId, fixture.BoundStack));
        Assert.IsFalse(slot.ShowsStack(PlayerEntityId, fixture.OtherStack));
        Assert.IsFalse(slot.ShowsStack(PlayerEntityId + 1, fixture.BoundStack));
        Assert.IsFalse(SlotElement(fixture, HotkeySlot.Slot3).ShowsStack(PlayerEntityId, fixture.BoundStack));
    }

    [TestMethod]
    public void AMenuOpenForACellOfTheBoundStack_HighlightsTheSlot()
    {
        var fixture = Build();
        var slot = SlotElement(fixture, HotkeySlot.Slot2);

        OpenFor(fixture, new ShowsOneStack(TestElementServices.For(fixture.Pool, TestFonts.Shared), fixture.BoundStack));
        Assert.IsTrue(fixture.Hotbar.IsContextMenuAbout(slot));

        OpenFor(fixture, new ShowsOneStack(TestElementServices.For(fixture.Pool, TestFonts.Shared), fixture.OtherStack));
        Assert.IsFalse(fixture.Hotbar.IsContextMenuAbout(slot));

        fixture.ContextMenus.Close();
        Assert.IsFalse(fixture.Hotbar.IsContextMenuAbout(slot));
    }

    [TestMethod]
    public void AMenuOpenForTheSlot_HighlightsIt_AndNotAnUnboundSlot()
    {
        var fixture = Build();
        var slot = SlotElement(fixture, HotkeySlot.Slot2);
        var unbound = SlotElement(fixture, HotkeySlot.Slot3);

        OpenFor(fixture, slot);

        Assert.IsTrue(fixture.Hotbar.IsContextMenuAbout(slot));
        Assert.IsFalse(fixture.Hotbar.IsContextMenuAbout(unbound));
    }
}
