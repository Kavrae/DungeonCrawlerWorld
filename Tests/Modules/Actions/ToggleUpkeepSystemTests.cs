using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.ECS.Entities;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.StatModifiers.Components;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Actions.Systems;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.BodyPartEffects.Components;
using Game.Modules.Core.Components;
using Game.Modules.Death.Components;
using Game.Modules.Mana.Components;
using Game.World;
using Microsoft.Xna.Framework;

namespace Tests.Modules.Actions;

/// <summary>A toggle's activation and periodic effects: asked before they are applied, all or nothing, each toggle on its own timer.</summary>
[TestClass]
public sealed class ToggleUpkeepSystemTests
{
    private const int HolderEntityId = 1;
    private const ushort IntervalFrames = 60;

    private static readonly Vector3Int HolderPosition = new(5, 5, 0);

    private sealed class DefinitionOwner(Toggles toggles) : IToggleOwner
    {
        public Dictionary<Guid, ActivatableDefinition> Definitions { get; } = [];

        public bool EndsWithHolder { get; set; } = true;

        public bool TryResolveDefinition(int holderEntityId, in ActiveToggleComponent toggle, out ActivatableDefinition definition) =>
            Definitions.TryGetValue(toggle.Owner.ActionId, out definition!);

        public void SwitchOff(int holderEntityId, in ActiveToggleComponent toggle, ActivatableDefinition definition, long now) =>
            toggles.TurnOff(holderEntityId, toggle.Key, definition, now);

        public bool EndsWhenHolderDies(ActivatableDefinition definition) => EndsWithHolder;
    }

    private sealed class Harness
    {
        public required ComponentManager Components { get; init; }
        public required Toggles Toggles { get; init; }
        public required EffectServices Services { get; init; }
        public required DefinitionOwner Owner { get; init; }
        public required ToggleUpkeepSystem System { get; init; }
        public required SimulationScope Scope { get; init; }
        public required SimulationClock Clock { get; init; }
        public required EventBus EventBus { get; init; }
        public HashSet<int> FrozenEntityIds { get; } = [];
        public List<ToggleEndedEvent> Ended { get; } = [];

        public float Mana => Components.GetPackedPool<ManaComponent>().GetReadonly(HolderEntityId).CurrentMana;

        public void SetMana(float mana)
        {
            var pool = Components.GetPackedPool<ManaComponent>();
            pool.Remove(HolderEntityId);
            pool.Add(HolderEntityId, new ManaComponent(mana, 100));
        }

        public int ActiveToggleCount => Components.GetMultiPool<ActiveToggleComponent>().CountForEntity(HolderEntityId);

        public int SourceCount => Components.GetMultiPool<AuraSourceComponent>().CountForEntity(HolderEntityId);

        public uint TurnOn(ActionDefinition definition, long now)
        {
            Owner.Definitions[definition.Id] = definition;
            return Toggles.TurnOn(HolderEntityId, ActivatableReference.Action(definition.Id), definition, now);
        }

        public void RunThrough(long fromFrame, long toFrame)
        {
            for (var frame = fromFrame; frame <= toFrame; frame++)
            {
                Clock.Advance(frame);
                System.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: frame), 0);
            }
        }
    }

    private static Harness Build()
    {
        var components = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 20, initialComponentCapacity: 20));
        var eventBus = new EventBus();
        var world = TestWorlds.Over(new Map(new Vector3Int(20, 20, 1)), components, eventBus);
        TestTransforms.Set(components, HolderEntityId, new TransformComponent(HolderPosition, new Vector2Byte(1, 1)));

        var services = TestActionEffects.Services(components, new EntityKeys(), eventBus, new MathUtility(),
            deadEntities: components.GetPackedPool<DeadComponent>(),
            mana: components.GetPackedPool<ManaComponent>(),
            auraSources: components.GetMultiPool<AuraSourceComponent>());
        var toggles = new Toggles(components.GetMultiPool<ActiveToggleComponent>(), services);
        var owner = new DefinitionOwner(toggles);
        toggles.RegisterOwner(ActivatableKind.Action, owner);

        var clock = new SimulationClock();
        Harness? harness = null;
        var scope = new SimulationScope(entityId => !harness!.FrozenEntityIds.Contains(entityId));
        var system = new ToggleUpkeepSystem(
            components.GetMultiPool<ActiveToggleComponent>(),
            toggles,
            components.GetPackedPool<DeadComponent>(),
            components.GetDirectPool<TransformComponent>(),
            world,
            eventBus,
            clock,
            scope);

        harness = new Harness { Components = components, Toggles = toggles, Services = services, Owner = owner, System = system, Scope = scope, Clock = clock, EventBus = eventBus };
        eventBus.Subscribe<ToggleEndedEvent>(harness.Ended.Add);
        harness.SetMana(10);
        return harness;
    }

    private static ActionDefinition Toggle(IReadOnlyList<Effect>? activationEffects = null, IReadOnlyList<Effect>? periodicEffects = null, ushort intervalFrames = IntervalFrames) =>
        new(Guid.NewGuid(), "Test Toggle", null, "t", Color.White, [],
            Effects: [new Effect([new AuraSourceGrant(TestAuras.PoisonGlowOnly, Power: 8, Size: 3)])],
            Activator: new DirectAction(new TargetingSpec(TargetShape.Self, Range: 0), new ActionTiming(ActionTimingCategory.FreeCast)),
            Toggle: new ToggleSpec(periodicEffects is null ? null : new TogglePeriodicEffects(periodicEffects, intervalFrames)))
        {
            ActivationEffects = activationEffects ?? [],
        };

    private static Effect Drain(ushort amount) => new([new ManaDrain(amount)]);

    [TestMethod]
    public void PeriodicEffects_LandEveryInterval_TheFirstOneIntervalAfterTurningOn()
    {
        var harness = Build();
        harness.TurnOn(Toggle(periodicEffects: [Drain(1)]), now: 0);

        harness.RunThrough(1, IntervalFrames - 1);
        Assert.AreEqual(10f, harness.Mana, "Turning on takes nothing, and nothing is due before the first interval.");

        harness.RunThrough(IntervalFrames, IntervalFrames);
        Assert.AreEqual(9f, harness.Mana);

        harness.RunThrough(IntervalFrames + 1, IntervalFrames * 3);
        Assert.AreEqual(7f, harness.Mana);
        Assert.AreEqual(1, harness.ActiveToggleCount);
    }

    [TestMethod]
    public void PeriodicEffectsThatCannotApply_SwitchTheToggleOff_WithItsGrantGoneAndNothingApplied()
    {
        var harness = Build();
        harness.SetMana(0.5f);
        harness.TurnOn(Toggle(periodicEffects: [Drain(1)]), now: 0);
        Assert.AreEqual(1, harness.SourceCount);

        harness.RunThrough(1, IntervalFrames - 1);
        Assert.AreEqual(1, harness.ActiveToggleCount, "On for the whole first interval.");

        harness.RunThrough(IntervalFrames, IntervalFrames);

        Assert.AreEqual(0, harness.ActiveToggleCount);
        Assert.AreEqual(0, harness.SourceCount);
        Assert.AreEqual(0.5f, harness.Mana);
        Assert.HasCount(1, harness.Ended);
        Assert.AreEqual(ToggleEndReason.PeriodicEffectsRefused, harness.Ended[0].Reason);
    }

    [TestMethod]
    public void PeriodicEffects_AreAllOrNothing()
    {
        var harness = Build();
        harness.SetMana(3);
        harness.TurnOn(Toggle(periodicEffects: [Drain(2), Drain(5)]), now: 0);

        harness.RunThrough(1, IntervalFrames);

        Assert.AreEqual(3f, harness.Mana, "The first drain could be paid, the second couldn't: neither is taken.");
        Assert.AreEqual(0, harness.ActiveToggleCount);
    }

    [TestMethod]
    public void TwoTogglesOnOneEntity_TickSeparately_AndEachEndsOnItsOwn()
    {
        var harness = Build();
        harness.SetMana(2);
        harness.TurnOn(Toggle(periodicEffects: [Drain(1)]), now: 0);
        harness.TurnOn(Toggle(periodicEffects: [Drain(1)], intervalFrames: 90), now: 0);

        harness.RunThrough(1, 60);
        Assert.AreEqual(1f, harness.Mana);

        harness.RunThrough(61, 90);
        Assert.AreEqual(0f, harness.Mana);
        Assert.AreEqual(2, harness.ActiveToggleCount);

        harness.RunThrough(91, 120);
        Assert.AreEqual(1, harness.ActiveToggleCount, "The first toggle's second tick can't be paid; the other isn't due until 180.");
        Assert.AreEqual(1, harness.SourceCount);
    }

    [TestMethod]
    public void FrozenHolder_IsNotTicked_AndOwesNothingWhenItResumes()
    {
        var harness = Build();
        harness.TurnOn(Toggle(periodicEffects: [Drain(1)]), now: 0);
        harness.FrozenEntityIds.Add(HolderEntityId);

        harness.RunThrough(1, IntervalFrames * 5 + 10);
        Assert.AreEqual(10f, harness.Mana);

        harness.FrozenEntityIds.Clear();
        harness.Scope.RaiseResumed(HolderEntityId);
        harness.RunThrough(IntervalFrames * 5 + 11, IntervalFrames * 6 - 1);
        Assert.AreEqual(10f, harness.Mana, "None of the intervals spent frozen is owed.");

        harness.RunThrough(IntervalFrames * 6, IntervalFrames * 6);
        Assert.AreEqual(9f, harness.Mana, "The cadence it had before freezing is kept.");
    }

    [TestMethod]
    public void HolderOffTheMap_IsNotTicked_AndStaysOn()
    {
        var harness = Build();
        harness.SetMana(0);
        TestTransforms.Set(harness.Components, HolderEntityId, new TransformComponent(TransformComponent.UnplacedOn(MapLayer.Ground), new Vector2Byte(1, 1)));
        harness.TurnOn(Toggle(periodicEffects: [Drain(1)]), now: 0);

        harness.RunThrough(1, IntervalFrames * 3);

        Assert.AreEqual(1, harness.ActiveToggleCount);
        Assert.IsEmpty(harness.Ended);
    }

    [TestMethod]
    public void HolderDies_TogglesThatEndWithTheirHolderAreSwitchedOffAtOnce_AndOthersStay()
    {
        var harness = Build();
        harness.TurnOn(Toggle(), now: 0);
        harness.Components.Merge(HolderEntityId, new DeadComponent(TestSources.Entity(2), DiedAtFrame: 0));

        Assert.AreEqual(0, harness.ActiveToggleCount);
        Assert.AreEqual(0, harness.SourceCount);
        Assert.AreEqual(ToggleEndReason.HolderDied, harness.Ended.Single().Reason);

        var staying = Build();
        staying.Owner.EndsWithHolder = false;
        staying.TurnOn(Toggle(), now: 0);
        staying.Components.Merge(HolderEntityId, new DeadComponent(TestSources.Entity(2), DiedAtFrame: 0));

        Assert.AreEqual(1, staying.ActiveToggleCount);
        Assert.AreEqual(1, staying.SourceCount);
    }

    [TestMethod]
    public void ToggleWithPeriodicEffectsTurnedOnForADeadHolder_IsSwitchedOffByItsFirstTick()
    {
        var harness = Build();
        harness.Components.Merge(HolderEntityId, new DeadComponent(TestSources.Entity(2), DiedAtFrame: 0));
        harness.TurnOn(Toggle(periodicEffects: [Drain(1)]), now: 5);
        Assert.AreEqual(1, harness.ActiveToggleCount);

        harness.RunThrough(5, 6);

        Assert.AreEqual(0, harness.ActiveToggleCount);
        Assert.AreEqual(10f, harness.Mana);
    }

    [TestMethod]
    public void ManaDrain_IsRefusedExactlyWhenApplyWouldNotTakeTheFullAmount()
    {
        var harness = Build();
        var drain = new ManaDrain(3);
        var definition = Toggle(activationEffects: [new Effect([drain])]);

        harness.SetMana(3);
        Assert.AreEqual(EffectRefusal.None, ActivationEffectsApplier.GetRefusal(harness.Services, HolderEntityId, definition, now: 0));
        ActivationEffectsApplier.Apply(harness.Services, HolderEntityId, definition, now: 0);
        Assert.AreEqual(0f, harness.Mana);

        harness.SetMana(2.9f);
        Assert.AreEqual(EffectRefusal.NotEnoughMana, ActivationEffectsApplier.GetRefusal(harness.Services, HolderEntityId, definition, now: 0));

        harness.Components.GetPackedPool<ManaComponent>().Remove(HolderEntityId);
        Assert.AreEqual(EffectRefusal.NoManaPool, ActivationEffectsApplier.GetRefusal(harness.Services, HolderEntityId, definition, now: 0), "A target with no mana pool can't supply any.");
    }

    [TestMethod]
    public void TurnOnBlocker_HoldsOnlyWhileOff_AndOnlyWhenAnActivationEffectCannotApply()
    {
        var harness = Build();
        var mana = harness.Components.GetPackedPool<ManaComponent>();
        var meleeDisabled = harness.Components.GetPackedPool<MeleeDisabledComponent>();
        var definition = Toggle(activationEffects: [Drain(2), Drain(5)]);

        ActivationBlocker Blocker(bool isToggledOn) =>
            ActivationQueries.GetBlocker(HolderEntityId, definition, definition.Activator, isToggledOn, meleeDisabled, harness.Services, now: 0);

        harness.SetMana(4);
        Assert.AreEqual(ActivationBlocker.NotEnoughMana, Blocker(isToggledOn: false), "The second activation effect can't be applied, so the turn-on is blocked and neither is.");
        Assert.AreEqual(4f, harness.Mana);
        Assert.AreEqual(ActivationBlocker.None, Blocker(isToggledOn: true), "Turning off takes nothing.");

        harness.SetMana(6);
        Assert.AreEqual(ActivationBlocker.NotEnoughMana, Blocker(isToggledOn: false), "Each is affordable alone, but turning on takes both.");

        harness.SetMana(7);
        Assert.AreEqual(ActivationBlocker.None, Blocker(isToggledOn: false));
    }
}
