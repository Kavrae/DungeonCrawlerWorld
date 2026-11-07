using Engine.ECS.Components;
using Engine.ECS.Systems;
using Engine.Math;
using Game.Modules.Inventory;
using Game.Modules.Paralysis;
using Game.Modules.Paralysis.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatusEffects;
using Game.Terrain;
using Game.Modules.Auras;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.UI.Content;
using System.Linq;
using Tests.UIEngine;
using UIEngine.Input;
using UIEngine.UI;
using UIEngine.UI.Chrome;

namespace Tests.Presentation;

/// <summary>Hovering a HUD status effect icon shows the popup the Health window shows for that effect's row, through the real hit-test (a UiInputController publishing the cursor).</summary>
[TestClass]
[DoNotParallelize]
public sealed class PlayerStatusEffectsContentTests
{
    private const int PlayerEntityId = 1;
    private static readonly Rectangle ScreenBounds = new(0, 0, 1920, 1080);

    private sealed record Harness(PlayerStatusEffectsContent Content, Window HostWindow, UiInputController Input, UiLayerStack Layers);

    private static Harness Build()
    {
        var world = TestWorlds.Create(new Game.World.Map(new Vector3Int(20, 20, 1)), playerEntityId: PlayerEntityId);
        var fontService = TestFonts.Shared;
        var layers = new UiLayerStack();
        var pool = TestElementPoolServiceFactory.Create(fontService);
        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));
        componentManager.Keys.Issue(PlayerEntityId);

        componentManager.GetPackedPool<PoisonTimerComponent>().Add(PlayerEntityId, new PoisonTimerComponent(nextTickFrame: 30, stackCount: 3, remainingDurationTicks: 3, ActionSource.Admin));
        componentManager.GetPackedPool<ParalysisTimerComponent>().Add(PlayerEntityId, new ParalysisTimerComponent(expiresAtFrame: 120));

        var statusEffectDisplays = new StatusEffectDisplayRegistry();
        statusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph, PoisonEffects.MaxStacks, componentManager.GetPackedPool<PoisonTimerComponent>(),
            (poison, now) => TimedStatusEffectStacks.RemainingFrames(poison, now, PoisonEffects.TickIntervalFrames), static poison => poison.Source));
        statusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<ParalysisTimerComponent>(StatusEffectType.Paralysis, ParalysisEffects.Glyph, maxStacks: 1, componentManager.GetPackedPool<ParalysisTimerComponent>(),
            (paralysis, now) => FrameDeadline.Remaining(paralysis.ExpiresAtFrame, now)));

        pool.RegisterFactory<Tooltip>(() => new Tooltip(TestElementServices.For(pool, fontService)));
        var tooltipController = new TooltipController { ScreenBoundsOverrideForTests = ScreenBounds };
        tooltipController.Initialize(pool, layers);

        var pointerState = new PointerState();
        var content = new PlayerStatusEffectsContent(world, TestActionStateViews.Over(componentManager), new ItemCatalog(), fontService, statusEffectDisplays, new SimulationClock(),
            TestActionSources.Naming(new TerrainRegistry(), new AuraCatalog(), componentManager.Keys), tooltipController, pointerState);
        var hostWindow = pool.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(1600, 60),
                Size = PlayerStatusEffectsContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
                IsTransparent = true,
            },
            Chrome = new ElementChromeOptions { ShowTitle = false, ShowBorder = false, CanUserFocus = false },
        });
        hostWindow.SetContent(content);
        hostWindow.Initialize();
        layers.Add(UiLayer.StaticHud, hostWindow);

        var input = new UiInputController(layers, new Vector2(ScreenBounds.Width, ScreenBounds.Height), pointerState, [], []);
        return new Harness(content, hostWindow, input, layers);
    }

    /// <summary>The centre of the icon at index, counting from the left: Poison is first, Paralysis second (StatusEffectType order).</summary>
    private static Point IconCentre(Window hostWindow, int index)
    {
        var iconSize = hostWindow.ContentSize.Y;
        var centre = hostWindow.ContentDrawOrigin + new Vector2((iconSize + 1f) * index + iconSize / 2f, iconSize / 2f);
        return new Point((int)centre.X, (int)centre.Y);
    }

    private static void HoverFor(Harness harness, Point position, int frameCount)
    {
        for (var frame = 0; frame < frameCount; frame++)
        {
            harness.Input.Update(default, new MouseState(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released));
            harness.Content.Update(new GameTime());
        }
    }

    private static Tooltip FindTooltip(UiLayerStack layers) => layers[UiLayer.Tooltip].OfType<Tooltip>().Single();

    [TestMethod]
    public void HoveringAnIcon_AfterTheDelay_ShowsTheHealthWindowsPopupForThatEffect()
    {
        var harness = Build();
        var tooltip = FindTooltip(harness.Layers);

        HoverFor(harness, IconCentre(harness.HostWindow, 0), PopupChrome.HoverTooltipDelayFrames - 1);
        Assert.IsFalse(tooltip.IsVisible);

        HoverFor(harness, IconCentre(harness.HostWindow, 0), 1);
        Assert.IsTrue(tooltip.IsVisible);
        Assert.AreEqual($"{PoisonEffects.Glyph} Poison x3\n3s remaining", tooltip.OriginalText);
    }

    [TestMethod]
    public void MovingOffTheIcon_HidesThePopup()
    {
        var harness = Build();
        var tooltip = FindTooltip(harness.Layers);
        HoverFor(harness, IconCentre(harness.HostWindow, 0), PopupChrome.HoverTooltipDelayFrames);

        HoverFor(harness, new Point(10, 10), 1);

        Assert.IsFalse(tooltip.IsVisible);
    }

    [TestMethod]
    public void HoveringAnEffectThatRecordsNoSource_ShowsNothing()
    {
        var harness = Build();

        HoverFor(harness, IconCentre(harness.HostWindow, 1), PopupChrome.HoverTooltipDelayFrames);

        Assert.IsFalse(FindTooltip(harness.Layers).IsVisible);
    }
}
