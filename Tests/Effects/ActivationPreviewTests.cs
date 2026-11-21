using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Engine.Tags;
using Engine.Tiles;
using Game.Blueprints.NPCs.Generic;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Actions;
using Game.Modules.Auras;
using Game.Modules.Auras.Components;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Inventory;
using Game.Modules.Movement.Components;
using Game.Modules.Poison.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Spawning;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.UI;

namespace Tests.Effects;

/// <summary>An action's, item's or aura's amounts as previewed are what applying the same entry does, with the user as the source and a target that has no modifiers.</summary>
[TestClass]
public sealed class ActivationPreviewTests
{
    private const int Ground = (int)MapLayer.Ground;

    private sealed record Session(GameBuildPassResult Build, int UserId, int TargetId)
    {
        public ComponentManager Components => Build.EcsContext.ComponentManager;

        public EffectServices Services => Build.Context.EffectServices;

        public ActivationPreviews Previews => new(Services, Build.Context.SimulationClock);

        public void Modify(int entityId, StatModifierTarget target, StatModifierOperation operation, float magnitude) =>
            StatModifierEffects.Apply(Components, entityId, target, operation, StatModifierPolarity.Buff, canModify: true, magnitude, FrameDeadline.Never, ActionSource.Admin);

        public EffectContext UserContext(ActivatableDefinition definition, int targetId) =>
            EffectContext.FromEntity(Services, UserId, targetId, definition, Build.Context.SimulationClock.CurrentFrame);

        public EffectContext AuraTickContext(AuraDefinition aura, float power, int targetId) =>
            new(Services, ActionSource.Admin, SourceEntityId: null, targetId, aura.EffectName, aura.Tags, Build.Context.SimulationClock.CurrentFrame)
            {
                Magnitude = aura.MagnitudeAt(power),
            };

        public float HealthOf(int entityId) =>
            HealthQueries.TryGetTotals(Services.Health, Services.BodyParts, Services.StatModifiers, entityId, out var current, out _) ? current : 0f;

        public void Wound(int entityId, short damage) =>
            new DirectDamage(damage, damage).Apply(new EffectContext(Services, ActionSource.Admin, SourceEntityId: null, entityId, "Test", default, Build.Context.SimulationClock.CurrentFrame));
    }

    private static Session BuildSession()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)), sessionSeed: 1, initialEntityCapacity: 100, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(new Vector3Int(5, 5, Ground));
        int Spawn(Guid blueprintId, Vector3Int position)
        {
            var entityId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(blueprintId), position) with { Seed = 1 });
            build.EcsContext.ComponentManager.GetPackedPool<MovementComponent>().Remove(entityId);
            return entityId;
        }

        return new Session(build, Spawn(Goblin.Id, new Vector3Int(5, 5, Ground)), Spawn(TestDummyBlueprint.Id, new Vector3Int(7, 5, Ground)));
    }

    private static ActionDefinition ActionWith(params IEffectEntry[] entries) =>
        TestActivatables.Action() with { Effects = [new Effect(entries)] };

    [TestMethod]
    public void Damage_AsPreviewed_IsWhatLandsOnATargetWithNoModifiers()
    {
        var session = BuildSession();
        session.Modify(session.UserId, StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, 1f);
        session.Modify(session.UserId, StatModifierTarget.CritChance, StatModifierOperation.Additive, -1f);
        var damage = new DirectDamage(7, 7);
        var action = ActionWith(damage);

        var preview = session.Previews.Preview(session.UserId, action).Of(damage)!;
        var healthBefore = session.HealthOf(session.TargetId);
        damage.Apply(session.UserContext(action, session.TargetId));

        Assert.IsTrue(preview.TryGet(EffectAmount.DamageMaximum, out var maximum));
        Assert.AreEqual(14f, maximum.Value, 0.001f);
        Assert.AreEqual(7f, maximum.Base);
        Assert.AreEqual(healthBefore - session.HealthOf(session.TargetId), maximum.Value, 0.001f);
    }

    [TestMethod]
    public void AnAdvancedLine_ShowsItsChangeAndBreakdown_AndASimpleOneShowsOnlyTheValue()
    {
        var session = BuildSession();
        session.Modify(session.UserId, StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, 1f);
        var action = ActionWith(new DirectDamage(7, 7));
        var preview = session.Previews.Preview(session.UserId, action);

        var advanced = ItemComparisonStatExtraction.ExtractEffectStats(action, EffectTextNames.None, DetailsMode.Advanced, preview).Single();
        var simple = ItemComparisonStatExtraction.ExtractEffectStats(action, EffectTextNames.None, DetailsMode.Simple, preview).Single();

        Assert.AreEqual("Deals 14 damage to a random body part", advanced.DisplayText);
        Assert.AreEqual("(+7)", advanced.ChangeText);
        Assert.IsTrue(advanced.ChangeIsBuff);
        Assert.AreEqual("Base: 7\nModifier: x2\nTotal: 14", advanced.TooltipText);
        Assert.AreEqual("Deals 14 damage", simple.DisplayText);
        Assert.IsNull(simple.ChangeText);
        Assert.IsNull(simple.TooltipText);
    }

    [TestMethod]
    public void AModifierThatCounts_IsListed_WithItsSource()
    {
        var session = BuildSession();
        session.Modify(session.UserId, StatModifierTarget.OutgoingAuraPower, StatModifierOperation.Multiplicative, 1f);
        var grant = (AuraSourceGrant)TestContent.Item(BuiltInItemIds.ToxicIdol).Effects[0].Entries[0];

        var preview = session.Previews.Preview(session.UserId, ActionWith(grant)).Of(grant)!;

        Assert.IsTrue(preview.TryGet(EffectAmount.AuraPower, out var power));
        var contribution = power.Contributions.Single();
        Assert.AreEqual(ValueContributionKind.Modifier, contribution.Kind);
        Assert.AreEqual(StatModifierOperation.Multiplicative, contribution.Operation);
        Assert.AreEqual(1f, contribution.Amount);
        Assert.AreEqual(ActionSource.Admin, contribution.Source);
        Assert.IsTrue(preview.TryGet(EffectAmount.AuraSize, out var size));
        Assert.IsFalse(size.IsModified);
    }

    [TestMethod]
    public void AuraPower_AsPreviewed_IsThePowerPlaced()
    {
        var session = BuildSession();
        session.Modify(session.UserId, StatModifierTarget.OutgoingAuraPower, StatModifierOperation.Multiplicative, 1f);
        var grant = (AuraSourceGrant)TestContent.Item(BuiltInItemIds.ToxicIdol).Effects[0].Entries[0];
        var action = ActionWith(grant);

        var preview = session.Previews.Preview(session.UserId, action).Of(grant)!;
        grant.Apply(session.UserContext(action, session.TargetId));

        var sources = session.Components.GetMultiPool<AuraSourceComponent>();
        var placedPower = sources.GetReadonlyByDenseIndex(sources.GetFirstDenseIndex(session.TargetId)).Power;
        Assert.AreEqual(grant.Power * 2, placedPower);
        Assert.AreEqual(placedPower, preview.ValueOr(EffectAmount.AuraPower, 0f));
    }

    [TestMethod]
    public void AScrollsRange_AsPreviewed_IsTheRangeItResolvesWith()
    {
        var session = BuildSession();
        var scroll = TestContent.Item(BuiltInItemIds.ScrollOfTorch);

        var preview = session.Previews.Preview(session.UserId, scroll);

        var resolved = session.Build.Context.TargetResolution.EffectiveSpec(session.UserId, scroll.Activator!);
        Assert.AreEqual(resolved.AimRanges.MaximumRange, preview.Range!.Value);
        Assert.AreEqual(scroll.Activator!.Targeting.AimRanges.MaximumRange, preview.Range.Base);
        Assert.AreEqual(resolved, preview.Targeting);
    }

    [TestMethod]
    public void TheHealingAura_AtItsShrinesStrength_HealsWhatOneTickHeals()
    {
        var session = BuildSession();
        var aura = TestContent.Aura(BuiltInAuraIds.HealingShrine);
        const float shrineStrength = 16f;
        session.Wound(session.TargetId, 30);

        var previews = session.Previews.PreviewAura(aura, shrineStrength);
        var heal = aura.Effects[0].Entries[0];
        var healthBefore = session.HealthOf(session.TargetId);
        EffectSequence.Apply(aura.Effects, session.AuraTickContext(aura, shrineStrength, session.TargetId));

        Assert.AreEqual(session.HealthOf(session.TargetId) - healthBefore, previews[heal].ValueOr(EffectAmount.HealFlat, 0f), 0.001f);
        Assert.AreEqual("Heals 16 health", EffectFormatting.FormatEntry(heal, EffectTextNames.None, DetailsMode.Simple, previews));
        Assert.AreEqual("Heals 1 health", EffectFormatting.FormatEntry(heal, EffectTextNames.None, DetailsMode.Simple));
    }

    [TestMethod]
    public void ThePoisonAura_AtTheToxicIdolsStrength_TopsUpToTheStacksOneTickGives()
    {
        var session = BuildSession();
        var aura = TestContent.Aura(BuiltInAuraIds.ToxicIdolPoison);
        var strength = ((AuraSourceGrant)TestContent.Item(BuiltInItemIds.ToxicIdol).Effects[0].Entries[0]).Power;

        var previews = session.Previews.PreviewAura(aura, strength);
        EffectSequence.Apply(aura.Effects, session.AuraTickContext(aura, strength, session.TargetId));

        var stacks = session.Components.GetPackedPool<PoisonTimerComponent>().GetReadonly(session.TargetId).StackCount;
        Assert.AreEqual(stacks, previews[aura.Effects[0].Entries[0]].ValueOr(EffectAmount.Stacks, 0f));
        Assert.AreEqual(strength, stacks);
    }

    [TestMethod]
    public void AFlatAura_ReadsTheSame_AtAnyStrength()
    {
        var session = BuildSession();
        var dread = TestContent.Aura(BuiltInAuraIds.DreadAura);
        var entry = dread.Effects[0].Entries[0];

        var weak = EffectFormatting.FormatEntry(entry, EffectTextNames.None, DetailsMode.Simple, session.Previews.PreviewAura(dread, 1f));
        var strong = EffectFormatting.FormatEntry(entry, EffectTextNames.None, DetailsMode.Simple, session.Previews.PreviewAura(dread, 10f));

        Assert.AreEqual(weak, strong);
    }

    [TestMethod]
    public void TheChangeStamp_Changes_WhenAModifierLands()
    {
        var session = BuildSession();
        var before = session.Previews.ChangeStampOf(session.UserId);

        session.Modify(session.UserId, StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, 1f);

        Assert.AreNotEqual(before, session.Previews.ChangeStampOf(session.UserId));
    }

    [TestMethod]
    public void APreview_ChangesNothing()
    {
        var session = BuildSession();
        var grant = new StatusEffectGrant(StatusEffectType.Poison, StackCount: 3);

        session.Previews.Preview(session.UserId, ActionWith(grant, new DirectDamage(5, 5)));

        Assert.IsFalse(session.Components.GetPackedPool<PoisonTimerComponent>().Has(session.TargetId));
        Assert.IsFalse(session.Components.GetPackedPool<PoisonTimerComponent>().Has(session.UserId));
    }
}
