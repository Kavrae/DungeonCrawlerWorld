using Engine.Events;
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

    /// <summary>Registering is how a definition becomes an id, so content registers the same one over and over: that must change nothing and announce nothing.</summary>
    [TestMethod]
    public void Register_TheSameDefinitionAgain_ReturnsItsIdAndAnnouncesNothing()
    {
        var auras = new AuraCatalog();
        var definition = new AuraDefinition(FirstAuraGuid, "First", Color.Red);
        var auraId = auras.Register(definition);
        var changes = 0;
        auras.DefinitionChanged += _ => changes++;

        Assert.AreEqual(auraId, auras.Register(definition));
        Assert.AreEqual(auraId, auras.Register(new AuraDefinition(FirstAuraGuid, "First", Color.Red) { Effects = definition.Effects }));

        Assert.AreEqual(1, auras.Count);
        Assert.AreEqual(0, changes);
    }

    [TestMethod]
    public void Register_ADifferentDefinitionUnderTheSameGuid_ReplacesItKeepsItsIdAndAnnouncesIt()
    {
        var auras = new AuraCatalog();
        var originalAuraId = auras.Register(new AuraDefinition(FirstAuraGuid, "Original", Color.Red));
        var changedAuraIds = new List<byte>();
        auras.DefinitionChanged += changedAuraIds.Add;

        var replacementAuraId = auras.Register(new AuraDefinition(FirstAuraGuid, "Replacement", Color.Blue));

        Assert.AreEqual(originalAuraId, replacementAuraId);
        Assert.AreEqual(1, auras.Count);
        Assert.AreEqual(Color.Blue, auras.Get(originalAuraId).GlowColor);
        CollectionAssert.AreEqual(new[] { originalAuraId }, changedAuraIds);
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

        sources.Apply(entityId: 3, madeAtRuntime, strength: 4);

        Assert.IsTrue(auras.TryGetId(FirstAuraGuid, out var auraId));
        Assert.AreSame(madeAtRuntime, auras.Get(auraId));
        Assert.AreEqual(auraId, sourcePool.GetReadonlyByDenseIndex(sourcePool.GetFirstDenseIndex(3)).AuraId);
    }
}
