using Engine.ECS.Components;
using Engine.Math;
using Game.Modules.Abilities;
using Game.Modules.Abilities.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

/// <summary>
/// HotbarContent's Draw/Update logic needs a real GraphicsDevice to exercise (SpriteBatch.Draw,
/// RadialFillRenderer) -- the same reason ActionLockContent/PlayerHealthBarContent/
/// PlayerStatusEffectsContent have no test coverage either; verified by running the game
/// instead (see CLAUDE.md's UI-change rule). Size is the one piece of this class that's pure
/// arithmetic with no rendering dependency, so it's worth covering directly. The bind/unbind/
/// hit-test query methods (added for GameInputController's content-drag path) touch only
/// component pools and geometry -- no SpriteBatch involved -- so they're covered here too.
/// </summary>
[TestClass]
public sealed class HotbarContentTests
{
    private const int PlayerEntityId = 1;

    [TestMethod]
    public void Size_AccountsForTenSlotsAcrossThreeVisualGroups()
    {
        // 10 slots total across groups of 2, 3, and 5 (QE / RFV / 12345): (2-1)+(3-1)+(5-1) = 7
        // intra-group gaps, plus 2 gaps between the 3 groups themselves. SlotGap (1) and
        // GroupGap (10) are HotbarContent's own private constants, duplicated here rather than
        // exposed publicly just for this test -- keep in sync if those ever change.
        const float slotGap = 1f;
        const float groupGap = 10f;

        var slotSize = HotbarContent.SlotSize;
        var expectedWidth = 10 * slotSize.X + 7 * slotGap + 2 * groupGap;

        Assert.AreEqual(expectedWidth, HotbarContent.Size.X, 0.01f);
        Assert.AreEqual(slotSize.Y, HotbarContent.Size.Y);
    }

    private static (HotbarContent Hotbar, ComponentManager ComponentManager) Build()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 10);
        componentManager.RegisterMultiPool<ActionHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<ItemHotkeyBindingComponent>();
        componentManager.RegisterMultiPool<AbilityInstanceComponent>();
        componentManager.RegisterMultiPool<InventoryItemStackComponent>();
        componentManager.RegisterPackedPool<InventoryComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ActionLockComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<PotionCooldownComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterPackedPool<ManaComponent>(static (ref existing, incoming) => existing = incoming);

        var world = new Game.World.World(new Game.World.Map(new Vector3Int(10, 10, 1))) { PlayerEntityId = PlayerEntityId };
        var fontService = new FontService("Fonts");
        var windowService = new ElementPoolService(fontService, new GlyphRenderer());

        var hotbar = new HotbarContent(
            world, new MapViewState(), componentManager, new AbilityCatalog(), new ItemCatalog(),
            fontService, new SpriteSheetService(null, "Spritesheets"), new SpriteRenderer());

        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { Size = HotbarContent.Size, DisplayMode = ElementDisplayMode.Fixed },
        });
        hostWindow.SetContent(hotbar);
        hostWindow.Initialize();

        return (hotbar, componentManager);
    }

    [TestMethod]
    public void TryGetSlotAt_PointWithinTheFirstSlot_ReturnsSlot1()
    {
        var (hotbar, _) = Build();

        Assert.IsTrue(hotbar.TryGetSlotAt(new Point(1, 1), out var slot));
        Assert.AreEqual(HotkeySlot.Slot1, slot);
    }

    [TestMethod]
    public void TryGetSlotAt_PointFarOutsideAnySlot_ReturnsFalse()
    {
        var (hotbar, _) = Build();

        Assert.IsFalse(hotbar.TryGetSlotAt(new Point(100_000, 100_000), out _));
    }

    [TestMethod]
    public void BindItem_WritesTheBinding()
    {
        var (hotbar, componentManager) = Build();
        var itemId = Guid.NewGuid();

        hotbar.BindItem(HotkeySlot.Slot3, itemId);

        Assert.IsTrue(hotbar.TryGetBoundItemId(HotkeySlot.Slot3, out var boundItemId));
        Assert.AreEqual(itemId, boundItemId);
    }

    [TestMethod]
    public void BindItem_ClearsAnyExistingActionBindingOnTheSameSlot()
    {
        var (hotbar, componentManager) = Build();
        var abilityId = Guid.NewGuid();
        componentManager.GetMultiPool<ActionHotkeyBindingComponent>().Add(PlayerEntityId, new ActionHotkeyBindingComponent(HotkeySlot.Slot3, abilityId));

        hotbar.BindItem(HotkeySlot.Slot3, Guid.NewGuid());

        Assert.IsFalse(ActionHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ActionHotkeyBindingComponent>(), PlayerEntityId, HotkeySlot.Slot3, out _));
    }

    [TestMethod]
    public void BindItem_CalledTwiceOnTheSameSlot_ReplacesTheEarlierItem()
    {
        var (hotbar, componentManager) = Build();
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();

        hotbar.BindItem(HotkeySlot.Slot3, firstItemId);
        hotbar.BindItem(HotkeySlot.Slot3, secondItemId);

        Assert.IsTrue(hotbar.TryGetBoundItemId(HotkeySlot.Slot3, out var boundItemId));
        Assert.AreEqual(secondItemId, boundItemId);
        Assert.AreEqual(1, componentManager.GetMultiPool<ItemHotkeyBindingComponent>().CountForEntity(PlayerEntityId));
    }

    [TestMethod]
    public void UnbindItemSlot_RemovesTheBinding()
    {
        var (hotbar, _) = Build();
        var itemId = Guid.NewGuid();
        hotbar.BindItem(HotkeySlot.Slot3, itemId);

        hotbar.UnbindItemSlot(HotkeySlot.Slot3);

        Assert.IsFalse(hotbar.TryGetBoundItemId(HotkeySlot.Slot3, out _));
    }

    [TestMethod]
    public void UnbindItemSlot_NoBindingPresent_DoesNotThrow()
    {
        var (hotbar, _) = Build();

        hotbar.UnbindItemSlot(HotkeySlot.Slot3);
    }

    [TestMethod]
    public void TryGetBoundItemId_NoBindingOnThatSlot_ReturnsFalse()
    {
        var (hotbar, _) = Build();

        Assert.IsFalse(hotbar.TryGetBoundItemId(HotkeySlot.Slot1, out _));
    }
}
