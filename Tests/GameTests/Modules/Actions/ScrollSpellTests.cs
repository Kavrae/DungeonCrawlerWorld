using Engine.Bootstrap;
using Engine.Content;
using Engine.Math;
using Engine.Tiles;
using Game;
using Game.Blueprints.Races;
using Game.Bootstrap;
using Game.Content;
using Game.Modules.AbilityScores;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Movement.Components;
using Game.Spawning;
using Game.Tags;
using Game.Views;
using Game.World;

namespace Tests.GameTests.Modules.Actions;

/// <summary>A scroll casts its spell as the spell is -- its effects, targeting and timing, without its costs or cooldown -- resolved through the content like any reference, and scales with its reader's Intelligence only because a spell does.</summary>
[TestClass]
public sealed class ScrollSpellTests
{
    private const int Ground = (int)MapLayer.Ground;

    private static readonly ContentTypeRegistry Types = EcsBuilder.Declare(GameBootstrapper.BuiltInModules()).ContentTypes;

    private static string ItemLine(ItemDefinition item) => ContentTable.WriteLine(item, Types);

    private static ContentLayer Layer(string name, ContentLayerKind kind, string fileName, string tableName, params string[] lines) =>
        ContentLayer.FromText(name, kind, new Dictionary<string, string>
        {
            [fileName] = ContentTable.Header(tableName) + "\n" + string.Join('\n', lines) + "\n",
        });

    private static GameBuildPassResult BuildById(ContentLayers layers) =>
        GameBuildPass.Run(GameBootstrapper.BuiltInModules(), [], new Map(new MapBounds(0, 0, 40, 40, 3)), layers, sessionSeed: 1, [], initialEntityCapacity: 5000, initialComponentCapacity: 5000, new UniqueNumberAllocator(1, 1, 24), loadsContentById: true);

    [TestMethod]
    public void AScroll_IsItsSpellsEffectsTargetingAndTiming_WithoutItsCostsOrCooldown_AndCarriesItsTags()
    {
        var content = GameContent.Open(TestContent.Layers, Types);
        Assert.IsTrue(content.TryLoadAction(BuiltInActionIds.Heal, out var heal));
        Assert.IsTrue(content.TryLoadItem(BuiltInItemIds.ScrollOfHealing, out var scroll));

        var activator = (ScrollActivator)scroll.Activator!;
        Assert.AreSame(heal, activator.Spell);
        Assert.AreSame(heal.Effects, scroll.Effects);
        Assert.AreEqual(heal.Activator.Targeting, activator.Targeting);
        Assert.AreEqual(heal.Activator.Timing.Category, activator.Timing.Category);
        Assert.IsNull(activator.Timing.CooldownFrames);
        Assert.IsFalse(activator.Timing.CooldownMatchesActionLock);
        Assert.IsNotEmpty(heal.ActivationEffects, "Precondition: Heal costs mana.");
        Assert.IsEmpty(scroll.ActivationEffects);
        Assert.IsTrue(scroll.Tags.HasExact(GameTags.Spell));
        Assert.IsTrue(scroll.Tags.HasExact(GameTags.ItemConsumableScroll));
        foreach (var tag in heal.Tags)
        {
            Assert.IsTrue(scroll.Tags.HasExact(tag), $"Heal's {tag.Name} is the scroll's too: {scroll.Tags}.");
        }
    }

    [TestMethod]
    public void AScrollTakesTheSpellsTiming_WithoutTheSpellsCooldown()
    {
        var spell = TestActivatables.Spell(new TargetingSpec(TargetShape.Self, MaximumRange: 0, Anchor: TargetAnchor.Caster), new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 45, CooldownFrames: 120));

        var scroll = TestActivatables.Scroll(spell);

        Assert.AreEqual(new ActionTiming(ActionTimingCategory.Delayed, ActionLockFrames: 45), scroll.Timing);
    }

    [TestMethod]
    public void AScrollRecord_WritesOnlyItsSpell_AndReadsBackToTheSameBytes()
    {
        var content = GameContent.Open(TestContent.Layers, Types);
        Assert.IsTrue(content.TryLoadItem(BuiltInItemIds.ScrollOfTorch, out var scroll));

        var line = ItemLine(scroll);

        Assert.DoesNotContain("\"fx\"", line);
        Assert.DoesNotContain("\"tg\"", line);
        StringAssert.Contains(line, $"\"ac\":{{\"$\":\"Scroll\",\"sp\":\"{ContentGuid.Format(BuiltInActionIds.Torch)}\"}}");
    }

    [TestMethod]
    public void AScrollHoldingEffectsOfItsOwn_FailsToLoad()
    {
        var withEffects = ItemLine(TestContent.Item(BuiltInItemIds.ScrollOfHealing)).Replace("\"ac\":", "\"fx\":[{\"e\":[{\"$\":\"DirectHeal\",\"pct\":0.5}]}],\"ac\":");
        var content = GameContent.OpenCollectingFailures(TestContent.Layers.With(Layer("Mod", ContentLayerKind.Mod, GameContent.ItemsFileName, GameContent.ItemsTable, withEffects)), Types);

        content.TryLoadItem(BuiltInItemIds.ScrollOfHealing, out _);

        StringAssert.Contains(Assert.ContainsSingle(content.Failures).Reason, "A scroll's effects are its spell's");
    }

    [TestMethod]
    public void AScrollNamingNoSpell_FailsToLoad()
    {
        var unknown = ItemLine(TestContent.Item(BuiltInItemIds.ScrollOfHealing)).Replace(ContentGuid.Format(BuiltInActionIds.Heal), ContentGuid.Format(Guid.NewGuid()));
        var content = GameContent.OpenCollectingFailures(TestContent.Layers.With(Layer("Mod", ContentLayerKind.Mod, GameContent.ItemsFileName, GameContent.ItemsTable, unknown)), Types);

        content.TryLoadItem(BuiltInItemIds.ScrollOfHealing, out _);

        StringAssert.Contains(Assert.ContainsSingle(content.Failures).Reason, "which no ActionDefinition has");
    }

    [TestMethod]
    public void AScrollNamingADisabledSpell_CastsItsReplacement_AndKeepsTheGuidItNames()
    {
        var disabledHeal = ContentTable.WriteDisabled(TestContent.Action(BuiltInActionIds.Heal), BuiltInActionIds.MagicMissile, Types);
        var content = GameContent.Open(TestContent.Layers.With(Layer("Mod", ContentLayerKind.Mod, GameContent.ActionsFileName, GameContent.ActionsTable, disabledHeal)), Types);

        Assert.IsTrue(content.TryLoadItem(BuiltInItemIds.ScrollOfHealing, out var scroll));

        var activator = (ScrollActivator)scroll.Activator!;
        Assert.AreEqual(BuiltInActionIds.Heal, activator.SpellId);
        Assert.AreEqual(BuiltInActionIds.MagicMissile, activator.Spell.Id);
        StringAssert.Contains(ItemLine(scroll), ContentGuid.Format(BuiltInActionIds.Heal));
    }

    [TestMethod]
    public void AScrollsSpellOverriddenInASaveThatFailsItsChecks_IsTheRecordBeneath()
    {
        var heal = TestContent.Action(BuiltInActionIds.Heal);
        var broken = heal with
        {
            Name = "Broken Heal",
            Toggle = ToggleSpec.HoldsEffectsOnly,
            Activator = new SpellActivator(new TargetingSpec(TargetShape.SingleTarget, MaximumRange: 4), heal.Activator.Timing),
        };
        var save = Layer("Save Test", ContentLayerKind.Save, GameContent.ActionsFileName, GameContent.ActionsTable, ContentTable.WriteLine(broken, Types));

        var pass = BuildById(TestContent.Layers.With(save));

        var scroll = pass.Context.Items.Get(BuiltInItemIds.ScrollOfHealing);
        Assert.AreEqual("Heal", ((ScrollActivator)scroll.Activator!).Spell.Name);
        Assert.AreSame(pass.Context.Actions.Get(BuiltInActionIds.Heal), ((ScrollActivator)scroll.Activator!).Spell);
    }

    [TestMethod]
    public void RegisteringAScroll_RegistersItsSpell()
    {
        var pass = BuildById(TestContent.Layers);

        pass.Context.Items.Get(BuiltInItemIds.ScrollOfTorch);

        Assert.IsTrue(pass.Context.Actions.Definitions.Any(action => action.Id == BuiltInActionIds.Torch));
    }

    [TestMethod]
    public void AScrollOfAToggle_OrOfSomethingThatIsntASpell_FailsItsChecks()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1)));
        var context = build.Context;
        var checks = new ContentChecks(context.GameplayTags, context.Auras, context.Terrain, context.StatusEffectAppliers);
        var toggleSpell = TestActivatables.Spell(new TargetingSpec(TargetShape.Self, MaximumRange: 0, Anchor: TargetAnchor.Caster)) with { Toggle = ToggleSpec.HoldsEffectsOnly };
        var scrollOfToggle = TestContent.Item(BuiltInItemIds.ScrollOfHealing) with { Activator = TestActivatables.Scroll(toggleSpell) };
        var scrollOfAbility = TestContent.Item(BuiltInItemIds.ScrollOfHealing) with { Activator = TestActivatables.Scroll(TestContent.Action(BuiltInActionIds.QuickAttack)) };

        Assert.IsTrue(checks.ProblemsOf(scrollOfToggle).Any(problem => problem.Contains("a toggle", StringComparison.Ordinal)));
        Assert.IsTrue(checks.ProblemsOf(scrollOfAbility).Any(problem => problem.Contains("isn't a spell", StringComparison.Ordinal)));
        Assert.IsEmpty(checks.ProblemsOf(TestContent.Item(BuiltInItemIds.ScrollOfHealing)));
    }

    [TestMethod]
    public void AtIntelligence300_TorchCastAndReadFromItsScroll_ReachTheSameScaledRange_AndAWandDoesnt()
    {
        var build = BuiltInTestModules.Build(new Map(new Vector3Int(20, 20, 3)), sessionSeed: 1, initialEntityCapacity: 100, initialComponentCapacity: 100);
        build.Context.ProcessingTierResolver.SetReferencePosition(new Vector3Int(5, 5, Ground));
        var casterId = build.Factory.Spawn(SpawnRequest.At(build.Context.Definitions.GetId(Goblin.Id), new Vector3Int(5, 5, Ground)) with { Seed = 1 });
        build.EcsContext.ComponentManager.GetPackedPool<MovementComponent>().Remove(casterId);
        AbilityScoreEffects.Grant(build.EcsContext.ComponentManager, casterId, AbilityScoreType.Intelligence, baseValue: 300);
        var previews = new ActivationPreviews(build.Context.EffectServices, build.Context.SimulationClock);
        var torch = TestContent.Action(BuiltInActionIds.Torch);

        var cast = previews.Preview(casterId, torch);
        var read = previews.Preview(casterId, TestContent.Item(BuiltInItemIds.ScrollOfTorch));
        var wand = previews.Preview(casterId, TestContent.Item(BuiltInItemIds.WandOfFireball));

        Assert.AreEqual(6, torch.Activator.Targeting.MaximumRange, "Precondition: Torch's declared range.");
        Assert.AreEqual(24, cast.Range!.Value);
        Assert.AreEqual(24, read.Range!.Value);
        Assert.IsNull(wand.Range, "A wand isn't a spell: its range is as declared.");
        Assert.AreEqual(4f, SpellScalingEffects.MultiplierFor(build.Context.EffectServices.AbilityScores, casterId, torch.Activator));
        Assert.AreEqual(1f, SpellScalingEffects.MultiplierFor(build.Context.EffectServices.AbilityScores, casterId, TestContent.Item(BuiltInItemIds.WandOfFireball).Activator));
    }
}
