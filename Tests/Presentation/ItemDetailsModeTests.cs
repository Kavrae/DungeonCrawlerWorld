using Game.Bootstrap;
using Game.Effects;
using Game.Effects.Entries;
using Game.Modules.Inventory;
using Game.Modules.Lootboxes;
using Game.Modules.StatModifiers;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Details;
using Presentation.UI.Inventory;
using Tests.UIEngine;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>Advanced mode's change after a line, and Item Details Comparison's difference on the better side only.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ItemDetailsModeTests
{
    private const float ContentWidth = 460f;

    private static readonly Lazy<GameBuildPassResult> Build = new(() => BuiltInTestModules.Build(GameBuildPass.CreatePlaceholderMap()));

    private static PreviewedValue Value(float baseValue, float value) => new(baseValue, value, []);

    private static ShownValue Shown(float baseValue, float value) => new("Value", Value(baseValue, value), static amount => $"{amount:0.#}");

    [TestMethod]
    public void OneChangedValue_ShowsItsSignedDifference()
    {
        Assert.AreEqual(("(+7)", true), PreviewedValueText.Change([Shown(6, 13)]));
        Assert.AreEqual(("(-2)", false), PreviewedValueText.Change([Shown(6, 4)]));
        Assert.IsNull(PreviewedValueText.Change([Shown(6, 6)]));
    }

    [TestMethod]
    public void SeveralChangedValues_ShareTheirDifference_ElseTheirMultiplier_ElseListEach()
    {
        Assert.AreEqual(("(+13)", true), PreviewedValueText.Change([Shown(35, 48), Shown(40, 53)]));
        Assert.AreEqual(("(x2)", true), PreviewedValueText.Change([Shown(35, 70), Shown(40, 80)]));
        Assert.AreEqual(("(+1, +5)", true), PreviewedValueText.Change([Shown(4, 5), Shown(16, 21)]));
    }

    [TestMethod]
    public void TheTooltip_ListsBaseEachStepAndTotal_HeadedByLabelWhenSeveralChanged()
    {
        var doubled = new PreviewedValue(7, 14, [new ValueContribution(ValueContributionKind.Modifier, StatModifierOperation.Multiplicative, 1f)]);

        Assert.AreEqual("Base: 7\nModifier: x2\nTotal: 14", PreviewedValueText.Tooltip([new ShownValue("Highest", doubled, static amount => $"{amount:0.#}")], EffectTextNames.None));
        Assert.IsNull(PreviewedValueText.Tooltip([Shown(6, 6)], EffectTextNames.None));
    }

    [TestMethod]
    [DataRow(53.0, StatUnit.Number, "(+53)")]
    [DataRow(25.0, StatUnit.Percent, "(+25%)")]
    [DataRow(-180.0, StatUnit.Frames, "(-3s)")]
    public void AComparisonDifference_IsWrittenInItsUnit(double difference, StatUnit unit, string expected)
    {
        Assert.AreEqual(expected, ItemDetailsWindow.ComparisonDifferenceText(difference, unit));
    }

    [TestMethod]
    public void AComparison_ShowsTheDifferenceOnTheBetterSideOnly()
    {
        var context = Build.Value.Context;
        var stronger = context.Items.Get(BuiltInItemIds.HealthPotion);
        var weaker = stronger with { Id = Guid.NewGuid(), Effects = [new Effect([new DirectHeal(0.25f)])] };
        var elementPool = CreateElementPool();

        var strongerTexts = TextsOf(Open(elementPool, stronger, weaker));
        var weakerTexts = TextsOf(Open(elementPool, weaker, stronger));

        CollectionAssert.Contains(strongerTexts, "(+25%)");
        Assert.IsFalse(weakerTexts.Any(static text => text.StartsWith("(+") || text.StartsWith("(-")), string.Join(" | ", weakerTexts));
    }

    [TestMethod]
    [DataRow(DetailsMode.Simple)]
    [DataRow(DetailsMode.Advanced)]
    public void TheToxicIdol_ShowsItsAurasSection_AndNoSelfShape(DetailsMode mode)
    {
        var idol = Build.Value.Context.Items.Get(BuiltInItemIds.ToxicIdol);
        var window = Open(CreateElementPool(), idol, mode);

        var texts = TextsOf(window);
        var grantLine = texts.IndexOf("Grants a size 4 Poison aura with strength 16");
        Assert.IsGreaterThanOrEqualTo(0, grantLine, string.Join(" | ", texts));
        Assert.IsGreaterThan(grantLine, texts.IndexOf("Poison aura"), "The aura's heading follows its grant.");
        CollectionAssert.Contains(texts, "Each second:");
        Assert.IsFalse(texts.Any(static text => text.StartsWith("Shape:") || text.StartsWith("Range:")), string.Join(" | ", texts));
        Assert.AreEqual(1, Descendants(window).OfType<TargetShapePreviewElement>().Count(), "Only the aura's reach is drawn; a toggle has no shape grid.");

        if (mode == DetailsMode.Simple)
        {
            CollectionAssert.Contains(texts, "Tops Poison up to 16 stacks");
            return;
        }

        CollectionAssert.Contains(texts, "Strength 16 at the source, 4 at the edge");
        CollectionAssert.Contains(texts, "Strength fading to the edge of its reach");
        CollectionAssert.Contains(texts, "Tops Poison up to 16 stacks at the source");
        CollectionAssert.Contains(texts, "Tops Poison up to 4 stacks at the edge");
    }

    [TestMethod]
    public void TheScrollOfTorch_ShowsItsGlowOnlyAura_AfterItsGrant_WithNoEffects()
    {
        var torch = Build.Value.Context.Items.Get(BuiltInItemIds.ScrollOfTorch);
        var texts = TextsOf(Open(CreateElementPool(), torch, DetailsMode.Advanced));

        var grantLine = texts.FindIndex(static text => text.StartsWith("Grants a size 3 Light aura"));
        Assert.IsGreaterThanOrEqualTo(0, grantLine, string.Join(" | ", texts));
        Assert.AreEqual(grantLine + 2, texts.IndexOf("Light aura"), "The heading (swatch, then name) follows the grant line.");
        CollectionAssert.DoesNotContain(texts, "Each second:");
    }

    [TestMethod]
    public void AFlatAura_ReadsTheSameAtTheSourceAndTheEdge()
    {
        var dreadIdol = Build.Value.Context.Items.Get(BuiltInItemIds.DreadIdol);
        var texts = TextsOf(Open(CreateElementPool(), dreadIdol, DetailsMode.Advanced));

        CollectionAssert.Contains(texts, "Strength 8 to the edge");
        CollectionAssert.Contains(texts, "x4 Caution for 2s");
        Assert.IsFalse(texts.Any(static text => text.EndsWith(" at the edge") && !text.StartsWith("Strength")), string.Join(" | ", texts));
    }

    [TestMethod]
    [DataRow(DetailsMode.Simple)]
    [DataRow(DetailsMode.Advanced)]
    public void ALootBox_ShowsWhatItOpensInto_AndUntradableAboveItsTags(DetailsMode mode)
    {
        var lootbox = Build.Value.Context.Lootboxes.GetOrCreateItem(new LootboxKind(LootboxTypes.Adventurer.Id, LootboxRarity.Bronze));
        var window = Open(CreateElementPool(), lootbox, mode);

        var texts = TextsOf(window);
        CollectionAssert.Contains(texts, "1-10 of one random item");
        Assert.AreEqual("Untradable", texts[^2], string.Join(" | ", texts));
        Assert.AreEqual(ItemFactsText.DescriptionOf(ItemRestriction.Untradable), Descendants(window).OfType<TextWindow>().Single(static line => line.OriginalText == "Untradable").TooltipText);
    }

    [TestMethod]
    public void ATradableItem_HasNoRestrictionLine_AndNoContents()
    {
        var texts = TextsOf(Open(CreateElementPool(), Build.Value.Context.Items.Get(BuiltInItemIds.HealthPotion), DetailsMode.Advanced));

        CollectionAssert.DoesNotContain(texts, "Untradable");
        Assert.IsFalse(texts.Any(static text => text.Contains("random item")));
    }

    [TestMethod]
    public void EveryContentsKind_IsDescribed()
    {
        var kinds = typeof(IItemContents).Assembly.GetTypes().Where(static type => typeof(IItemContents).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false });

        foreach (var kind in kinds)
        {
            Assert.Contains(kind, ItemFactsText.DescribedContentsKinds, $"{kind.Name} has no \"Opens into\" text.");
        }
    }

    [TestMethod]
    public void SetContents_ListEachItemByName_WithItsQuantity()
    {
        var names = new EffectTextNames(Items: Build.Value.Context.Items);
        var contents = new SetItemContents([new ItemContentsEntry(BuiltInItemIds.HealthPotion, 3)]);

        CollectionAssert.AreEqual(new[] { $"{Build.Value.Context.Items.Get(BuiltInItemIds.HealthPotion).Name} x3" }, ItemFactsText.Contents(contents, names).ToList());
    }

    private static ItemDetailsWindow Open(ElementPoolService elementPool, ItemDefinition shown, DetailsMode mode)
    {
        var window = elementPool.CreateElement<ItemDetailsWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(ContentWidth, 2000), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, ShowBorder = true },
        });
        window.ChosenMode = () => mode;
        window.Configure(-1, ItemDetailsWindow.NoStackInstanceId, shown, ContentWidth);
        window.Initialize();
        return window;
    }

    private static IEnumerable<Element> Descendants(Element element)
    {
        foreach (var child in element.ChildElements)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static ElementPoolService CreateElementPool()
    {
        var context = Build.Value.Context;
        var fontService = TestFonts.Shared;
        var elementPool = TestElementPoolServiceFactory.Create(fontService);
        var actionStateView = TestActionStateViews.Over(Build.Value.EcsContext.ComponentManager, context.Actions, context.Items);
        elementPool.RegisterFactory(() => new TextDivider(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory(() => new TargetShapePreviewElement(TestElementServices.For(elementPool, fontService)));
        elementPool.RegisterFactory(() => new ItemDetailsWindow(TestElementServices.For(elementPool, fontService), context.Actions, new EffectTextNames(context.GameplayTags, context.Terrain, context.StatusEffectDisplays), actionStateView, new ActivationPreviews(context.EffectServices, context.SimulationClock), TestPlayerQuery.NoPlayer));
        return elementPool;
    }

    private static ItemDetailsWindow Open(ElementPoolService elementPool, ItemDefinition shown, ItemDefinition comparedAgainst)
    {
        var window = elementPool.CreateElement<ItemDetailsWindow>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
            Layout = new ElementLayoutOptions { MaximumSize = new Vector2(ContentWidth, 2000), DisplayMode = ElementDisplayMode.WrapContent },
            Chrome = new ElementChromeOptions { ShowTitle = true, ShowBorder = true },
        });
        window.AlwaysSimple = true;
        window.Configure(-1, ItemDetailsWindow.NoStackInstanceId, shown, ContentWidth, [comparedAgainst]);
        window.Initialize();
        return window;
    }

    private static List<string> TextsOf(Element element)
    {
        var texts = new List<string>();
        foreach (var child in element.ChildElements)
        {
            if (child is TextWindow textWindow)
            {
                texts.Add(textWindow.OriginalText);
            }

            texts.AddRange(TextsOf(child));
        }

        return texts;
    }
}
