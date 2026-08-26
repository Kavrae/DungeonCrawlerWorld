using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Burning;
using Game.Modules.Burning.Components;
using Game.Modules.Health.Components;
using Game.Modules.Paralysis;
using Game.Modules.Paralysis.Components;
using Game.Modules.Poison;
using Game.Modules.Poison.Components;
using Game.Modules.StatusEffects;
using Game.Modules.StatusEffects.Components;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Inventory;
using System.Linq;

namespace Tests.Presentation;

/// <summary>
/// Drives the real click pipeline (Button.HandleClick, the same public entry point
/// UiInputController itself calls on a real mouse click) rather than calling WindowLifecycle/the
/// button's Clicked handler directly -- per this session's own "live testing catches what code
/// review misses" lesson for click/hit-test work.
/// </summary>
[TestClass]
public sealed class HealthWindowControllerTests
{
    private const int PlayerEntityId = 1;

    private static (HealthWindowController Health, UiLayerStack Layers) Build()
    {
        var world = new Game.World.World(new Game.World.Map(new Vector3Int(20, 20, 1))) { PlayerEntityId = PlayerEntityId };
        var fontService = new FontService("Fonts");
        var labelRenderer = new LabelRenderer();
        var layers = new UiLayerStack();
        var pool = TestElementPoolServiceFactory.Create(fontService, labelRenderer);

        var componentManager = new ComponentManager(20, 10);
        componentManager.RegisterPackedPool<SimpleHealthComponent>(static (ref existing, incoming) => existing = incoming);
        componentManager.RegisterMultiPool<BodyPartComponent>();
        componentManager.RegisterMultiPool<StatusEffectStack>();
        componentManager.RegisterPackedPool<PoisonTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<BurningTimerComponent>(static (ref existing, incoming) => { });
        componentManager.RegisterPackedPool<ParalysisTimerComponent>(static (ref existing, incoming) => { });

        componentManager.Merge(PlayerEntityId, new SimpleHealthComponent(50, 100));

        var statusEffectDisplays = new StatusEffectDisplayRegistry();
        statusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<PoisonTimerComponent>(StatusEffectType.Poison, PoisonEffects.Glyph,
            poison => poison.FramesUntilNextTick + (poison.RemainingDurationTicks - 1) * PoisonEffects.TickIntervalFrames));
        statusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<BurningTimerComponent>(StatusEffectType.Burning, BurningEffects.Glyph,
            burning => burning.FramesUntilNextTick + (burning.StackCount - 1) * BurningEffects.TickIntervalFrames));
        statusEffectDisplays.Register(new TimerBasedStatusEffectDisplay<ParalysisTimerComponent>(StatusEffectType.Paralysis, ParalysisEffects.Glyph,
            paralysis => paralysis.FramesUntilNextTick));

        pool.RegisterFactory<HealthWindow>(() => new HealthWindow(fontService, pool, labelRenderer, componentManager, statusEffectDisplays));
        pool.RegisterFactory<TextDivider>(() => new TextDivider(fontService, pool, labelRenderer));
        pool.RegisterFactory<FractionBarElement>(() => new FractionBarElement(fontService, pool, labelRenderer));

        var health = new HealthWindowController(pool, world, componentManager, fontService, labelRenderer);
        health.Initialize(layers);

        return (health, layers);
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

    [TestMethod]
    public void FolderPosition_ShiftedBelowHealthButton_NoOverlap()
    {
        var buttonBottom = HealthWindowController.ButtonPosition.Y + HealthWindowController.ButtonSize.Y;

        Assert.IsTrue(InventoryFolderController.FolderPosition.Y >= buttonBottom, "The Inventory Folder must sit at or below the Health button's own bottom edge -- no overlap.");
        Assert.AreEqual(HealthWindowController.ButtonPosition.X, InventoryFolderController.FolderPosition.X, "Both stay left-aligned under HudMetrics.Margin.");
    }
}
