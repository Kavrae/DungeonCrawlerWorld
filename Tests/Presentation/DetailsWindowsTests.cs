using Engine.Tags;
using Microsoft.Xna.Framework;
using Game.Bootstrap;
using Game.Modules.Actions;
using Presentation.UI;
using Presentation.UI.Content;
using Presentation.UI.Details;
using Presentation.UI.Inventory;
using Tests.UIEngine;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Tests.Presentation;

/// <summary>Item Details, Action Details and Aura Details show every built-in item, action and aura with the sections they share, in the same order.</summary>
[TestClass]
[DoNotParallelize]
public sealed class DetailsWindowsTests
{
    private const float ContentWidth = 460f;

    private static readonly Lazy<GameBuildPassResult> Build = new(() => BuiltInTestModules.Build(GameBuildPass.CreatePlaceholderMap()));

    private static ElementPoolService CreateElementPool()
    {
        var context = Build.Value.Context;
        var fontService = TestFonts.Shared;
        var elementPool = TestElementPoolServiceFactory.Create(fontService);
        var spriteSheetService = new SpriteSheetService(null, "Spritesheets", TestSprites.Resolver);
        var actionStateView = TestActionStateViews.Over(Build.Value.EcsContext.ComponentManager, context.Actions, context.Items);

        elementPool.RegisterFactory(() => new TextDivider(fontService, elementPool, new UiRenderService()));
        elementPool.RegisterFactory(() => new ItemIconElement(fontService, elementPool, new UiRenderService(), spriteSheetService));
        elementPool.RegisterFactory(() => new TargetShapePreviewElement(fontService, elementPool, new UiRenderService()));
        elementPool.RegisterFactory(() => new ItemDetailsWindow(fontService, elementPool, new UiRenderService(), context.Actions, context.GameplayTags, actionStateView, TestPlayerQuery.NoPlayer));
        elementPool.RegisterFactory(() => new ActionDetailsWindow(fontService, elementPool, new UiRenderService(), context.Actions, context.GameplayTags));
        elementPool.RegisterFactory(() => new AuraDetailsWindow(fontService, elementPool, new UiRenderService(), context.Actions, context.GameplayTags));
        return elementPool;
    }

    private static ElementOptions DetailsWindowOptions() => new()
    {
        Hierarchy = new ElementHierarchyOptions { CanContainChildren = true, ChildrenTileMode = ChildElementTileMode.Vertical },
        Layout = new ElementLayoutOptions { MaximumSize = new Vector2(ContentWidth, 2000), DisplayMode = ElementDisplayMode.WrapContent },
        Chrome = new ElementChromeOptions { ShowTitle = true, ShowBorder = true },
    };

    /// <summary>Every TextWindow's text under element, depth first in child order -- the order the window shows them in.</summary>
    private static List<string> TextsInOrder(Element element)
    {
        var texts = new List<string>();
        foreach (var child in element.ChildElements)
        {
            if (child is TextWindow textWindow)
            {
                texts.Add(textWindow.OriginalText);
            }

            texts.AddRange(TextsInOrder(child));
        }

        return texts;
    }

    private static string TagLine(ActivatableDefinition definition, GameplayTagRegistry gameplayTags)
    {
        var displayNames = new List<string>();
        foreach (var tag in definition.Tags)
        {
            displayNames.Add(gameplayTags.GetDisplayName(tag));
        }

        return string.Join(", ", displayNames);
    }

    /// <summary>Asserts the shared sections appear in texts in order -- name, effect lines, activation lines, description, tags -- and returns where the description (or, without one, the last activation line) sits.</summary>
    private static int AssertSharedSectionsInOrder(List<string> texts, ActivatableDefinition definition, IActionActivator? activator)
    {
        var context = Build.Value.Context;
        var expected = new List<string> { definition.Name };

        var effectLines = ItemComparisonStatExtraction.ExtractEffectStats(definition, context.GameplayTags).Select(stat => stat.DisplayText).ToList();
        expected.AddRange(effectLines.Count > 0 ? effectLines : ["None"]);

        if (activator is not null)
        {
            expected.AddRange(ItemComparisonStatExtraction.ExtractActivatorStats(activator, context.Actions).Select(stat => stat.DisplayText));
        }

        if (!string.IsNullOrWhiteSpace(definition.Description))
        {
            expected.Add(definition.Description);
        }

        var searchFrom = 0;
        var lastFound = -1;
        foreach (var line in expected)
        {
            var index = texts.IndexOf(line, searchFrom);
            Assert.IsGreaterThanOrEqualTo(0, index, $"{definition.Name}: \"{line}\" is missing or out of order in [{string.Join(" | ", texts)}].");
            lastFound = index;
            searchFrom = index + 1;
        }

        Assert.AreEqual(TagLine(definition, context.GameplayTags), texts[^1], $"{definition.Name}: the tags are not the last line.");
        return lastFound;
    }

    [TestMethod]
    public void ItemDetails_ShowsEveryBuiltInItem_SharedSectionsInOrder_ThenGoldThenTags()
    {
        var elementPool = CreateElementPool();
        foreach (var item in Build.Value.Context.Items.Definitions)
        {
            var window = elementPool.CreateElement<ItemDetailsWindow>(null, DetailsWindowOptions());
            window.Configure(-1, ItemDetailsWindow.NoStackInstanceId, item, ContentWidth);
            window.Initialize();

            var texts = TextsInOrder(window);
            var lastSharedLine = AssertSharedSectionsInOrder(texts, item, item.Activator);
            var goldLine = texts.IndexOf($"{item.GoldValue}G", lastSharedLine + 1);
            Assert.IsGreaterThan(lastSharedLine, goldLine, $"{item.Name}: the gold value is missing or comes before the shared sections.");

            window.Close();
        }
    }

    [TestMethod]
    public void ActionDetails_ShowsEveryBuiltInAction_SharedSectionsInOrder_WithNoGold()
    {
        var elementPool = CreateElementPool();
        foreach (var action in Build.Value.Context.Actions.Definitions)
        {
            var window = elementPool.CreateElement<ActionDetailsWindow>(null, DetailsWindowOptions());
            window.Configure(action, ContentWidth);
            window.Initialize();

            var texts = TextsInOrder(window);
            AssertSharedSectionsInOrder(texts, action, action.Activator);
            Assert.IsFalse(texts.Any(text => text.EndsWith('G') && int.TryParse(text[..^1], out _)), $"{action.Name}: an action shows a gold value.");

            window.Close();
        }
    }

    [TestMethod]
    public void AuraDetails_ShowsEveryAura_NameStrengthEffectsThenTags()
    {
        var context = Build.Value.Context;
        var elementPool = CreateElementPool();
        Assert.IsGreaterThan(0, context.Auras.Count);

        for (var auraId = 0; auraId < context.Auras.Count; auraId++)
        {
            var aura = context.Auras.Get((byte)auraId);
            var window = elementPool.CreateElement<AuraDetailsWindow>(null, DetailsWindowOptions());
            window.Configure(aura, ContentWidth);
            window.Initialize();

            var texts = TextsInOrder(window);
            var effectLines = ItemComparisonStatExtraction.ExtractEffectStats(aura.Effects, context.GameplayTags).Select(stat => stat.DisplayText).ToList();
            List<string> expected = [aura.Name, AuraDetailsWindow.MagnitudeText(aura.Magnitude), AuraDetailsWindow.FalloffText(aura.Falloff), .. effectLines.Count > 0 ? effectLines : ["None"]];

            var searchFrom = 0;
            foreach (var line in expected)
            {
                var index = texts.IndexOf(line, searchFrom);
                Assert.IsGreaterThanOrEqualTo(0, index, $"{aura.Name}: \"{line}\" is missing or out of order in [{string.Join(" | ", texts)}].");
                searchFrom = index + 1;
            }

            var tagNames = new List<string>();
            foreach (var tag in aura.Tags)
            {
                tagNames.Add(context.GameplayTags.GetDisplayName(tag));
            }

            Assert.AreEqual(string.Join(", ", tagNames), texts[^1], $"{aura.Name}: the tags are not the last line.");
            window.Close();
        }
    }

    [TestMethod]
    public void ActionDetails_ReconfiguredWithAnotherAction_ShowsOnlyTheNewOne()
    {
        var elementPool = CreateElementPool();
        var actions = Build.Value.Context.Actions.Definitions.ToList();
        var window = elementPool.CreateElement<ActionDetailsWindow>(null, DetailsWindowOptions());
        window.Configure(actions[0], ContentWidth);
        window.Initialize();

        window.Configure(actions[1], ContentWidth);

        var texts = TextsInOrder(window);
        Assert.AreEqual(actions[1].Name, texts[0]);
        CollectionAssert.DoesNotContain(texts, actions[0].Name);
    }
}
