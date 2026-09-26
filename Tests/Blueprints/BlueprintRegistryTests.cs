using Game.Blueprints;
using Game.Spawning;

namespace Tests.Blueprints;

[TestClass]
public sealed class BlueprintRegistryTests
{
    private static readonly Guid FirstId = Guid.NewGuid();
    private static readonly Guid SecondId = Guid.NewGuid();

    private static BlueprintDefinition Race(Guid id, string name) =>
        new(id, name) { Race = new RaceFacet() };

    private static BlueprintDefinition Plain(Guid id, string name, params Guid[] includes) => new(id, name) { Includes = includes };

    [TestMethod]
    public void Register_AssignsIdsFromOne_LeavingZeroAsNone()
    {
        var definitions = new BlueprintRegistry();

        Assert.AreEqual((ushort)1, definitions.Register(Race(FirstId, "First")));
        Assert.AreEqual((ushort)2, definitions.Register(Plain(SecondId, "Second")));
        Assert.IsFalse(definitions.TryGet(BlueprintRegistry.None, out _));
    }

    [TestMethod]
    public void Register_SameGuidAgain_ReplacesTheDefinitionUnderTheSameId()
    {
        var definitions = new BlueprintRegistry();
        var id = definitions.Register(Race(FirstId, "Original"));

        var replacedId = definitions.Register(Race(FirstId, "Replacement"));

        Assert.AreEqual(id, replacedId);
        Assert.AreEqual("Replacement", definitions.Get(id).Name);
    }

    [TestMethod]
    public void GetId_ResolvesTheGuid_AndThrowsForAnUnknownOne()
    {
        var definitions = new BlueprintRegistry();
        var id = definitions.Register(Race(FirstId, "First"));

        Assert.AreEqual(id, definitions.GetId(FirstId));
        Assert.IsFalse(definitions.TryGetId(SecondId, out _));
        Assert.ThrowsExactly<KeyNotFoundException>(() => definitions.GetId(SecondId));
        Assert.ThrowsExactly<KeyNotFoundException>(() => definitions.Get(5));
    }

    /// <summary>A kind view only answers for its own kind, so a race lookup can't mistake another definition for a race.</summary>
    [TestMethod]
    public void Races_AnotherKindsId_IsNotFound()
    {
        var definitions = new BlueprintRegistry();
        var raceId = definitions.Register(Race(FirstId, "Race"));
        var plainId = definitions.Register(Plain(SecondId, "Plain"));

        Assert.IsTrue(definitions.Races.TryGet(raceId, out _));
        Assert.IsFalse(definitions.Races.TryGet(plainId, out _));
        Assert.IsFalse(definitions.Races.TryGetId(SecondId, out _));
        Assert.IsFalse(definitions.Classes.TryGet(raceId, out _));
    }

    [TestMethod]
    public void Resolve_ReadsRaceAppearanceAndOccupancyThroughIncludes()
    {
        var definitions = new BlueprintRegistry();
        var tinyId = Guid.NewGuid();
        definitions.Register(Race(FirstId, "Race"));
        definitions.Register(new BlueprintDefinition(tinyId, "Tiny") { NonBlocking = Game.Modules.Core.Components.NonBlockingKind.Tiny });
        var composite = definitions.Register(Plain(SecondId, "Tiny Race", FirstId, tinyId));

        var resolved = definitions.Resolve(composite);

        Assert.HasCount(3, resolved.BuildOrder);
        Assert.AreEqual(definitions.GetId(FirstId), resolved.Races.Single());
        Assert.AreEqual(Game.Modules.Core.Components.NonBlockingKind.Tiny, resolved.NonBlocking.Single());
        Assert.IsTrue(resolved.Deferrable);
    }

    [TestMethod]
    public void Resolve_Cycle_Throws()
    {
        var definitions = new BlueprintRegistry();
        definitions.Register(Plain(FirstId, "First", SecondId));
        definitions.Register(Plain(SecondId, "Second", FirstId));

        var error = Assert.ThrowsExactly<InvalidOperationException>(definitions.ResolveAll);
        StringAssert.Contains(error.Message, "includes itself");
    }

    [TestMethod]
    public void Resolve_UnregisteredInclude_Throws()
    {
        var definitions = new BlueprintRegistry();
        var id = definitions.Register(Plain(FirstId, "First", SecondId));

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => definitions.Resolve(id));
        StringAssert.Contains(error.Message, "not registered");
    }

    [TestMethod]
    public void Resolve_IncludesNestedPastTheLimit_Throws()
    {
        var definitions = new BlueprintRegistry();
        var previous = Guid.NewGuid();
        definitions.Register(Plain(previous, "Level 0"));
        for (var level = 1; level <= BlueprintRegistry.MaximumIncludeDepth; level++)
        {
            var next = Guid.NewGuid();
            definitions.Register(Plain(next, $"Level {level}", previous));
            previous = next;
        }

        var error = Assert.ThrowsExactly<InvalidOperationException>(() => definitions.Resolve(definitions.GetId(previous)));
        StringAssert.Contains(error.Message, "deeper than");
    }

    [TestMethod]
    public void Resolve_IncludesNestedToTheLimit_Resolves()
    {
        var definitions = new BlueprintRegistry();
        var previous = Guid.NewGuid();
        definitions.Register(Plain(previous, "Level 0"));
        for (var level = 1; level < BlueprintRegistry.MaximumIncludeDepth; level++)
        {
            var next = Guid.NewGuid();
            definitions.Register(Plain(next, $"Level {level}", previous));
            previous = next;
        }

        Assert.HasCount(BlueprintRegistry.MaximumIncludeDepth, definitions.Resolve(definitions.GetId(previous)).BuildOrder);
    }

    private static readonly AppearanceFacet Complete = new() { DisplayNames = ["Grub"], Description = "A grub.", Glyph = "g", GlyphColor = Microsoft.Xna.Framework.Color.Green, SpriteName = AppearanceFacet.NoSprite };

    [TestMethod]
    public void Appearance_LaterDefinitions_OverrideOnlyTheFieldsTheySet()
    {
        var definitions = new BlueprintRegistry();
        var baseId = Guid.NewGuid();
        definitions.Register(new BlueprintDefinition(baseId, "Base") { Appearance = Complete });
        var id = definitions.Register(new BlueprintDefinition(Guid.NewGuid(), "Variant") { Includes = [baseId], Appearance = new() { Description = "A bigger grub.", GlyphColor = Microsoft.Xna.Framework.Color.Red } });

        var appearance = definitions.Resolve(id).Appearance;

        Assert.AreEqual("A bigger grub.", appearance.Description);
        Assert.AreEqual(Microsoft.Xna.Framework.Color.Red, appearance.GlyphColor);
        Assert.AreEqual("g", appearance.Glyph);
        Assert.AreEqual("Grub", appearance.NameFor(seed: 1));
        Assert.IsNull(appearance.SpriteName, "NoSprite resolves to glyph only.");
        Assert.IsTrue(definitions.Resolve(id).IsSpawnable);
    }

    [TestMethod]
    public void Appearance_ASecondRace_DoesNotChangeTheFirstRacesLook()
    {
        var definitions = new BlueprintRegistry();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        definitions.Register(new BlueprintDefinition(first, "First") { Race = new RaceFacet(), Appearance = Complete });
        definitions.Register(new BlueprintDefinition(second, "Second") { Race = new RaceFacet(), Appearance = Complete with { Glyph = "s", DisplayNames = ["Other"] } });
        var hybrid = definitions.Register(Plain(Guid.NewGuid(), "Hybrid", first, second));

        var appearance = definitions.Resolve(hybrid).Appearance;

        Assert.AreEqual("g", appearance.Glyph);
        Assert.AreEqual("Grub", appearance.NameFor(seed: 1));
    }

    [TestMethod]
    public void Name_ComposesTheDisplayNameThenClassesAndSuffixesInBuildOrder()
    {
        var definitions = new BlueprintRegistry();
        var race = Guid.NewGuid();
        var job = Guid.NewGuid();
        var rank = Guid.NewGuid();
        definitions.Register(new BlueprintDefinition(race, "Race") { Race = new RaceFacet(), Appearance = Complete });
        definitions.Register(new BlueprintDefinition(job, "Digger") { Class = new ClassFacet() });
        definitions.Register(new BlueprintDefinition(rank, "Chief") { Appearance = new() { NameSuffix = "Chief" } });
        var id = definitions.Register(Plain(Guid.NewGuid(), "Grub Digger Chief", race, job, rank));

        Assert.AreEqual("Grub Digger Chief", definitions.Resolve(id).NameFor(seed: 1));
    }

    [TestMethod]
    public void Name_AnExplicitName_ReplacesTheComposedOne()
    {
        var definitions = new BlueprintRegistry();
        var race = Guid.NewGuid();
        var job = Guid.NewGuid();
        definitions.Register(new BlueprintDefinition(race, "Race") { Race = new RaceFacet(), Appearance = Complete });
        definitions.Register(new BlueprintDefinition(job, "Digger") { Class = new ClassFacet() });
        var id = definitions.Register(new BlueprintDefinition(Guid.NewGuid(), "Named") { Includes = [race, job], Appearance = new() { Name = "Gerald" } });

        Assert.AreEqual("Gerald", definitions.Resolve(id).NameFor(seed: 1));
    }

    [TestMethod]
    public void Appearance_ATraitAlone_IsNotSpawnable_AndSaysWhatItLacks()
    {
        var definitions = new BlueprintRegistry();
        var id = definitions.Register(new BlueprintDefinition(Guid.NewGuid(), "Trait") { Appearance = new() { NameSuffix = "Boss" } });

        var resolved = definitions.Resolve(id);

        Assert.IsFalse(resolved.IsSpawnable);
        CollectionAssert.AreEquivalent(new[] { "a name", "a description", "a glyph and its color", "a sprite (or AppearanceFacet.NoSprite)" }, resolved.Appearance.Missing.ToArray());
    }
}
