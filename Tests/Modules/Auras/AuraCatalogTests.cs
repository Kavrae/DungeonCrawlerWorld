using Engine.Events;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Auras;

[TestClass]
public sealed class AuraCatalogTests
{
    private static readonly Guid FirstAuraGuid = new("00000000-0000-0000-0000-0000000000b1");
    private static readonly Guid SecondAuraGuid = new("00000000-0000-0000-0000-0000000000b2");

    [TestMethod]
    public void Register_AssignsIdsInRegistrationOrder()
    {
        var auras = new AuraCatalog();

        var firstAuraId = auras.Register(new AuraDefinition(FirstAuraGuid, "First", Color.Red));
        var secondAuraId = auras.Register(new AuraDefinition(SecondAuraGuid, "Second", Color.Blue));

        Assert.AreEqual(0, firstAuraId);
        Assert.AreEqual(1, secondAuraId);
        Assert.AreEqual(secondAuraId, auras.GetId(SecondAuraGuid));
        Assert.AreEqual("Second", auras.Get(secondAuraId).Name);
    }

    /// <summary>Registering is how a definition becomes an id, so content registers the same one over and over: that must change nothing.</summary>
    [TestMethod]
    public void Register_TheSameDefinitionAgain_ReturnsItsId()
    {
        var auras = new AuraCatalog();
        var definition = new AuraDefinition(FirstAuraGuid, "First", Color.Red, [new Effect([new DirectHeal(0f, FlatAmount: 1f)])]);
        var auraId = auras.Register(definition);

        Assert.AreEqual(auraId, auras.Register(definition));
        Assert.AreEqual(auraId, auras.Register(new AuraDefinition(FirstAuraGuid, "First", Color.Red, [new Effect([new DirectHeal(0f, FlatAmount: 1f)])])));
        Assert.AreEqual(1, auras.Count);
    }

    /// <summary>A Guid means what it was first registered as for the whole session: nothing replaces a definition once it is in use.</summary>
    [TestMethod]
    public void Register_ADifferentDefinitionUnderTheSameGuid_KeepsTheFirst()
    {
        var auras = new AuraCatalog();
        var original = new AuraDefinition(FirstAuraGuid, "Original", Color.Red);
        var originalAuraId = auras.Register(original);

        var laterAuraId = auras.Register(new AuraDefinition(FirstAuraGuid, "Later", Color.Blue));

        Assert.AreEqual(originalAuraId, laterAuraId);
        Assert.AreEqual(1, auras.Count);
        Assert.AreSame(original, auras.Get(originalAuraId));
    }

    [TestMethod]
    public void GetId_UnregisteredGuid_ThrowsNamingIt()
    {
        var auras = new AuraCatalog();

        var thrown = Assert.ThrowsExactly<KeyNotFoundException>(() => auras.GetId(FirstAuraGuid));

        StringAssert.Contains(thrown.Message, FirstAuraGuid.ToString());
        Assert.IsFalse(auras.TryGetId(FirstAuraGuid, out _));
    }

    /// <summary>An aura made at runtime needs nothing registered first: radiating it is what registers it.</summary>
    [TestMethod]
    public void AuraSources_RadiatingADefinitionTheSessionHasNotMet_RegistersIt()
    {
        var auras = new AuraCatalog();
        var sourcePool = EmptyPools.Multi<AuraSourceComponent>();
        var sources = new AuraSources(sourcePool, auras, new EventBus());
        var madeAtRuntime = new AuraDefinition(FirstAuraGuid, "Made at runtime", Color.Red);

        sources.Apply(entityId: 3, madeAtRuntime, power: 4, size: 2);

        Assert.IsTrue(auras.TryGetId(FirstAuraGuid, out var auraId));
        Assert.AreSame(madeAtRuntime, auras.Get(auraId));
        Assert.AreEqual(auraId, sourcePool.GetReadonlyByDenseIndex(sourcePool.GetFirstDenseIndex(3)).AuraId);
    }
}
