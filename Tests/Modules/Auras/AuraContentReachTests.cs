using Game.Blueprints.Objects;
using Game.Effects;
using Game.Modules.Actions.Definitions.DirectActions;
using Game.Modules.Auras;
using Game.Modules.Inventory.Definitions;
using Game.Terrain;

namespace Tests.Modules.Auras;

/// <summary>The built-in auras kept their numbers when strength split into power and size: each one's old strength is its power, and its old reach (log2 of the strength) is its size.</summary>
[TestClass]
public sealed class AuraContentReachTests
{
    private static AuraSourceGrant OnlyAuraGrant(IReadOnlyList<Effect> effects) => effects.SelectMany(effect => effect.Entries).OfType<AuraSourceGrant>().Single();

    [TestMethod]
    public void Lava_PowerEightSizeThree()
    {
        Assert.AreEqual(new TerrainAura(BuiltInTerrain.LavaAura, 8, 3), BuiltInTerrain.Lava.Aura);
    }

    [TestMethod]
    public void HolyGround_PowerEightSizeThree()
    {
        Assert.AreEqual(new TerrainAura(BuiltInTerrain.HolyGroundAura, 8, 3), BuiltInTerrain.HolyGround.Aura);
    }

    [TestMethod]
    public void HealingShrine_PowerSixteenSizeFour()
    {
        var grant = HealingShrine.Definition.Auras.Single();

        Assert.AreEqual((16, 4), (grant.Power, grant.Size));
    }

    [TestMethod]
    public void ToxicIdolAndToxicAura_PowerSixteenSizeFour()
    {
        var idol = OnlyAuraGrant(ToxicIdol.Build().Effects);
        var action = OnlyAuraGrant(ToxicAuraAction.Build().Effects);

        Assert.AreEqual((16, 4), (idol.Power, idol.Size));
        Assert.AreEqual((16, 4), (action.Power, action.Size));
    }

    [TestMethod]
    public void ScrollOfTorch_PowerEightSizeThree()
    {
        var grant = OnlyAuraGrant(ScrollOfTorch.Build().Effects);

        Assert.AreEqual((8, 3), (grant.Power, grant.Size));
    }

    [TestMethod]
    public void EveryBuiltInAura_FallsOffLinearly()
    {
        AuraDefinition[] auras = [BuiltInTerrain.LavaAura, BuiltInTerrain.HolyGroundAura, HealingShrine.Aura, ToxicIdol.Aura, ToxicAuraAction.Aura, ScrollOfTorch.Aura];

        Assert.IsTrue(auras.All(aura => aura.Falloff == Engine.Math.AuraFalloff.Linear));
    }
}
