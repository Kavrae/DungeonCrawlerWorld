using Engine.Math;
using Game.Bootstrap;
using Game.Modules.Inventory.Components;
using Game.World;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class AdminContextMenuOptionsTests
{
    private static GameSession BuildSession() =>
        GameBootstrapper.Build(ValidatedMods.None, new Map(new Vector3Int(20, 20, 3)), new MathUtility(new Random(1)), initialEntityCapacity: 100, initialComponentCapacity: 50);

    [TestMethod]
    public void AddTileOptions_OffersTheNeighborhoodGroupSpawnHereAndTeleportHere()
    {
        var adminContextMenuOptions = new AdminContextMenuOptions(TestAdminTools.Create());
        List<ContextMenuOption> options = [];

        adminContextMenuOptions.AddTileOptions(options, new Vector3Int(5, 5, 0));

        Assert.IsTrue(options[0].IsHeader);
        StringAssert.StartsWith(options[0].Label, "Neighborhood (0, 0)");
        StringAssert.StartsWith(options[1].Label, "Regenerate");
        Assert.AreEqual("Spawn here", options[2].Label);
        Assert.IsNotEmpty(options[2].Submenu!);
        Assert.AreEqual("Teleport here", options[3].Label);
        Assert.HasCount(4, options);
    }

    [TestMethod]
    public void AddEntityOptions_OffersApplyAndGrantLootBox()
    {
        var adminContextMenuOptions = new AdminContextMenuOptions(TestAdminTools.Create());
        List<ContextMenuOption> options = [];

        adminContextMenuOptions.AddEntityOptions(options, entityId: 0);

        CollectionAssert.AreEqual(new[] { "Apply", "Grant loot box" }, options.Select(option => option.Label).ToArray());
        Assert.IsTrue(options.All(option => option.Submenu is { Count: > 0 }));
    }

    [TestMethod]
    public void GrantLootBox_SelectingARarity_GrantsOneBoxToTheEntity()
    {
        var gameSession = BuildSession();
        var entityId = gameSession.EcsContext.EntityManager.CreateEntity();
        var adminContextMenuOptions = new AdminContextMenuOptions(TestAdminTools.Create(gameSession));
        List<ContextMenuOption> options = [];
        adminContextMenuOptions.AddEntityOptions(options, entityId);

        var grantLootBox = options.Single(option => option.Label == "Grant loot box");
        grantLootBox.Submenu![0].Submenu![0].OnSelect();

        Assert.AreEqual(1, gameSession.EcsContext.ComponentManager.GetMultiPool<InventoryItemStackComponent>().CountForEntity(entityId));
    }

    [TestMethod]
    public void SpawnHere_SelectingABlueprint_SpawnsAnEntity()
    {
        var gameSession = BuildSession();
        var adminContextMenuOptions = new AdminContextMenuOptions(TestAdminTools.Create(gameSession));
        List<ContextMenuOption> options = [];
        adminContextMenuOptions.AddTileOptions(options, new Vector3Int(5, 5, 0));
        var livingBefore = gameSession.EcsContext.EntityManager.LivingEntityCount;

        options.Single(option => option.Label == "Spawn here").Submenu![0].OnSelect();

        Assert.AreEqual(livingBefore + 1, gameSession.EcsContext.EntityManager.LivingEntityCount);
    }
}
