using Engine.ECS.Components;
using Engine.ECS.Entities;
using Game.Blueprints;
using Game.Modules.Auras;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Views;

[TestClass]
public sealed class ActionSourceNamingTests
{
    private static readonly Guid AuraGuid = new("00000000-0000-0000-0000-00000000b001");

    [TestMethod]
    public void Describe_Entity_IsTheNameItHadWhenTheSourceWasCreated()
    {
        const int entityId = 3;
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4));
        componentManager.Merge(entityId, new DisplayTextComponent("Goblin Foreman", ""));
        var entityKeys = new EntityKeys();
        for (var id = 0; id <= entityId; id++)
        {
            entityKeys.Issue(id);
        }

        var source = ActionSource.FromEntity(componentManager, entityKeys, entityId, new BlueprintRegistry());

        Assert.AreEqual("Goblin Foreman", TestActionSources.Naming().Describe(source));
    }

    [TestMethod]
    public void Describe_Terrain_IsTheTerrainsName()
    {
        var terrain = new TerrainRegistry();
        var holyGroundId = terrain.Register(new TerrainDefinition("test:holy", "Holy Ground", "", default, ".", default));

        Assert.AreEqual("Holy Ground", TestActionSources.Naming(terrain).Describe(ActionSource.FromTerrain(holyGroundId)));
    }

    [TestMethod]
    public void Describe_Aura_IsTheAurasName()
    {
        var auras = new AuraCatalog();
        var auraId = auras.Register(new AuraDefinition(AuraGuid, "Toxic Idol", Color.Green));

        Assert.AreEqual("Toxic Idol", TestActionSources.Naming(auras: auras).Describe(ActionSource.FromAura(auraId)));
    }

    [TestMethod]
    public void Describe_AdminAndAI_AreTheKind()
    {
        var naming = TestActionSources.Naming();

        Assert.AreEqual("Admin", naming.Describe(ActionSource.Admin));
        Assert.AreEqual("AI", naming.Describe(ActionSource.AI));
    }

    [TestMethod]
    public void Describe_TerrainRegisteredAgainUnderTheSameKey_IsTheNewName()
    {
        var terrain = new TerrainRegistry();
        var terrainId = terrain.Register(new TerrainDefinition("test:holy", "Holy Ground", "", default, ".", default));
        var naming = TestActionSources.Naming(terrain);
        var source = ActionSource.FromTerrain(terrainId);

        terrain.Register(new TerrainDefinition("test:holy", "Hallowed Ground", "", default, ".", default));

        Assert.AreEqual("Hallowed Ground", naming.Describe(source));
    }

    [TestMethod]
    public void Describe_AuraRegisteredAgainUnderTheSameGuid_IsTheNewName()
    {
        var auras = new AuraCatalog();
        var auraId = auras.Register(new AuraDefinition(AuraGuid, "Toxic Idol", Color.Green));
        var naming = TestActionSources.Naming(auras: auras);
        var source = ActionSource.FromAura(auraId);

        auras.Register(new AuraDefinition(AuraGuid, "Venom Idol", Color.Green));

        Assert.AreEqual("Venom Idol", naming.Describe(source));
    }
}
