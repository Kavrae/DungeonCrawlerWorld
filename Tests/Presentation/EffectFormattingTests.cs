using Engine.Tags;
using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules;
using Game.Modules.Actions.Effects;
using Game.Modules.Auras;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.StatModifiers;
using Game.Modules.StatusEffects;
using Game.Tags;
using Presentation.UI;
using Presentation.UI.Details;

namespace Tests.Presentation;

/// <summary>Every effect entry reads as what it does, in simple and advanced mode, with the names a session's catalogs give.</summary>
[TestClass]
public sealed class EffectFormattingTests
{
    private static readonly Lazy<GameBuildPassResult> Build = new(() => BuiltInTestModules.Build(GameBuildPass.CreatePlaceholderMap()));

    private static GameModuleContext Context => Build.Value.Context;

    private static EffectTextNames Names => new(Context.GameplayTags, Context.Terrain, Context.StatusEffectDisplays);

    private static string Simple(IEffectEntry entry) => EffectFormatting.FormatEntry(entry, Names, DetailsMode.Simple);

    private static string Advanced(IEffectEntry entry) => EffectFormatting.FormatEntry(entry, Names, DetailsMode.Advanced);

    private static IEnumerable<(IEffectEntry Entry, string HeldBy)> EveryBuiltInEntry()
    {
        var entries = new List<(IEffectEntry, string)>();
        foreach (var action in Context.Actions.Definitions)
        {
            EffectEntries.ForEach(action, entry => entries.Add((entry, $"Action '{action.Name}'")));
        }

        foreach (var item in Context.Items.Definitions)
        {
            EffectEntries.ForEach(item, entry => entries.Add((entry, $"Item '{item.Name}'")));
        }

        for (var terrainTypeId = 1; terrainTypeId <= Context.Terrain.Count; terrainTypeId++)
        {
            if (Context.Terrain.TryGet((ushort)terrainTypeId, out var terrain) && terrain.Contact is { } contact)
            {
                EffectEntries.ForEach(contact.Effects, entry => entries.Add((entry, $"Terrain '{terrain.Name}'")));
            }
        }

        for (var auraId = 0; auraId < Context.Auras.Count; auraId++)
        {
            var aura = Context.Auras.Get((byte)auraId);
            EffectEntries.ForEach(aura.Effects, entry => entries.Add((entry, $"Aura '{aura.Name}'")));
        }

        return entries;
    }

    [TestMethod]
    public void NoBuiltInEntry_FormatsAsItsTypeName_InEitherMode()
    {
        var entries = EveryBuiltInEntry().ToList();
        Assert.IsNotEmpty(entries);

        foreach (var (entry, heldBy) in entries)
        {
            var typeName = entry.GetType().Name;
            Assert.AreNotEqual(typeName, Simple(entry), $"{heldBy}: {typeName} has no simple text.");
            Assert.AreNotEqual(typeName, Advanced(entry), $"{heldBy}: {typeName} has no advanced text.");
        }
    }

    [TestMethod]
    public void TheCurePoisonPotion_CuresPoison_AndGrantsAnImmunityForItsDuration()
    {
        var entries = TestContent.Item(BuiltInItemIds.CurePoisonPotion).Effects[0].Entries;

        Assert.AreEqual("Cures Poison", Simple(entries[0]));
        Assert.AreEqual("Poison immunity for 5m", Simple(entries[1]));
    }

    [TestMethod]
    public void AnImmunityWithNoDuration_SaysNone()
    {
        Assert.AreEqual("Poison immunity", Simple(new StatusEffectImmunityGrant(StatusEffectType.Poison)));
    }

    [TestMethod]
    public void TheNullZones_PreventTheirTagsByTheirPlurals_ForTheirDuration_InEitherMode()
    {
        var antiMagic = TestContent.Aura(BuiltInAuraIds.AntiMagic).Effects[0].Entries[0];

        Assert.AreEqual("Prevents spells for 2s", Simple(antiMagic));
        Assert.AreEqual("Prevents spells for 2s", Advanced(antiMagic));

        var scrollsAndTomes = new ActivationDisableGrant(GameplayTagQuery.Any([GameTags.ItemConsumableScroll, GameTags.ItemConsumableTome]), DurationFrames: 90);
        var simple = Simple(scrollsAndTomes);
        Assert.IsTrue(simple is "Prevents scrolls and tomes for 2s" or "Prevents tomes and scrolls for 2s", simple);
    }

    [TestMethod]
    public void ADisableWithoutADeclaredPlural_UsesTheDisplayName_AndNamesWhatItExcepts()
    {
        var disable = new ActivationDisableGrant(new GameplayTagQuery(requireAny: [GameTags.Magic], exclude: [GameTags.ItemWand]), DurationFrames: 600);

        Assert.AreEqual("Prevents magic except wands for 10s", Simple(disable));
    }

    [TestMethod]
    public void ATerrainChange_NamesTheTerrain()
    {
        var ringOfLava = Context.Actions.Definitions.Single(action => action.Name == "Ring of Lava").Effects[0].Entries[0];

        Assert.AreEqual("Turns the ground to Lava", Simple(ringOfLava));
        Assert.AreEqual("Turns the ground to core:unknown", EffectFormatting.FormatEntry(new TerrainChange("core:unknown"), EffectTextNames.None, DetailsMode.Simple));
    }

    [TestMethod]
    public void Dodge_DodgesMostAttacksWithAStep()
    {
        Assert.AreEqual("Dodges most attacks with a step", Simple(new DodgeActivation()));
    }

    [TestMethod]
    public void AStatusGrant_SaysTopUp_AndItsDurationInSeconds_AndAdvancedAddsItsBodyPart()
    {
        var topUp = new StatusEffectGrant(StatusEffectType.Poison, StackCount: 5, Mode: StatusEffectGrantMode.TopUpTo, BodyPart: BodyPartTargeting.Random, DurationTicks: 6);

        Assert.AreEqual("Tops Poison up to 5 stacks for 6s", Simple(topUp));
        Assert.AreEqual("Tops Poison up to 5 stacks on a random body part for 6s", Advanced(topUp));
        Assert.AreEqual("Applies 1 stack of Burning", Advanced(new StatusEffectGrant(StatusEffectType.Burning)));
        Assert.AreEqual("Applies 2 stacks of Poison for 6 ticks", EffectFormatting.FormatEntry(new StatusEffectGrant(StatusEffectType.Poison, 2, DurationTicks: 6), EffectTextNames.None, DetailsMode.Simple));
    }

    [TestMethod]
    public void Damage_ShowsItsPercentage_AndAdvancedAddsWhereItLands()
    {
        Assert.AreEqual("Deals 35-40 damage", Simple(new DirectDamage(35, 40)));
        Assert.AreEqual("Deals 35-40 damage to a random body part", Advanced(new DirectDamage(35, 40)));
        Assert.AreEqual("Deals 5 damage + 10% of max health", Simple(new DirectDamage(5, 5, PercentageDamage: 0.1f)));
        Assert.AreEqual("Deals 10% of max health as damage", Simple(new DirectDamage(0, 0, PercentageDamage: 0.1f)));
        Assert.AreEqual("Deals 3 damage to the foot", Advanced(new DirectDamage(3, 3, BodyPart: BodyPartTargeting.Of(BodyPartType.Foot))));
        Assert.AreEqual("Deals 3 damage split across every body part", Advanced(new DirectDamage(3, 3, BodyPartTargetMode: BodyPartTargetMode.All)));
    }

    [TestMethod]
    public void Healing_AdvancedAddsWhereItLands()
    {
        var heal = new DirectHeal(0.5f);

        Assert.AreEqual("Heals 50% of max health", Simple(heal));
        Assert.AreEqual("Heals 50% of max health split across every body part", Advanced(heal));
        Assert.AreEqual("Heals 50% of max health to the most hurt body part", Advanced(heal with { BodyPartTargetMode = BodyPartTargetMode.LowestPercentage }));
    }

    [TestMethod]
    public void AStatModifier_IsAPlainSentence_ItsDurationLast_InEitherMode()
    {
        var dread = TestContent.Aura(BuiltInAuraIds.DreadAura).Effects[0].Entries[0];

        Assert.AreEqual("x4 Caution for 2s", Simple(dread));
        Assert.AreEqual("x4 Caution for 2s", Advanced(dread));

        var fireDamage = new StatModifierGrant(StatModifierTarget.OutgoingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff, CanModify: true, Magnitude: 1f, DurationFrames: 7200, ConditionTag: GameTags.DamageFire);
        Assert.AreEqual("x2 Fire Damage Dealt for 2m", Simple(fireDamage));
        Assert.AreEqual("+3 Strength permanently", Simple(fireDamage with { Target = StatModifierTarget.Strength, Operation = StatModifierOperation.Additive, Magnitude = 3f, DurationFrames = null, ConditionTag = default }));
    }

    [TestMethod]
    public void Qualifiers_AreLockedDurationWithItsTooltip_ForAFixedDurationOnly()
    {
        var dread = TestContent.Aura(BuiltInAuraIds.DreadAura).Effects[0].Entries[0];
        CollectionAssert.AreEqual(new[] { (EffectFormatting.LockedDuration, (string?)EffectFormatting.LockedDurationTooltip) }, EffectFormatting.AdvancedQualifiers(dread).ToList());

        var insulation = TestContent.Item(BuiltInItemIds.ResistanceTestPotion).Effects[0].Entries[0];
        Assert.IsEmpty(EffectFormatting.AdvancedQualifiers(insulation));

        var antiMagic = TestContent.Aura(BuiltInAuraIds.AntiMagic).Effects[0].Entries[0];
        CollectionAssert.AreEqual(new[] { (EffectFormatting.LockedDuration, (string?)EffectFormatting.LockedDurationTooltip) }, EffectFormatting.AdvancedQualifiers(antiMagic).ToList());

        Assert.IsEmpty(EffectFormatting.AdvancedQualifiers(new DirectDamage(1, 1)));
    }

    [TestMethod]
    public void QualifierLines_FollowTheirEntry_InAdvancedMode_Only()
    {
        var dread = TestContent.Aura(BuiltInAuraIds.DreadAura);

        var advanced = ItemComparisonStatExtraction.ExtractEffectStats(dread.Effects, Names, DetailsMode.Advanced);
        Assert.AreEqual(2, advanced.Count);
        Assert.AreEqual("x4 Caution for 2s", advanced[0].DisplayText);
        Assert.AreEqual(EffectFormatting.LockedDuration, advanced[1].DisplayText);
        Assert.AreEqual(EffectFormatting.LockedDurationTooltip, advanced[1].TooltipText);

        Assert.AreEqual(1, ItemComparisonStatExtraction.ExtractEffectStats(dread.Effects, Names, DetailsMode.Simple).Count);
    }

    [TestMethod]
    public void ADamageReduction_ReadsAsAReduction_WithItsDuration()
    {
        var insulation = TestContent.Item(BuiltInItemIds.ResistanceTestPotion).Effects[0].Entries[0];

        Assert.AreEqual("50% Fire Damage Reduction for 10m", Simple(insulation));
    }

    [TestMethod]
    public void AnAuraGrant_IsOneLine_SizeThenAuraThenStrength_EverywhereItShows()
    {
        var torch = TestContent.Item(BuiltInItemIds.ScrollOfTorch);
        Assert.AreEqual("Grants a size 3 Light aura with strength 8 for 10s", Simple(torch.Effects[0].Entries[0]));

        var toxicIdol = TestContent.Item(BuiltInItemIds.ToxicIdol);
        var itemDetailsLines = ItemComparisonStatExtraction.ExtractEffectStats(toxicIdol, Names, DetailsMode.Simple);
        Assert.AreEqual(1, itemDetailsLines.Count);
        Assert.AreEqual("Grants a size 4 Poison aura with strength 16", itemDetailsLines[0].DisplayText);
        Assert.AreEqual(16, itemDetailsLines[0].ComparableValue);
    }

    [TestMethod]
    public void AnAurasStrengthLines_DescribeItsStrength_AndAGlowOnlyAuraHasNone()
    {
        CollectionAssert.AreEqual(new[] { "Effects scale with its strength", "Strength fading to the edge of its reach" }, AuraDetailsWindow.StrengthLines(TestContent.Aura(BuiltInAuraIds.ToxicIdolPoison)).ToList());
        CollectionAssert.AreEqual(new[] { "Full strength to the edge of its reach" }, AuraDetailsWindow.StrengthLines(TestContent.Aura(BuiltInAuraIds.DreadAura)).ToList());
        Assert.IsEmpty(AuraDetailsWindow.StrengthLines(TestContent.Aura(BuiltInAuraIds.TorchLight)));
    }

    [TestMethod]
    [DataRow(60, "1s")]
    [DataRow(61, "2s")]
    [DataRow(3540, "59s")]
    [DataRow(3600, "1m")]
    [DataRow(5400, "2m")]
    [DataRow(36000, "10m")]
    [DataRow(216000, "1h")]
    public void Durations_ReadInTheLargestWholeUnit_RoundedUp(int frames, string expected)
    {
        Assert.AreEqual(expected, EffectFormatting.FormatDuration(frames));
    }
}
