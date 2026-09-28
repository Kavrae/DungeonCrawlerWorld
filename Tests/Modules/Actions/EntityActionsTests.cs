using Engine.ECS.Components;
using Engine.Math;
using Engine.ECS.Components.Stores;
using Game.Blueprints;
using Game.Spawning;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Effects;
using Game.Modules.Class.Components;

namespace Tests.Modules.Actions;

[TestClass]
public sealed class EntityActionsTests
{
    private const int EntityId = 0;

    private static readonly Guid RacialActionId = new("11111111-0000-0000-0000-000000000001");
    private static readonly Guid ClassActionId = new("11111111-0000-0000-0000-000000000002");
    private static readonly Guid UnknownActionId = new("11111111-0000-0000-0000-000000000003");
    private static readonly Guid TraitActionId = new("11111111-0000-0000-0000-000000000004");
    private static readonly Guid OtherClassActionId = new("11111111-0000-0000-0000-000000000005");
    private static readonly Guid RaceId = new("22222222-0000-0000-0000-000000000001");
    private static readonly Guid ClassId = new("22222222-0000-0000-0000-000000000002");
    private static readonly Guid TraitId = new("22222222-0000-0000-0000-000000000003");
    private static readonly Guid CreatureId = new("22222222-0000-0000-0000-000000000004");
    private static readonly Guid ChampionId = new("22222222-0000-0000-0000-000000000005");
    private static readonly Guid OtherClassId = new("22222222-0000-0000-0000-000000000006");

    private sealed record Fixture(EntityActions Actions, ComponentManager Components, ActionCatalog Catalog, BlueprintRegistry Blueprints)
    {
        /// <summary>Makes EntityId an entity spawned from the blueprint registered as blueprintId, the way EntityFactory records one.</summary>
        public void SpawnedAs(Guid blueprintId) =>
            Components.Merge(EntityId, new SpawnRecordComponent(Blueprints.GetId(blueprintId), seed: 1));

        /// <summary>Records blueprintId as applied to EntityId, the way EntityFactory.Apply does.</summary>
        public void Applied(Guid blueprintId)
        {
            var applied = Components.GetMultiPool<AppliedBlueprintComponent>();
            applied.Add(EntityId, new AppliedBlueprintComponent(Blueprints.GetId(blueprintId), (ushort)applied.CountForEntity(EntityId)));
        }
    }

    private static ActionDefinition ActionWithDamage(Guid actionId, string name, short flatDamage, ushort? cooldownFrames = null) =>
        new(actionId, name, null, "#", default, [], [new ActionEffect([new DirectDamage(MinFlatDamage: flatDamage, MaxFlatDamage: flatDamage)])],
            new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, Range: 1), new ActionTiming(ActionTimingCategory.Immediate, ActionLockFrames: 0, CooldownFrames: cooldownFrames)));

    /// <summary>A race and a class, a trait that grants an action of its own, the creature they make, and a champion of it that overrides the racial attack.</summary>
    private static Fixture Build()
    {
        var components = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 10, initialComponentCapacity: 8));

        var catalog = new ActionCatalog();
        catalog.Register(ActionWithDamage(RacialActionId, "Racial Attack", 1, cooldownFrames: 40));
        catalog.Register(ActionWithDamage(ClassActionId, "Class Trick", 2));
        catalog.Register(ActionWithDamage(UnknownActionId, "Nobody Knows This", 3));
        catalog.Register(ActionWithDamage(TraitActionId, "Trait Trick", 4));
        catalog.Register(ActionWithDamage(OtherClassActionId, "Other Class Trick", 5));

        var blueprints = new BlueprintRegistry();
        blueprints.Register(new BlueprintDefinition(RaceId, "Test Race")
        {
            Race = new RaceFacet(),
            Actions = [new ActionGrant(RacialActionId, ActionWithDamage(RacialActionId, "Racial Attack", 11, cooldownFrames: 40))],
        });
        blueprints.Register(new BlueprintDefinition(ClassId, "Test Class") { Class = new ClassFacet(), Actions = [new ActionGrant(ClassActionId)] });
        blueprints.Register(new BlueprintDefinition(OtherClassId, "Other Class") { Class = new ClassFacet(), Actions = [new ActionGrant(OtherClassActionId)] });
        blueprints.Register(new BlueprintDefinition(TraitId, "Test Trait") { Actions = [new ActionGrant(TraitActionId)] });
        blueprints.Register(new BlueprintDefinition(CreatureId, "Test Creature") { Includes = [RaceId, ClassId] });
        blueprints.Register(new BlueprintDefinition(ChampionId, "Test Champion")
        {
            Includes = [CreatureId, TraitId],
            Actions = [new ActionGrant(RacialActionId, ActionWithDamage(RacialActionId, "Champion Attack", 22, cooldownFrames: 40))],
        });

        return new Fixture(EntityActions.For(components, catalog, blueprints), components, catalog, blueprints);
    }

    private static short? FlatDamageOf(ActionDefinition action) =>
        action.Effects.SelectMany(effect => effect.Entries).OfType<DirectDamage>().First().MinFlatDamage;

    [TestMethod]
    public void BlueprintGrants_AreUsableWithoutAnyComponentPerEntity()
    {
        var fixture = Build();
        fixture.SpawnedAs(CreatureId);

        Assert.IsTrue(fixture.Actions.Has(EntityId, RacialActionId));
        Assert.IsTrue(fixture.Actions.Has(EntityId, ClassActionId));
        Assert.IsFalse(fixture.Actions.Has(EntityId, UnknownActionId));
        Assert.IsEmpty(fixture.Components.GetMultiPool<ActionInstanceComponent>().EntityIds.ToArray(), "A definition grant costs the entity no component at all.");
    }

    [TestMethod]
    public void RaceGrantsOverride_IsWhatResolves_AndAGrantWithoutOneFallsBackToTheCatalog()
    {
        var fixture = Build();
        fixture.SpawnedAs(CreatureId);

        Assert.IsTrue(fixture.Actions.TryGetEffectiveAction(EntityId, RacialActionId, out var racial));
        Assert.AreEqual((short)11, FlatDamageOf(racial));

        Assert.IsTrue(fixture.Actions.TryGetEffectiveAction(EntityId, ClassActionId, out var classAction));
        Assert.AreEqual((short)2, FlatDamageOf(classAction));
    }

    /// <summary>Shared actions aren't only a race's or a class's: any definition an entity is built from can grant one.</summary>
    [TestMethod]
    public void ATraitsGrant_IsUsable()
    {
        var fixture = Build();
        fixture.SpawnedAs(ChampionId);

        Assert.IsTrue(fixture.Actions.Has(EntityId, TraitActionId));
    }

    [TestMethod]
    public void ACompositesGrant_OverridesTheRaceItIncludes()
    {
        var fixture = Build();
        fixture.SpawnedAs(ChampionId);

        Assert.IsTrue(fixture.Actions.TryGetEffectiveAction(EntityId, RacialActionId, out var racial));
        Assert.AreEqual((short)22, FlatDamageOf(racial));
    }

    /// <summary>The class slot also holds a class the blueprint built; its grant must not come back un-overridden through the slot.</summary>
    [TestMethod]
    public void AClassTheBlueprintBuilt_IsAnsweredByTheBlueprint_NotAgainThroughItsSlot()
    {
        var fixture = Build();
        var champion = new BlueprintDefinition(Guid.NewGuid(), "Class Champion")
        {
            Includes = [CreatureId],
            Actions = [new ActionGrant(ClassActionId, ActionWithDamage(ClassActionId, "Better Trick", 33))],
        };
        fixture.Blueprints.Register(champion);
        fixture.SpawnedAs(champion.Id);
        fixture.Components.Merge(EntityId, new ClassSlotsComponent(fixture.Blueprints.GetId(ClassId)));

        Assert.IsTrue(fixture.Actions.TryGetEffectiveAction(EntityId, ClassActionId, out var classAction));
        Assert.AreEqual((short)33, FlatDamageOf(classAction));
    }

    [TestMethod]
    public void AnAppliedClass_GrantsItsActions()
    {
        var fixture = Build();
        fixture.SpawnedAs(CreatureId);
        Assert.IsFalse(fixture.Actions.Has(EntityId, OtherClassActionId));

        fixture.Applied(OtherClassId);

        Assert.IsTrue(fixture.Actions.Has(EntityId, OtherClassActionId));
    }

    /// <summary>A class in a slot that no applied part put there grants nothing: runtime classes arrive through Apply, which records them.</summary>
    [TestMethod]
    public void AClassSlotAlone_GrantsNothing()
    {
        var fixture = Build();
        fixture.SpawnedAs(CreatureId);

        fixture.Components.Merge(EntityId, new ClassSlotsComponent(fixture.Blueprints.GetId(OtherClassId)));

        Assert.IsFalse(fixture.Actions.Has(EntityId, OtherClassActionId));
    }

    /// <summary>An applied part is later than everything the entity spawned with, so its grant replaces its blueprint's -- composition's own rule.</summary>
    [TestMethod]
    public void AnAppliedGrant_OverridesTheBlueprints()
    {
        var fixture = Build();
        var fury = new BlueprintDefinition(Guid.NewGuid(), "Fury") { Actions = [new ActionGrant(RacialActionId, ActionWithDamage(RacialActionId, "Furious Attack", 44, cooldownFrames: 40))] };
        fixture.Blueprints.Register(fury);
        fixture.SpawnedAs(ChampionId);

        fixture.Applied(fury.Id);

        Assert.IsTrue(fixture.Actions.TryGetEffectiveAction(EntityId, RacialActionId, out var racial));
        Assert.AreEqual((short)44, FlatDamageOf(racial));
    }

    [TestMethod]
    public void TheMostRecentlyAppliedGrant_Wins()
    {
        var fixture = Build();
        var first = new BlueprintDefinition(Guid.NewGuid(), "First") { Actions = [new ActionGrant(TraitActionId, ActionWithDamage(TraitActionId, "First Trick", 5))] };
        var second = new BlueprintDefinition(Guid.NewGuid(), "Second") { Actions = [new ActionGrant(TraitActionId, ActionWithDamage(TraitActionId, "Second Trick", 6))] };
        fixture.Blueprints.Register(first);
        fixture.Blueprints.Register(second);
        fixture.SpawnedAs(CreatureId);

        fixture.Applied(first.Id);
        fixture.Applied(second.Id);

        Assert.IsTrue(fixture.Actions.TryGetEffectiveAction(EntityId, TraitActionId, out var trick));
        Assert.AreEqual((short)6, FlatDamageOf(trick));
    }

    [TestMethod]
    public void AGrantTheEntityHoldsItself_WinsOverItsBlueprints()
    {
        var fixture = Build();
        fixture.SpawnedAs(CreatureId);
        fixture.Components.Merge(EntityId, new ActionInstanceComponent(RacialActionId, ActionWithDamage(RacialActionId, "Racial Attack", 99, cooldownFrames: 40)));

        Assert.IsTrue(fixture.Actions.TryGetEffectiveAction(EntityId, RacialActionId, out var racial));
        Assert.AreEqual((short)99, FlatDamageOf(racial));
    }

    [TestMethod]
    public void NoEntityHasAnUngrantedAction()
    {
        var fixture = Build();

        Assert.IsFalse(fixture.Actions.Has(EntityId, RacialActionId), "No blueprint, no racial action.");
        Assert.IsFalse(fixture.Actions.TryGetEffectiveAction(EntityId, RacialActionId, out _));
    }

    [TestMethod]
    public void Cooldown_IsHeldPerEntity_ForADefinitionGrantedAction()
    {
        var fixture = Build();
        fixture.SpawnedAs(CreatureId);

        Assert.IsFalse(fixture.Actions.IsOnCooldown(EntityId, RacialActionId, now: 100), "Never used, never on cooldown.");

        fixture.Actions.SetCooldown(EntityId, RacialActionId, cooldownFrames: 40, now: 100);

        Assert.IsTrue(fixture.Actions.IsOnCooldown(EntityId, RacialActionId, now: 100));
        Assert.AreEqual(40, fixture.Actions.CooldownFramesRemaining(EntityId, RacialActionId, now: 100));
        Assert.AreEqual(1, fixture.Actions.CooldownFramesRemaining(EntityId, RacialActionId, now: 139));
        Assert.IsFalse(fixture.Actions.IsOnCooldown(EntityId, RacialActionId, now: 140));
        Assert.AreEqual(1, fixture.Components.GetMultiPool<ActionCooldownComponent>().CountForEntity(EntityId), "Using it again re-arms the same entry.");

        fixture.Actions.SetCooldown(EntityId, RacialActionId, cooldownFrames: 40, now: 200);
        Assert.AreEqual(1, fixture.Components.GetMultiPool<ActionCooldownComponent>().CountForEntity(EntityId));
        Assert.AreEqual(40, fixture.Actions.CooldownFramesRemaining(EntityId, RacialActionId, now: 200));
    }

    [TestMethod]
    public void Cooldowns_AreKeptApartPerAction()
    {
        var fixture = Build();
        fixture.SpawnedAs(CreatureId);

        fixture.Actions.SetCooldown(EntityId, RacialActionId, cooldownFrames: 40, now: 0);

        Assert.IsTrue(fixture.Actions.IsOnCooldown(EntityId, RacialActionId, now: 0));
        Assert.IsFalse(fixture.Actions.IsOnCooldown(EntityId, ClassActionId, now: 0));
    }
}
