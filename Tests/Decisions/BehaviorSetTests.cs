using Engine.Decisions;
using Engine.Tags;
using static Tests.Decisions.DecisionTestKit;

namespace Tests.Decisions;

[TestClass]
public sealed class BehaviorSetTests
{
    [TestMethod]
    public void ALaterGrant_ReplacesAnEarlierOne_AndRankZeroTakesItAway()
    {
        var flee = new FakeBehavior("Flee");
        var wander = new FakeBehavior("Wander");
        var catalog = FrozenCatalog(flee, wander);

        var set = BehaviorSet<FakeContext>.Resolve(catalog, [[Grant(flee, 3), Grant(wander, 1)], [Grant(flee, 2, weight: 5f)], [Grant(wander, 0)]], []);

        var entry = Assert.ContainsSingle(set.Entries.ToArray());
        Assert.AreSame(flee, entry.Behavior);
        Assert.AreEqual(2, entry.Rank);
        Assert.AreEqual(5f, entry.ResolvedWeight);
    }

    [TestMethod]
    public void ABehaviorGrantedAgainAfterRankZero_IsHeldOnce()
    {
        var flee = new FakeBehavior("Flee");
        var wander = new FakeBehavior("Wander");
        var catalog = FrozenCatalog(flee, wander);

        var set = BehaviorSet<FakeContext>.Resolve(catalog, [[Grant(flee, 3), Grant(wander, 1)], [Grant(flee, 0)], [Grant(flee, 2)]], []);

        Assert.AreEqual(2, set.Count);
        Assert.AreEqual(1, set.Entries.ToArray().Count(entry => ReferenceEquals(entry.Behavior, flee)));
    }

    [TestMethod]
    public void AModifierByTag_ReachesEveryBehaviorUnderIt_AndOneById_ReachesOnlyThatBehavior()
    {
        var flee = new FakeBehavior("Flee", [Caution]);
        var engage = new FakeBehavior("Engage", [Aggression]);
        var idle = new FakeBehavior("Idle");
        var catalog = FrozenCatalog(flee, engage, idle);

        var set = BehaviorSet<FakeContext>.Resolve(catalog, [[Grant(flee, 3, weight: 2f), Grant(engage, 3), Grant(idle, 1)]],
        [
            new BehaviorModifier(3f, Tag: Drive),
            new BehaviorModifier(0.5f, Tag: Caution),
            new BehaviorModifier(10f, BehaviorId: idle.Id),
        ]);

        Assert.AreEqual(3f, WeightOf(set, flee));
        Assert.AreEqual(3f, WeightOf(set, engage));
        Assert.AreEqual(10f, WeightOf(set, idle));
    }

    [TestMethod]
    public void AModifierAimedAtABehaviorTheSetDoesntHold_DoesNothing()
    {
        var flee = new FakeBehavior("Flee", [Caution]);
        var engage = new FakeBehavior("Engage", [Aggression]);
        var catalog = FrozenCatalog(flee, engage);

        var set = BehaviorSet<FakeContext>.Resolve(catalog, [[Grant(flee, 3)]], [new BehaviorModifier(4f, Tag: Aggression), new BehaviorModifier(4f, BehaviorId: engage.Id)]);

        Assert.AreEqual(1f, WeightOf(set, flee));
    }

    [TestMethod]
    public void AGrantNamingAnUnknownBehavior_ThrowsNamingIt()
    {
        var catalog = FrozenCatalog(new FakeBehavior("Wander"));
        var unknown = Guid.NewGuid();

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BehaviorSet<FakeContext>.Resolve(catalog, [[new BehaviorGrant<FakeContext>(unknown, 1)]], []));

        Assert.Contains(unknown.ToString(), exception.Message);
    }

    [TestMethod]
    public void AModifierTagOutsideTheBehaviorDomain_Throws()
    {
        var flee = new FakeBehavior("Flee", [Caution]);
        var catalog = FrozenCatalog(flee);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => BehaviorSet<FakeContext>.Resolve(catalog, [[Grant(flee, 3)]], [new BehaviorModifier(2f, Tag: OutsideTheDomain)]));

        Assert.Contains(OutsideTheDomain.Name, exception.Message);
    }

    [TestMethod]
    public void ABehaviorTaggedOutsideTheBehaviorDomain_FailsTheFreeze()
    {
        var catalog = new BehaviorCatalog<FakeContext>();
        catalog.Register(new FakeBehavior("Odd", [OutsideTheDomain]));

        var exception = Assert.ThrowsExactly<InvalidOperationException>(() => catalog.Freeze(DecisionTags, BehaviorDomain));

        Assert.Contains("Odd", exception.Message);
        Assert.Contains(OutsideTheDomain.Name, exception.Message);
    }

    [TestMethod]
    public void AModifierNamingBothOrNeither_Throws()
    {
        var flee = new FakeBehavior("Flee", [Caution]);
        var catalog = FrozenCatalog(flee);

        Assert.ThrowsExactly<InvalidOperationException>(() => BehaviorSet<FakeContext>.Resolve(catalog, [[Grant(flee, 3)]], [new BehaviorModifier(2f)]));
        Assert.ThrowsExactly<InvalidOperationException>(() => BehaviorSet<FakeContext>.Resolve(catalog, [[Grant(flee, 3)]], [new BehaviorModifier(2f, Caution, flee.Id)]));
    }

    [TestMethod]
    public void Entries_AreSortedByRankCeiling_KeepingGrantOrderWithinARank()
    {
        var idle = new FakeBehavior("Idle");
        var engage = new FakeBehavior("Engage");
        var heal = new FakeBehavior("Heal");
        var flee = new FakeBehavior("Flee");
        var catalog = FrozenCatalog(idle, engage, heal, flee);

        var set = Set(catalog, Grant(idle, 1), Grant(engage, 3), Grant(heal, 4), Grant(flee, 3));

        CollectionAssert.AreEqual(new[] { "Heal", "Engage", "Flee", "Idle" }, set.Entries.ToArray().Select(entry => entry.Behavior.Name).ToArray());
    }

    [TestMethod]
    public void ASetResolves_OnlyAgainstAFrozenCatalog()
    {
        var catalog = new BehaviorCatalog<FakeContext>();
        var wander = new FakeBehavior("Wander");
        catalog.Register(wander);

        Assert.ThrowsExactly<InvalidOperationException>(() => Set(catalog, Grant(wander, 1)));
    }

    [TestMethod]
    public void TheCatalog_NumbersBehaviorsInRegistrationOrder_AndRefusesRegistrationOnceFrozen()
    {
        var first = new FakeBehavior("First");
        var second = new FakeBehavior("Second");
        var catalog = FrozenCatalog(first, second);

        Assert.AreEqual(1, catalog.SessionIdOf(first.Id));
        Assert.AreEqual(2, catalog.SessionIdOf(second.Id));
        Assert.AreSame(second, catalog.BySessionId(2));
        Assert.IsNull(catalog.BySessionId(0));
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.Register(new FakeBehavior("Late")));
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.Freeze(DecisionTags, BehaviorDomain));
    }

    [TestMethod]
    public void AGrantNamingARedirectedGuid_GetsWhatItRedirectsTo()
    {
        var replacement = new FakeBehavior("Replacement");
        var catalog = new BehaviorCatalog<FakeContext>();
        catalog.Register(replacement);
        var retired = Guid.NewGuid();
        catalog.AddRedirect(retired, replacement.Id);
        catalog.Freeze(DecisionTags, BehaviorDomain);

        var set = BehaviorSet<FakeContext>.Resolve(catalog, [[new BehaviorGrant<FakeContext>(retired, 2)]], []);

        Assert.AreSame(replacement, set[0].Behavior);
        Assert.AreEqual(catalog.SessionIdOf(replacement.Id), set[0].SessionId);
    }

    private static float WeightOf(BehaviorSet<FakeContext> set, FakeBehavior behavior) =>
        set.Entries.ToArray().Single(entry => ReferenceEquals(entry.Behavior, behavior)).ResolvedWeight;
}
