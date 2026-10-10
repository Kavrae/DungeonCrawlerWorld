using Engine.ECS.Components;
using Engine.ECS.Entities;
using Engine.ECS.Relationships;

namespace Tests.ECS.Relationships;

[TestClass]
public sealed class RelationshipTests
{
    private readonly record struct OwnedByLink(EntityKey TargetKey, int Tag = 0) : IRelationshipLink;

    private readonly record struct PartOfLink(EntityKey TargetKey) : IRelationshipLink;

    private readonly record struct UnrelatedComponent(int Value);

    private readonly record struct UnlinkRecord(int SourceEntityId, int TargetEntityId, int Tag, UnlinkReason Reason);

    private sealed class TestRelationshipWorld
    {
        public TestRelationshipWorld(TargetDestroyedPolicy ownedByPolicy = TargetDestroyedPolicy.UnlinkSources)
        {
            ComponentManager = new ComponentManager(initialEntityCapacity: 8, initialComponentCapacity: 4);
            ComponentManager.RegisterPackedPool<UnrelatedComponent>(static (ref existing, incoming) => existing = incoming);
            OwnedBy = ComponentManager.RegisterRelationship<OwnedByLink>(new RelationshipSpec(ownedByPolicy));
            PartOf = ComponentManager.RegisterRelationship<PartOfLink>(new RelationshipSpec(TargetDestroyedPolicy.DestroySources, Acyclic: true, MaximumDepth: 3));
            EntityManager = new EntityManager(ComponentManager, initialCapacity: 8);
            OwnedBy.SourceUnlinked += (int sourceEntityId, int targetEntityId, in OwnedByLink link, UnlinkReason reason) =>
                OwnedByUnlinks.Add(new UnlinkRecord(sourceEntityId, targetEntityId, link.Tag, reason));
        }

        public ComponentManager ComponentManager { get; }

        public EntityManager EntityManager { get; }

        public Relationship<OwnedByLink> OwnedBy { get; }

        public Relationship<PartOfLink> PartOf { get; }

        public List<UnlinkRecord> OwnedByUnlinks { get; } = [];

        public EntityKey KeyOf(int entityId) => EntityManager.Keys.GetKey(entityId);

        public int Create() => EntityManager.CreateEntity();

        public List<int> SourcesOf<TLink>(Relationship<TLink> relationship, int targetEntityId) where TLink : struct, IRelationshipLink
        {
            var sources = new List<int>();
            relationship.CopySourceEntityIds(targetEntityId, sources);
            sources.Sort();
            return sources;
        }
    }

    [TestMethod]
    public void Link_AddsTheSourceToItsTarget()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var first = world.Create();
        var second = world.Create();

        world.OwnedBy.Link(first, new OwnedByLink(world.KeyOf(owner)));
        world.OwnedBy.Link(second, new OwnedByLink(world.KeyOf(owner)));

        CollectionAssert.AreEqual(new[] { first, second }, world.SourcesOf(world.OwnedBy, owner));
        Assert.AreEqual(2, world.OwnedBy.CountSources(owner));
        Assert.IsTrue(world.OwnedBy.TryGetTargetEntityId(first, out var target));
        Assert.AreEqual(owner, target);
    }

    [TestMethod]
    public void WritingTheLinkPoolDirectly_KeepsTheTargetSideInStep()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var addedSource = world.Create();
        var mergedSource = world.Create();

        world.OwnedBy.Links.Add(addedSource, new OwnedByLink(world.KeyOf(owner)));
        world.ComponentManager.Merge(mergedSource, new OwnedByLink(world.KeyOf(owner)));

        CollectionAssert.AreEqual(new[] { addedSource, mergedSource }, world.SourcesOf(world.OwnedBy, owner));
    }

    [TestMethod]
    public void RemovingTheLink_RemovesTheSourceFromItsTargetAsUnlinked()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var source = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner), Tag: 7));

        world.ComponentManager.RemoveComponent<OwnedByLink>(source);

        Assert.AreEqual(0, world.OwnedBy.CountSources(owner));
        Assert.IsFalse(world.OwnedBy.TryGetTargetEntityId(source, out _));
        CollectionAssert.AreEqual(new[] { new UnlinkRecord(source, owner, 7, UnlinkReason.Unlinked) }, world.OwnedByUnlinks);
    }

    [TestMethod]
    public void ChangingOnlyTheLinksData_KeepsItsTargetAndRaisesNothing()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var source = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner), Tag: 1));

        world.OwnedBy.Links.TrySet(source, new OwnedByLink(world.KeyOf(owner), Tag: 2));

        CollectionAssert.AreEqual(new[] { source }, world.SourcesOf(world.OwnedBy, owner));
        Assert.IsEmpty(world.OwnedByUnlinks);
    }

    [TestMethod]
    public void LinkingToAnotherTarget_MovesTheSourceAsRetargeted()
    {
        var world = new TestRelationshipWorld();
        var firstOwner = world.Create();
        var secondOwner = world.Create();
        var source = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(firstOwner)));

        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(secondOwner), Tag: 3));

        Assert.AreEqual(0, world.OwnedBy.CountSources(firstOwner));
        CollectionAssert.AreEqual(new[] { source }, world.SourcesOf(world.OwnedBy, secondOwner));
        CollectionAssert.AreEqual(new[] { new UnlinkRecord(source, firstOwner, 3, UnlinkReason.Retargeted) }, world.OwnedByUnlinks);
    }

    [TestMethod]
    public void Link_ToItself_IsRefusedAndWritesNothing()
    {
        var world = new TestRelationshipWorld();
        var source = world.Create();

        Assert.ThrowsExactly<InvalidOperationException>(() => world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(source))));

        Assert.IsFalse(world.OwnedBy.Links.Has(source));
    }

    [TestMethod]
    public void Link_ToADestroyedEntity_IsRefusedAndWritesNothing()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var source = world.Create();
        var ownerKey = world.KeyOf(owner);
        world.EntityManager.DestroyEntity(owner);

        Assert.ThrowsExactly<InvalidOperationException>(() => world.OwnedBy.Link(source, new OwnedByLink(ownerKey)));

        Assert.IsFalse(world.OwnedBy.Links.Has(source));
    }

    [TestMethod]
    public void Link_ToAnEntityBeingDestroyed_IsRefusedAndLeavesNoStaleTarget()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var source = world.Create();
        var ownerKey = world.KeyOf(owner);
        var refused = false;
        world.EntityManager.EntityDestroying += entityId =>
        {
            if (entityId == owner)
            {
                refused = Assert.ThrowsExactly<InvalidOperationException>(() => world.OwnedBy.Link(source, new OwnedByLink(ownerKey))) is not null;
            }
        };

        world.EntityManager.DestroyEntity(owner);
        var reusedId = world.Create();

        Assert.IsTrue(refused);
        Assert.IsFalse(world.OwnedBy.Links.Has(source));
        Assert.IsFalse(world.OwnedBy.TryGetTargetEntityId(source, out _));
        Assert.AreEqual(0, world.OwnedBy.CountSources(reusedId));
    }

    [TestMethod]
    public void ARefusedWriteStraightToThePool_LeavesTheSourceUnlinked()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var source = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner)));

        Assert.ThrowsExactly<InvalidOperationException>(() => world.OwnedBy.Links.TrySet(source, new OwnedByLink(world.KeyOf(source))));

        Assert.IsFalse(world.OwnedBy.Links.Has(source));
        Assert.IsFalse(world.OwnedBy.TryGetTargetEntityId(source, out _));
        Assert.AreEqual(0, world.OwnedBy.CountSources(owner));
    }

    [TestMethod]
    public void DestroyingATarget_WhileAHandlerDestroysAnotherOfItsSources_DetachesEachOnce()
    {
        var world = new TestRelationshipWorld(TargetDestroyedPolicy.DestroySources);
        var owner = world.Create();
        var firstSource = world.Create();
        var secondSource = world.Create();
        world.OwnedBy.Link(firstSource, new OwnedByLink(world.KeyOf(owner), Tag: 1));
        world.OwnedBy.Link(secondSource, new OwnedByLink(world.KeyOf(owner), Tag: 2));
        world.OwnedBy.SourceUnlinked += (int sourceEntityId, int targetEntityId, in OwnedByLink link, UnlinkReason reason) =>
        {
            var otherSource = sourceEntityId == firstSource ? secondSource : firstSource;
            if (reason == UnlinkReason.TargetDestroyed && world.EntityManager.EntityExists(otherSource) && !world.EntityManager.IsDestroying(otherSource))
            {
                world.EntityManager.DestroyEntity(otherSource);
            }
        };

        world.EntityManager.DestroyEntity(owner);

        Assert.AreEqual(0, world.EntityManager.LivingEntityCount);
        Assert.AreEqual(0, world.OwnedBy.Links.Count);
        Assert.HasCount(2, world.OwnedByUnlinks);
    }

    [TestMethod]
    public void SourceUnlinked_HandsEveryHandlerTheUnlinkedValue_EvenAfterAnEarlierOneRewritesThePool()
    {
        foreach (var linkFirstSourceFirst in new[] { true, false })
        {
            var world = new TestRelationshipWorld(TargetDestroyedPolicy.DestroySources);
            var owner = world.Create();
            var otherOwner = world.Create();
            var firstSource = world.Create();
            var secondSource = world.Create();
            var newcomer = world.Create();
            var bystander = world.Create();
            var tagBySource = new Dictionary<int, int> { [firstSource] = 1, [secondSource] = 2 };
            foreach (var source in linkFirstSourceFirst ? new[] { firstSource, secondSource } : [secondSource, firstSource])
            {
                world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner), Tag: tagBySource[source]));
            }

            world.EntityManager.EntityDestroying += entityId =>
            {
                if (entityId == bystander)
                {
                    world.OwnedBy.Link(newcomer, new OwnedByLink(world.KeyOf(otherOwner), Tag: 99));
                }
            };
            var destroyedOtherSource = false;
            world.OwnedBy.SourceUnlinked += (int sourceEntityId, int targetEntityId, in OwnedByLink link, UnlinkReason reason) =>
            {
                if (reason == UnlinkReason.TargetDestroyed && !destroyedOtherSource)
                {
                    destroyedOtherSource = true;
                    world.EntityManager.DestroyEntity(sourceEntityId == firstSource ? secondSource : firstSource);
                    world.EntityManager.DestroyEntity(bystander);
                }
            };
            var tagSeenBySource = new Dictionary<int, int>();
            world.OwnedBy.SourceUnlinked += (int sourceEntityId, int targetEntityId, in OwnedByLink link, UnlinkReason reason) =>
                tagSeenBySource[sourceEntityId] = link.Tag;

            world.EntityManager.DestroyEntity(owner);

            Assert.IsNotEmpty(tagSeenBySource);
            foreach (var (sourceEntityId, tagSeen) in tagSeenBySource)
            {
                Assert.AreEqual(tagBySource[sourceEntityId], tagSeen);
            }
        }
    }

    [TestMethod]
    public void TheTargetSide_CannotBeWrittenOutsideTheRelationship()
    {
        var world = new TestRelationshipWorld();
        var entity = world.Create();

        Assert.ThrowsExactly<InvalidOperationException>(() => world.ComponentManager.GetMultiPool<RelatedSourceComponent<OwnedByLink>>());
        Assert.ThrowsExactly<InvalidOperationException>(() => world.ComponentManager.Merge(entity, default(RelatedSourceComponent<OwnedByLink>)));
        Assert.ThrowsExactly<InvalidOperationException>(() => world.ComponentManager.RemoveComponent<RelatedSourceComponent<OwnedByLink>>(entity));
    }

    [TestMethod]
    public void DestroyingATarget_WithUnlinkSources_LeavesTheSourcesAliveAndUnlinked()
    {
        var world = new TestRelationshipWorld(TargetDestroyedPolicy.UnlinkSources);
        var owner = world.Create();
        var source = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner), Tag: 4));

        world.EntityManager.DestroyEntity(owner);

        Assert.IsTrue(world.EntityManager.EntityExists(source));
        Assert.IsFalse(world.OwnedBy.Links.Has(source));
        Assert.IsFalse(world.OwnedBy.TryGetTargetEntityId(source, out _));
        CollectionAssert.AreEqual(new[] { new UnlinkRecord(source, owner, 4, UnlinkReason.TargetDestroyed) }, world.OwnedByUnlinks);
    }

    [TestMethod]
    public void DestroyingATarget_WithDestroySources_DestroysThemBeforeItsOwnHandlersRun()
    {
        var world = new TestRelationshipWorld(TargetDestroyedPolicy.DestroySources);
        var owner = world.Create();
        var source = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner), Tag: 5));
        var sourceAliveWhenOwnerHandlerRan = true;
        var ownerResolvableWhenSourceHandlerRan = false;
        var sourceKey = world.KeyOf(source);
        var ownerKey = world.KeyOf(owner);
        world.EntityManager.EntityDestroying += entityId =>
        {
            if (entityId == owner)
            {
                sourceAliveWhenOwnerHandlerRan = world.EntityManager.Keys.TryGetEntityId(sourceKey, out _);
            }
            else if (entityId == source)
            {
                ownerResolvableWhenSourceHandlerRan = world.EntityManager.Keys.TryGetEntityId(ownerKey, out _);
            }
        };

        world.EntityManager.DestroyEntity(owner);

        Assert.IsFalse(world.EntityManager.EntityExists(source));
        Assert.IsFalse(sourceAliveWhenOwnerHandlerRan);
        Assert.IsTrue(ownerResolvableWhenSourceHandlerRan);
        CollectionAssert.AreEqual(new[] { new UnlinkRecord(source, owner, 5, UnlinkReason.TargetDestroyed) }, world.OwnedByUnlinks);
    }

    [TestMethod]
    public void DestroyingATarget_WithDestroySources_CascadesDownAChain()
    {
        var world = new TestRelationshipWorld();
        var building = world.Create();
        var wing = world.Create();
        var door = world.Create();
        world.PartOf.Link(wing, new PartOfLink(world.KeyOf(building)));
        world.PartOf.Link(door, new PartOfLink(world.KeyOf(wing)));

        world.EntityManager.DestroyEntity(building);

        Assert.AreEqual(0, world.EntityManager.LivingEntityCount);
        Assert.AreEqual(0, world.PartOf.Links.Count);
    }

    [TestMethod]
    public void DestroyingASource_RemovesItFromItsTargetAsSourceDestroyed()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var source = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner), Tag: 6));

        world.EntityManager.DestroyEntity(source);

        Assert.AreEqual(0, world.OwnedBy.CountSources(owner));
        CollectionAssert.AreEqual(new[] { new UnlinkRecord(source, owner, 6, UnlinkReason.SourceDestroyed) }, world.OwnedByUnlinks);
    }

    [TestMethod]
    public void ARecycledTargetId_HasNoSources()
    {
        var world = new TestRelationshipWorld(TargetDestroyedPolicy.UnlinkSources);
        var owner = world.Create();
        var source = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner)));
        world.EntityManager.DestroyEntity(owner);

        var recycled = world.Create();

        Assert.AreEqual(owner, recycled);
        Assert.AreEqual(0, world.OwnedBy.CountSources(recycled));
    }

    [TestMethod]
    public void ARecycledSourceId_IsNotLinked()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var source = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner)));
        world.EntityManager.DestroyEntity(source);

        var recycled = world.Create();

        Assert.AreEqual(source, recycled);
        Assert.IsFalse(world.OwnedBy.TryGetTargetEntityId(recycled, out _));
        Assert.AreEqual(0, world.OwnedBy.CountSources(owner));
    }

    [TestMethod]
    public void AnAcyclicRelationship_RefusesALinkThatClosesACycle()
    {
        var world = new TestRelationshipWorld();
        var parent = world.Create();
        var child = world.Create();
        world.PartOf.Link(child, new PartOfLink(world.KeyOf(parent)));

        Assert.ThrowsExactly<InvalidOperationException>(() => world.PartOf.Link(parent, new PartOfLink(world.KeyOf(child))));

        Assert.IsFalse(world.PartOf.Links.Has(parent));
    }

    [TestMethod]
    public void AnAcyclicRelationship_RefusesAChainLongerThanItsMaximumDepth()
    {
        var world = new TestRelationshipWorld();
        var chain = Enumerable.Range(0, 5).Select(_ => world.Create()).ToArray();
        world.PartOf.Link(chain[1], new PartOfLink(world.KeyOf(chain[0])));
        world.PartOf.Link(chain[2], new PartOfLink(world.KeyOf(chain[1])));
        world.PartOf.Link(chain[3], new PartOfLink(world.KeyOf(chain[2])));

        Assert.ThrowsExactly<InvalidOperationException>(() => world.PartOf.Link(chain[4], new PartOfLink(world.KeyOf(chain[3]))));
    }

    [TestMethod]
    public void AnAcyclicRelationship_RefusesAttachingABranchThatMakesTheChainTooLong()
    {
        var world = new TestRelationshipWorld();
        var root = world.Create();
        var child = world.Create();
        var branchTop = world.Create();
        var branchMiddle = world.Create();
        var branchBottom = world.Create();
        world.PartOf.Link(child, new PartOfLink(world.KeyOf(root)));
        world.PartOf.Link(branchMiddle, new PartOfLink(world.KeyOf(branchTop)));
        world.PartOf.Link(branchBottom, new PartOfLink(world.KeyOf(branchMiddle)));

        Assert.ThrowsExactly<InvalidOperationException>(() => world.PartOf.Link(branchTop, new PartOfLink(world.KeyOf(child))));

        Assert.IsFalse(world.PartOf.Links.Has(branchTop));
    }

    [TestMethod]
    public void AnAcyclicRelationship_AcceptsAttachingABranchThatFitsTheMaximumDepth()
    {
        var world = new TestRelationshipWorld();
        var root = world.Create();
        var branchTop = world.Create();
        var branchMiddle = world.Create();
        var branchBottom = world.Create();
        world.PartOf.Link(branchMiddle, new PartOfLink(world.KeyOf(branchTop)));
        world.PartOf.Link(branchBottom, new PartOfLink(world.KeyOf(branchMiddle)));

        world.PartOf.Link(branchTop, new PartOfLink(world.KeyOf(root)));

        Assert.AreEqual(root, world.PartOf.RootOf(branchBottom));
    }

    [TestMethod]
    public void RootOf_FollowsTheChainToItsTop_AndIsTheEntityItselfWhenUnlinked()
    {
        var world = new TestRelationshipWorld();
        var building = world.Create();
        var wing = world.Create();
        var door = world.Create();
        world.PartOf.Link(wing, new PartOfLink(world.KeyOf(building)));
        world.PartOf.Link(door, new PartOfLink(world.KeyOf(wing)));

        Assert.AreEqual(building, world.PartOf.RootOf(door));
        Assert.AreEqual(building, world.PartOf.RootOf(wing));
        Assert.AreEqual(building, world.PartOf.RootOf(building));
    }

    [TestMethod]
    public void RootOf_FollowsARetargetedBranch()
    {
        var world = new TestRelationshipWorld();
        var firstBuilding = world.Create();
        var secondBuilding = world.Create();
        var wing = world.Create();
        var door = world.Create();
        world.PartOf.Link(wing, new PartOfLink(world.KeyOf(firstBuilding)));
        world.PartOf.Link(door, new PartOfLink(world.KeyOf(wing)));

        world.PartOf.Link(wing, new PartOfLink(world.KeyOf(secondBuilding)));

        Assert.AreEqual(secondBuilding, world.PartOf.RootOf(door));
    }

    [TestMethod]
    public void CopyDescendantEntityIds_ListsEveryLevel_EachAfterItsParent()
    {
        var world = new TestRelationshipWorld();
        var building = world.Create();
        var westWing = world.Create();
        var eastWing = world.Create();
        var westDoor = world.Create();
        var eastWindow = world.Create();
        world.PartOf.Link(westWing, new PartOfLink(world.KeyOf(building)));
        world.PartOf.Link(eastWing, new PartOfLink(world.KeyOf(building)));
        world.PartOf.Link(westDoor, new PartOfLink(world.KeyOf(westWing)));
        world.PartOf.Link(eastWindow, new PartOfLink(world.KeyOf(eastWing)));
        var descendants = new List<int> { 99 };

        world.PartOf.CopyDescendantEntityIds(building, descendants);

        CollectionAssert.AreEquivalent(new[] { westWing, eastWing, westDoor, eastWindow }, descendants);
        Assert.IsLessThan(descendants.IndexOf(westDoor), descendants.IndexOf(westWing));
        Assert.IsLessThan(descendants.IndexOf(eastWindow), descendants.IndexOf(eastWing));
    }

    [TestMethod]
    public void CopyDescendantEntityIds_OfALeaf_IsEmpty()
    {
        var world = new TestRelationshipWorld();
        var building = world.Create();
        var door = world.Create();
        world.PartOf.Link(door, new PartOfLink(world.KeyOf(building)));
        var descendants = new List<int> { 99 };

        world.PartOf.CopyDescendantEntityIds(door, descendants);

        Assert.IsEmpty(descendants);
    }

    [TestMethod]
    public void HierarchyLookups_OnARelationshipThatIsNotAcyclic_Throw()
    {
        var world = new TestRelationshipWorld();
        var entity = world.Create();

        Assert.ThrowsExactly<InvalidOperationException>(() => world.OwnedBy.RootOf(entity));
        Assert.ThrowsExactly<InvalidOperationException>(() => world.OwnedBy.CopyDescendantEntityIds(entity, []));
    }

    [TestMethod]
    public void ACycleInARelationshipThatAllowsOne_StillEndsWhenDestroyed()
    {
        var world = new TestRelationshipWorld(TargetDestroyedPolicy.DestroySources);
        var first = world.Create();
        var second = world.Create();
        world.OwnedBy.Link(first, new OwnedByLink(world.KeyOf(second)));
        world.OwnedBy.Link(second, new OwnedByLink(world.KeyOf(first)));

        world.EntityManager.DestroyEntity(first);

        Assert.AreEqual(0, world.EntityManager.LivingEntityCount);
        Assert.AreEqual(0, world.OwnedBy.Links.Count);
    }

    [TestMethod]
    public void DestroyingAnEntityWithNoLinks_TouchesNoRelationship()
    {
        var world = new TestRelationshipWorld();
        var owner = world.Create();
        var source = world.Create();
        var bystander = world.Create();
        world.OwnedBy.Link(source, new OwnedByLink(world.KeyOf(owner)));

        world.EntityManager.DestroyEntity(bystander);

        CollectionAssert.AreEqual(new[] { source }, world.SourcesOf(world.OwnedBy, owner));
        Assert.IsEmpty(world.OwnedByUnlinks);
    }

    [TestMethod]
    public void WritingALink_ToAKeyNeverIssued_Throws()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);
        var ownedBy = componentManager.RegisterRelationship<OwnedByLink>(new RelationshipSpec(TargetDestroyedPolicy.UnlinkSources));

        Assert.ThrowsExactly<InvalidOperationException>(() => ownedBy.Link(0, new OwnedByLink(new EntityKey(1))));
    }

    [TestMethod]
    public void ARelationshipRegisteredAfterTheEntityManager_StillWorks()
    {
        var componentManager = new ComponentManager(initialEntityCapacity: 4, initialComponentCapacity: 4);
        var entityManager = new EntityManager(componentManager, initialCapacity: 4);
        var ownedBy = componentManager.RegisterRelationship<OwnedByLink>(new RelationshipSpec(TargetDestroyedPolicy.UnlinkSources));
        var owner = entityManager.CreateEntity();
        var source = entityManager.CreateEntity();

        ownedBy.Link(source, new OwnedByLink(entityManager.Keys.GetKey(owner)));

        Assert.AreEqual(1, ownedBy.CountSources(owner));
    }

    [TestMethod]
    public void TheRegistry_KnowsBothSidesComponentTypes()
    {
        var world = new TestRelationshipWorld();

        Assert.IsTrue(world.ComponentManager.Relationships.IsRelationshipComponentType(typeof(OwnedByLink)));
        Assert.IsTrue(world.ComponentManager.Relationships.IsRelationshipComponentType(typeof(RelatedSourceComponent<OwnedByLink>)));
        Assert.IsFalse(world.ComponentManager.Relationships.IsRelationshipComponentType(typeof(UnrelatedComponent)));
        Assert.AreSame(world.OwnedBy, world.ComponentManager.GetRelationship<OwnedByLink>());
    }
}
