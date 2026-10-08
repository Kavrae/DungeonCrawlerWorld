using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Auras;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.Terrain;
using Game.World;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Health.Components;
using Game.Modules.Inventory;
using Game.Modules.Paralysis;
using Game.Modules.Paralysis.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatusEffects;
using Game.Views;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Chrome;
using System.Linq;

namespace Tests.Presentation;

/// <summary>
/// Drives the real click pipeline (Button.HandleClick, the same public entry point
/// UiInputController itself calls on a real mouse click) rather than calling WindowLifecycle/the
/// button's Clicked handler directly -- per this session's own "live testing catches what code
/// review misses" lesson for click/hit-test work.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class HealthWindowControllerTests
{
    private const int PlayerEntityId = 1;

    private sealed record Harness(
        HealthWindowController Health,
        UiLayerStack Layers,
        ComponentManager ComponentManager,
        UiInputController Input,
        TerrainRegistry Terrain,
        AuraCatalog Auras);

    private static (HealthWindowController Health, UiLayerStack Layers) Build()
    {
        var harness = BuildHarness();
        return (harness.Health, harness.Layers);
    }

    private static Harness BuildHarness()
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)), playerEntityId: PlayerEntityId);
        var fontService = TestFonts.Shared;
        var labelRenderer = new LabelRenderer();
        var layers = new UiLayerStack();
        var pool = TestElementPoolServiceFactory.Create(fontService, labelRenderer);

        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));

        componentManager.Merge(PlayerEntityId, new SimpleHealthComponent(50, 100));

        var statusEffectDisplays = new StatusEffectDisplayRegistry();
        statusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph, componentManager.GetPackedPool<PoisonTimerComponent>(),
            (poison, now) => FrameDeadline.Remaining(poison.NextTickFrame, now) + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames));
        statusEffectDisplays.Register(new BurningDisplay(componentManager.GetPackedPool<BurningTimerComponent>(), componentManager.GetMultiPool<BodyPartBurningTimerComponent>()));
        statusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<ParalysisTimerComponent>(StatusEffectType.Paralysis, ParalysisEffects.Glyph, componentManager.GetPackedPool<ParalysisTimerComponent>(),
            (paralysis, now) => FrameDeadline.Remaining(paralysis.ExpiresAtFrame, now)));

        var itemCatalog = new ItemCatalog();
        var terrain = new TerrainRegistry();
        var auras = new AuraCatalog();
        var pointerState = new PointerState();

        pool.RegisterFactory<Tooltip>(() => new Tooltip(fontService, pool, labelRenderer));
        var tooltipController = new TooltipController { ScreenBoundsOverrideForTests = new Rectangle(0, 0, 2000, 2000) };
        tooltipController.Initialize(pool, layers);

        pool.RegisterFactory<HealthWindow>(() => new HealthWindow(fontService, pool, labelRenderer, new HealthView(componentManager, BodyPartTestWorld.PartsOf(componentManager)), new StatModifierView(componentManager), TestActionStateViews.Over(componentManager), BodyPartTestWorld.PartsOf(componentManager), statusEffectDisplays, itemCatalog, TestGameplayTags.BuiltIn, simulationClock: new SimulationClock(),
            TestActionSources.Naming(terrain, auras), tooltipController, pointerState));
        pool.RegisterFactory<TextDivider>(() => new TextDivider(fontService, pool, labelRenderer));
        pool.RegisterFactory<FractionBarElement>(() => new FractionBarElement(fontService, pool, labelRenderer));

        var health = new HealthWindowController(pool, world);
        health.Initialize(layers);

        var input = TestUiInputController.Create(layers, new Vector2(2000, 2000), componentManager, world, new EventBus(), itemCatalog, pointerState: pointerState);

        return new Harness(health, layers, componentManager, input, terrain, auras);
    }

    private static MouseState MouseAt(Point position) =>
        new(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    /// <summary>Holds the cursor at position for frameCount frames: the input controller publishes it, then the window reads it.</summary>
    private static void HoverFor(Harness harness, HealthWindow window, Point position, int frameCount)
    {
        for (var frame = 0; frame < frameCount; frame++)
        {
            harness.Input.Update(default, MouseAt(position));
            window.Update(new GameTime());
        }
    }

    private static HealthWindow OpenWindow(Harness harness)
    {
        var button = FindButton(harness.Layers);
        button.HandleClick(button.Rectangle.Center);
        return FindWindow(harness.Layers)!;
    }

    private static Tooltip FindTooltip(UiLayerStack layers) => layers[UiLayer.Tooltip].OfType<Tooltip>().Single();

    private static TextWindow FindRow(HealthWindow window, string textFragment) =>
        SelfAndDescendants(window).OfType<TextWindow>().Last(row => row.OriginalText.Contains(textFragment));

    [TestMethod]
    public void HoveringATerrainSourcedModifierRow_ShowsTheTerrainsName_AndMovingOffHidesIt()
    {
        var harness = BuildHarness();
        var holyGroundId = harness.Terrain.Register(new TerrainDefinition("test:holy", "Holy Ground", "", default, ".", default));
        harness.ComponentManager.GetMultiPool<StatModifierComponent>().Add(PlayerEntityId, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: -0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.FromTerrain(holyGroundId)));
        var window = OpenWindow(harness);
        var row = FindRow(window, "Resistance");
        var tooltip = FindTooltip(harness.Layers);

        HoverFor(harness, window, row.Rectangle.Center, 1);
        Assert.AreEqual(global::Presentation.UI.ColorPalettes.WindowPalette.HighlightColor, row.ContentColor);

        HoverFor(harness, window, row.Rectangle.Center, HudChrome.HoverTooltipDelayFrames - 2);
        Assert.IsFalse(tooltip.IsVisible);

        HoverFor(harness, window, row.Rectangle.Center, 1);
        Assert.IsTrue(tooltip.IsVisible);
        Assert.AreEqual("Holy Ground", tooltip.TitleText);
        Assert.AreEqual("50% Damage Resistance\nPermanent", tooltip.OriginalText);

        HoverFor(harness, window, new Point(1999, 1999), 1);
        Assert.IsFalse(tooltip.IsVisible);
        Assert.AreEqual(Color.Transparent, row.ContentColor);
    }

    [TestMethod]
    public void HoveringAnAuraSourcedBodyPartBurn_ShowsTheAurasName_AndMovingOffHidesIt()
    {
        var harness = BuildHarness();
        var auraId = harness.Auras.Register(new AuraDefinition(new Guid("00000000-0000-0000-0000-00000000b002"), "Lava", Color.OrangeRed));
        harness.ComponentManager.GetMultiPool<BodyPartBurningTimerComponent>().Add(PlayerEntityId, new BodyPartBurningTimerComponent(partId: 0, stackCount: 3, nextTickFrame: 60, ActionSource.FromAura(auraId)));
        var window = OpenWindow(harness);
        var bodyPartBurningRow = FindRow(window, "Burning");
        var tooltip = FindTooltip(harness.Layers);

        HoverFor(harness, window, bodyPartBurningRow.Rectangle.Center, HudChrome.HoverTooltipDelayFrames);
        Assert.IsTrue(tooltip.IsVisible);
        Assert.AreEqual("Lava", tooltip.TitleText);
        StringAssert.Contains(tooltip.OriginalText, "Burning x3");

        HoverFor(harness, window, new Point(1999, 1999), 1);
        Assert.IsFalse(tooltip.IsVisible);
    }

    [TestMethod]
    public void ClosingTheWindowMidHover_HidesThePopup()
    {
        var harness = BuildHarness();
        harness.ComponentManager.GetMultiPool<StatModifierComponent>().Add(PlayerEntityId, new StatModifierComponent(StatModifierTarget.IncomingDamage, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: -0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));
        var window = OpenWindow(harness);
        var tooltip = FindTooltip(harness.Layers);
        HoverFor(harness, window, FindRow(window, "Resistance").Rectangle.Center, HudChrome.HoverTooltipDelayFrames);
        Assert.IsTrue(tooltip.IsVisible);

        var button = FindButton(harness.Layers);
        button.HandleClick(button.Rectangle.Center);

        Assert.IsFalse(tooltip.IsVisible);
    }

    private static Button FindButton(UiLayerStack layers) => layers[UiLayer.DynamicHud].OfType<Button>().Single();

    private static HealthWindow? FindWindow(UiLayerStack layers) => layers[UiLayer.DynamicHud].OfType<HealthWindow>().SingleOrDefault();

    [TestMethod]
    public void ButtonClick_OpensHealthWindow()
    {
        var (_, layers) = Build();
        var button = FindButton(layers);

        button.HandleClick(button.Rectangle.Center);

        Assert.IsNotNull(FindWindow(layers), "Clicking the heart button must open the HealthWindow.");
    }

    [TestMethod]
    public void ButtonClick_Twice_ClosesHealthWindow()
    {
        var (_, layers) = Build();
        var button = FindButton(layers);

        button.HandleClick(button.Rectangle.Center);
        Assert.IsNotNull(FindWindow(layers), "Sanity check: the window must have opened first.");

        button.HandleClick(button.Rectangle.Center);

        Assert.IsNull(FindWindow(layers), "Re-clicking an already-open window's own trigger must close it, same as Inventory/Ability Score's own toggle.");
    }

    private static IEnumerable<Element> SelfAndDescendants(Element element) =>
        element.ChildElements.SelectMany(SelfAndDescendants).Prepend(element);

    [TestMethod]
    public void ButtonClick_OpensHealthWindow_BodyPartBarShowsCurrentOverMaximum()
    {
        var (_, layers) = Build();
        var button = FindButton(layers);

        button.HandleClick(button.Rectangle.Center);

        var bar = SelfAndDescendants(FindWindow(layers)!).OfType<FractionBarElement>().Single();
        Assert.AreEqual("50 / 100", bar.ValueText);
    }

    [TestMethod]
    public void InventoryButtonPosition_ShiftedBelowHealthButton_NoOverlap()
    {
        var buttonBottom = HealthWindowChrome.ButtonPosition.Y + HealthWindowChrome.ButtonSize.Y;

        Assert.IsGreaterThanOrEqualTo(buttonBottom, InventoryChrome.ButtonPosition.Y, "The Inventory button must sit at or below the Health button's own bottom edge -- no overlap.");
        Assert.AreEqual(HealthWindowChrome.ButtonPosition.X, InventoryChrome.ButtonPosition.X, "Both stay left-aligned under HudChrome.Margin.");
    }
}
