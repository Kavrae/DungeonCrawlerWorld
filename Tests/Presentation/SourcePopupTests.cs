using Engine.ECS.Components;
using Engine.ECS.Entities;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Game.Terrain;
using Game.World;
using Presentation.UI;

namespace Tests.Presentation;

[TestClass]
public sealed class SourcePopupTests
{
    private const int GoblinEntityId = 1;
    private const int PlayerEntityId = 2;

    [TestMethod]
    public void AnEntitysOwnSourceWithACause_HasASelfRowAndALabelledCauseRow()
    {
        var (naming, components, keys) = Setup();
        var source = ActionSource.FromEntity(components, keys, PlayerEntityId, new BlueprintRegistry(), new SourceCause(SourceCauseKind.Class, "Tank"));

        var (body, rows) = ModifierDisplayFormatting.SourcePopupShowingSeconds(naming, source, PlayerEntityId, "+10% Health Regen", remainingSeconds: null);

        Assert.AreEqual("+10% Health Regen\nPermanent", body);
        CollectionAssert.AreEqual(new[] { ("Source", "Self"), ("Class", "Tank") }, rows.Select(static row => (row.LeftText, row.RightText)).ToArray());
    }

    [TestMethod]
    public void AnotherEntitysSourceWithACause_NamesTheEntityAndTheCause()
    {
        var (naming, components, keys) = Setup();
        var source = ActionSource.FromEntity(components, keys, GoblinEntityId, new BlueprintRegistry(), new SourceCause(SourceCauseKind.Spell, "Toxic Strike"));

        var (_, rows) = ModifierDisplayFormatting.SourcePopupShowingSeconds(naming, source, PlayerEntityId, "Poison x2", remainingSeconds: 4);

        CollectionAssert.AreEqual(new[] { ("Source", "Goblin"), ("Spell", "Toxic Strike") }, rows.Select(static row => (row.LeftText, row.RightText)).ToArray());
    }

    [TestMethod]
    public void AnEntitysOwnSourceWithNoCause_HasOnlyTheSelfRow()
    {
        var (naming, components, keys) = Setup();
        var source = ActionSource.FromEntity(components, keys, PlayerEntityId, new BlueprintRegistry());

        var (_, rows) = ModifierDisplayFormatting.SourcePopupShowingSeconds(naming, source, PlayerEntityId, "+1 Strength", remainingSeconds: null);

        CollectionAssert.AreEqual(new[] { ("Source", "Self") }, rows.Select(static row => (row.LeftText, row.RightText)).ToArray());
    }

    [TestMethod]
    public void ATerrainSource_HasOnlyTheTerrainsRow()
    {
        var terrain = new TerrainRegistry();
        var holyGroundId = terrain.Register(new TerrainDefinition("test:holy", "Holy Ground", "", ".", default));

        var (_, rows) = ModifierDisplayFormatting.SourcePopupShowingSeconds(TestActionSources.Naming(terrain), ActionSource.FromTerrain(holyGroundId), PlayerEntityId, "50% Damage Resistance", remainingSeconds: null);

        CollectionAssert.AreEqual(new[] { ("Source", "Holy Ground") }, rows.Select(static row => (row.LeftText, row.RightText)).ToArray());
    }

    private static (Game.Views.ActionSourceNaming Naming, ComponentManager Components, EntityKeys Keys) Setup()
    {
        var components = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 4));
        components.Merge(GoblinEntityId, new DisplayTextComponent("Goblin", ""));
        var keys = new EntityKeys();
        for (var entityId = 0; entityId <= PlayerEntityId; entityId++)
        {
            keys.Issue(entityId);
        }

        return (TestActionSources.Naming(entityKeys: keys), components, keys);
    }
}
