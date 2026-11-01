using Engine.ECS.Components;
using Engine.ECS.Entities;
using Game.Blueprints;
using Game.Modules.Auras;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Views;

[TestClass]
public sealed class ActionSourceNamingTests
{
    private static readonly Guid AuraGuid = new("00000000-0000-0000-0000-00000000b001");

    private const int GoblinEntityId = 3;
    private const int PlayerEntityId = 4;

    [TestMethod]
    public void Describe_Entity_IsTheNameItHadWhenTheSourceWasCreated()
    {
        var (naming, source) = GoblinSource();

        Assert.AreEqual("Goblin Foreman", naming.Describe(source, PlayerEntityId));
    }

    [TestMethod]
    public void Describe_TheDescribedEntityItself_IsSelf()
    {
        var (naming, source) = GoblinSource();

        Assert.AreEqual(ActionSourceNaming.SelfName, naming.Describe(source, GoblinEntityId));
        Assert.AreEqual(ActionSourceNaming.SelfName, naming.DescribeByName(source, GoblinEntityId));
        Assert.IsTrue(naming.IsOwnSource(source, GoblinEntityId));
    }

    [TestMethod]
    public void Describe_TheDescribedEntitysOwnSourceWithACause_IsTheCausesName()
    {
        var (naming, source) = GoblinSource(new SourceCause(SourceCauseKind.Class, "Tank"));

        Assert.AreEqual("Tank", naming.Describe(source, GoblinEntityId));
        Assert.AreEqual("Tank", naming.DescribeByName(source, GoblinEntityId));
        Assert.AreEqual(ActionSourceNaming.SelfName, naming.DescribeEntity(source, GoblinEntityId));
    }

    [TestMethod]
    public void Describe_AnotherEntitysSourceWithACause_IsTheCauseAndTheEntity()
    {
        var (naming, source) = GoblinSource(new SourceCause(SourceCauseKind.Spell, "Toxic Strike"));

        Assert.AreEqual("Toxic Strike (Goblin Foreman)", naming.Describe(source, PlayerEntityId));
        Assert.AreEqual("Toxic Strike (Goblin Foreman)", naming.DescribeByName(source, PlayerEntityId));
        Assert.AreEqual("Goblin Foreman", naming.DescribeEntity(source, PlayerEntityId));
    }

    [TestMethod]
    public void Describe_AnEntityWhoseIdWasReusedAfterTheSourceDied_IsTheOldName()
    {
        var (naming, source, entityKeys) = GoblinSourceWithKeys();

        entityKeys.Release(GoblinEntityId);
        entityKeys.Issue(GoblinEntityId);

        Assert.AreEqual("Goblin Foreman", naming.Describe(source, GoblinEntityId));
        Assert.IsFalse(naming.IsOwnSource(source, GoblinEntityId));
    }

    [TestMethod]
    public void Describe_Terrain_IsTheTerrainsName()
    {
        var terrain = new TerrainRegistry();
        var holyGroundId = terrain.Register(new TerrainDefinition("test:holy", "Holy Ground", "", ".", default));

        Assert.AreEqual("Holy Ground", TestActionSources.Naming(terrain).Describe(ActionSource.FromTerrain(holyGroundId), PlayerEntityId));
    }

    [TestMethod]
    public void Describe_Aura_IsTheAurasName()
    {
        var auras = new AuraCatalog();
        var auraId = auras.Register(new AuraDefinition(AuraGuid, "Toxic Idol", Color.Green));

        Assert.AreEqual("Toxic Idol", TestActionSources.Naming(auras: auras).Describe(ActionSource.FromAura(auraId), PlayerEntityId));
    }

    [TestMethod]
    public void Describe_AdminAndAI_AreTheKind()
    {
        var naming = TestActionSources.Naming();

        Assert.AreEqual("Admin", naming.Describe(ActionSource.Admin, PlayerEntityId));
        Assert.AreEqual("AI", naming.Describe(ActionSource.AI, PlayerEntityId));
    }

    [TestMethod]
    public void Describe_TerrainRegisteredAgainUnderTheSameKey_IsTheNewName()
    {
        var terrain = new TerrainRegistry();
        var terrainId = terrain.Register(new TerrainDefinition("test:holy", "Holy Ground", "", ".", default));
        var naming = TestActionSources.Naming(terrain);
        var source = ActionSource.FromTerrain(terrainId);

        terrain.Register(new TerrainDefinition("test:holy", "Hallowed Ground", "", ".", default));

        Assert.AreEqual("Hallowed Ground", naming.Describe(source, PlayerEntityId));
    }

    [TestMethod]
    public void Describe_AuraRegisteredAgainUnderTheSameGuid_IsTheNewName()
    {
        var auras = new AuraCatalog();
        var auraId = auras.Register(new AuraDefinition(AuraGuid, "Toxic Idol", Color.Green));
        var naming = TestActionSources.Naming(auras: auras);
        var source = ActionSource.FromAura(auraId);

        auras.Register(new AuraDefinition(AuraGuid, "Venom Idol", Color.Green));

        Assert.AreEqual("Venom Idol", naming.Describe(source, PlayerEntityId));
    }

    private static (ActionSourceNaming Naming, ActionSource Source) GoblinSource(SourceCause cause = default)
    {
        var (naming, source, _) = GoblinSourceWithKeys(cause);
        return (naming, source);
    }

    private static (ActionSourceNaming Naming, ActionSource Source, EntityKeys EntityKeys) GoblinSourceWithKeys(SourceCause cause = default)
    {
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4));
        componentManager.Merge(GoblinEntityId, new DisplayTextComponent("Goblin Foreman", ""));
        var entityKeys = new EntityKeys();
        for (var id = 0; id <= PlayerEntityId; id++)
        {
            entityKeys.Issue(id);
        }

        var source = ActionSource.FromEntity(componentManager, entityKeys, GoblinEntityId, new BlueprintRegistry(), cause);
        return (TestActionSources.Naming(entityKeys: entityKeys), source, entityKeys);
    }
}
