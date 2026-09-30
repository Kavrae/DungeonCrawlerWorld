using Engine.Math;
using Game.Bootstrap;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Lootboxes;
using Game.World;

namespace Tests.Bootstrap;

[TestClass]
public sealed class GameCommandsTests
{
    private static GameSession Build() =>
        GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(20, 20, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);

    private static (GameSession GameSession, int SourceEntityId, int PlayerEntityId) BuildWithPlayer()
    {
        var gameSession = Build();
        var entityManager = gameSession.EcsContext.EntityManager;
        var sourceEntityId = entityManager.CreateEntity();
        var playerEntityId = entityManager.CreateEntity();
        gameSession.World.PlayerEntityId = playerEntityId;
        return (gameSession, sourceEntityId, playerEntityId);
    }

    private static ItemDefinition AnyBindableItem(GameSession gameSession) =>
        gameSession.Catalogs.ItemCatalog.Definitions.First(ItemHotkeyBindingQueries.CanBind);

    [TestMethod]
    public void InventoryCommands_TryTransferStack_MovesTheStackInTheSessionsPools()
    {
        var (gameSession, sourceEntityId, playerEntityId) = BuildWithPlayer();
        var componentManager = gameSession.EcsContext.ComponentManager;
        var stackInstanceId = ItemGrants.Grant(componentManager, sourceEntityId, AnyBindableItem(gameSession), quantity: 1);

        Assert.IsTrue(gameSession.Commands.InventoryCommands.TryTransferStack(sourceEntityId, playerEntityId, stackInstanceId));

        var stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
        Assert.IsTrue(InventoryQueries.TryFindByStackInstanceId(stacks, playerEntityId, stackInstanceId, out _));
    }

    [TestMethod]
    public void CurrencyCommands_TryTransfer_MovesTheSessionsCurrency()
    {
        var (gameSession, sourceEntityId, playerEntityId) = BuildWithPlayer();
        var componentManager = gameSession.EcsContext.ComponentManager;
        componentManager.Merge(sourceEntityId, new CurrencyComponent(gold: 5, credits: 0));

        Assert.IsTrue(gameSession.Commands.CurrencyCommands.TryTransfer(sourceEntityId, playerEntityId, CurrencyType.Gold));

        Assert.AreEqual(5, componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(playerEntityId).Gold);
    }

    [TestMethod]
    public void ShopCommands_TryGiveCurrencyToShop_ToANonShop_IsAPlainTransfer()
    {
        var (gameSession, sourceEntityId, playerEntityId) = BuildWithPlayer();
        var componentManager = gameSession.EcsContext.ComponentManager;
        componentManager.Merge(playerEntityId, new CurrencyComponent(gold: 3, credits: 0));

        Assert.IsTrue(gameSession.Commands.ShopCommands.TryGiveCurrencyToShop(playerEntityId, sourceEntityId, CurrencyType.Gold));

        Assert.AreEqual(3, componentManager.GetPackedPool<CurrencyComponent>().GetReadonly(sourceEntityId).Gold);
    }

    [TestMethod]
    public void HotkeyBindingCommands_BindAction_WritesTheBindingAndPublishesOnTheSessionsBus()
    {
        var (gameSession, _, playerEntityId) = BuildWithPlayer();
        var actionId = Guid.NewGuid();
        ActionHotkeyBoundEvent? published = null;
        gameSession.EcsContext.EventBus.Subscribe<ActionHotkeyBoundEvent>(bound => published = bound);

        gameSession.Commands.HotkeyBindingCommands.BindAction(playerEntityId, HotkeySlot.Slot3, actionId);

        var bindings = gameSession.EcsContext.ComponentManager.GetMultiPool<ActionHotkeyBindingComponent>();
        Assert.IsTrue(ActionHotkeyBindingQueries.TryGet(bindings, playerEntityId, HotkeySlot.Slot3, out var boundActionId));
        Assert.AreEqual(actionId, boundActionId);
        Assert.AreEqual(new ActionHotkeyBoundEvent(playerEntityId, HotkeySlot.Slot3, actionId), published);
    }

    [TestMethod]
    public void HotkeyBindingCommands_TryBindItem_ALootbox_IsRefusedAndLeavesTheSlotAlone()
    {
        var (gameSession, _, playerEntityId) = BuildWithPlayer();
        var componentManager = gameSession.EcsContext.ComponentManager;
        var hotkeyBindingCommands = gameSession.Commands.HotkeyBindingCommands;
        var actionId = Guid.NewGuid();
        hotkeyBindingCommands.BindAction(playerEntityId, HotkeySlot.Slot3, actionId);
        LootboxActions.Grant(componentManager, gameSession.Catalogs.LootboxCatalog, gameSession.EcsContext.EventBus, playerEntityId, new LootboxReward(LootboxTypes.Investor.Id, LootboxRarity.Bronze));
        var stacks = new List<InventoryItemStackComponent>();
        InventoryQueries.CopyStacksForEntity(componentManager.GetMultiPool<InventoryItemStackComponent>(), playerEntityId, stacks);

        Assert.IsFalse(hotkeyBindingCommands.TryBindItem(playerEntityId, HotkeySlot.Slot3, stacks.Single().StackInstanceId));

        Assert.IsTrue(ActionHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ActionHotkeyBindingComponent>(), playerEntityId, HotkeySlot.Slot3, out _));
    }

    [TestMethod]
    public void HotkeyBindingCommands_TryBindItem_ReplacesAnActionBindingOnTheSlot()
    {
        var (gameSession, _, playerEntityId) = BuildWithPlayer();
        var componentManager = gameSession.EcsContext.ComponentManager;
        var hotkeyBindingCommands = gameSession.Commands.HotkeyBindingCommands;
        hotkeyBindingCommands.BindAction(playerEntityId, HotkeySlot.Slot3, Guid.NewGuid());
        var stackInstanceId = ItemGrants.Grant(componentManager, playerEntityId, AnyBindableItem(gameSession), quantity: 1);

        Assert.IsTrue(hotkeyBindingCommands.TryBindItem(playerEntityId, HotkeySlot.Slot3, stackInstanceId));

        Assert.IsFalse(ActionHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ActionHotkeyBindingComponent>(), playerEntityId, HotkeySlot.Slot3, out _));
        Assert.IsTrue(ItemHotkeyBindingQueries.TryGet(componentManager.GetMultiPool<ItemHotkeyBindingComponent>(), playerEntityId, HotkeySlot.Slot3, out var boundStackInstanceId));
        Assert.AreEqual(stackInstanceId, boundStackInstanceId);
    }

    [TestMethod]
    public void LootboxCommands_OpenAll_OpensTheSessionsBoxes()
    {
        var (gameSession, _, playerEntityId) = BuildWithPlayer();
        var componentManager = gameSession.EcsContext.ComponentManager;
        LootboxActions.Grant(componentManager, gameSession.Catalogs.LootboxCatalog, gameSession.EcsContext.EventBus, playerEntityId, new LootboxReward(LootboxTypes.Investor.Id, LootboxRarity.Bronze));

        Assert.HasCount(1, gameSession.Commands.LootboxCommands.OpenAll(playerEntityId));
    }
}
