using Game.Blueprints;
using Game.Modules.Auras;
using Microsoft.Xna.Framework;

namespace Tests.Blueprints;

[TestClass]
public sealed class BlueprintAuraTests
{
    private static readonly AuraDefinition FirstAura = new(new Guid("00000000-0000-0000-0000-0000000000c1"), "First", Color.Red);
    private static readonly AuraDefinition SecondAura = new(new Guid("00000000-0000-0000-0000-0000000000c2"), "Second", Color.Blue);

    [TestMethod]
    public void Resolve_CollectsEveryPartsAurasInBuildOrder()
    {
        var definitions = new BlueprintRegistry();
        var basePart = new BlueprintDefinition(Guid.NewGuid(), "Base") { Auras = [new AuraGrant(FirstAura, 4, 2)] };
        definitions.Register(basePart);
        var compositeId = definitions.Register(new BlueprintDefinition(Guid.NewGuid(), "Composite") { Includes = [basePart.Id], Auras = [new AuraGrant(SecondAura, 2, 1)] });

        var auras = definitions.Resolve(compositeId).Auras;

        CollectionAssert.AreEqual(new[] { new AuraGrant(FirstAura, 4, 2), new AuraGrant(SecondAura, 2, 1) }, auras.ToArray());
    }

    /// <summary>A composite overrides an aura a part it includes grants, the same rule as for actions: one source per aura, the later grant's strength.</summary>
    [TestMethod]
    public void Resolve_LaterGrantOfTheSameAura_ReplacesTheEarlierOne()
    {
        var definitions = new BlueprintRegistry();
        var basePart = new BlueprintDefinition(Guid.NewGuid(), "Base") { Auras = [new AuraGrant(FirstAura, 4, 2)] };
        definitions.Register(basePart);
        var compositeId = definitions.Register(new BlueprintDefinition(Guid.NewGuid(), "Composite") { Includes = [basePart.Id], Auras = [new AuraGrant(FirstAura, 8, 3)] });

        var auras = definitions.Resolve(compositeId).Auras;

        CollectionAssert.AreEqual(new[] { new AuraGrant(FirstAura, 8, 3) }, auras.ToArray());
    }

    [TestMethod]
    public void Resolve_NoPartGrantsAnAura_HasNone()
    {
        var definitions = new BlueprintRegistry();
        var blueprintId = definitions.Register(new BlueprintDefinition(Guid.NewGuid(), "Plain"));

        Assert.IsEmpty(definitions.Resolve(blueprintId).Auras);
    }
}
