using Engine.Diagnostics;
using Engine.ECS.Context;
using Engine.ECS.Systems;
using Engine.Events;
using Engine.Math;
using Game.Modules.Movement.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Chrome;
using Presentation.UI.Diagnostics;

namespace Tests.Presentation;

[TestClass]
[DoNotParallelize]
public sealed class DiagnosticsWindowTests
{
    private static readonly KeyboardState NoKeys = new();
    private static readonly KeyboardState F3Down = new(Keys.F3);
    private static readonly Vector2 ScreenSize = new(2000, 2000);

    private sealed record Harness(EcsContext EcsContext, UiLayerStack Layers, ElementPoolService ElementPool, DiagnosticsWindowController Controller, UiInputController Input);

    private static Harness BuildHarness(DiagnosticsEngine? diagnostics = null)
    {
        var ecsContext = BuiltInTestModules.Build(new Map(new Vector3Int(5, 5, 1)), initialEntityCapacity: 100, initialComponentCapacity: 50).EcsContext;
        var elementPool = TestElementPoolServiceFactory.Create(TestFonts.Shared, new LabelRenderer());
        elementPool.RegisterFactory<DiagnosticsWindow>(() => new DiagnosticsWindow(
            TestFonts.Shared, elementPool, new LabelRenderer(), ecsContext.EntityManager, ecsContext.ComponentManager.GetPackedPool<MovementComponent>(), ecsContext.SystemManager.Clock, diagnostics));
        var layers = new UiLayerStack();
        var controller = new DiagnosticsWindowController(elementPool, layers);
        var input = TestUiInputController.Create(layers, ScreenSize, ecsContext.ComponentManager, TestPlayerQuery.NoPlayer, new EventBus(), new Game.Modules.Inventory.ItemCatalog(), diagnosticsWindowController: controller);
        return new Harness(ecsContext, layers, elementPool, controller, input);
    }

    private static MouseState MouseAway => new(1999, 1999, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static void PressF3(Harness harness)
    {
        harness.Input.Update(F3Down, MouseAway);
        harness.Input.Update(NoKeys, MouseAway);
    }

    [TestMethod]
    public void F3_OpensTheWindow_ThenClosesIt()
    {
        var harness = BuildHarness();

        PressF3(harness);
        var window = harness.Controller.Window;
        Assert.IsNotNull(window);
        Assert.IsTrue(harness.Layers.Contains(UiLayer.DynamicHud, window));

        PressF3(harness);
        Assert.IsNull(harness.Controller.Window);
        Assert.IsFalse(harness.Layers.Contains(UiLayer.DynamicHud, window));
        Assert.IsFalse(harness.Layers.IsMenuModeExempt(window), "A closed window gives its exemption back before the pool reuses it.");
    }

    [TestMethod]
    public void F3_WhileATextBoxHasFocus_DoesNothing()
    {
        var harness = BuildHarness();
        var textBox = harness.ElementPool.CreateElement<TextBox>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(50, 50), Size = new Vector2(200, 30), DisplayMode = ElementDisplayMode.Fixed },
        });
        textBox.Initialize();
        harness.Layers.Add(UiLayer.DynamicHud, textBox);
        harness.Input.FocusElement(textBox);

        PressF3(harness);

        Assert.IsNull(harness.Controller.Window);
    }

    [TestMethod]
    public void F3_DuringMenuMode_OpensAWindowThatDoesNotHoldMenuModeOpen()
    {
        var harness = BuildHarness();
        var menuWindow = harness.ElementPool.CreateElement<Window>(null, new ElementOptions());
        menuWindow.Initialize();
        harness.Layers.Add(UiLayer.DynamicHud, menuWindow);
        harness.Layers.OpenMenuWindow(menuWindow);

        PressF3(harness);
        harness.Layers.CloseMenuWindow(menuWindow);

        Assert.IsNotNull(harness.Controller.Window);
        Assert.IsFalse(harness.Layers.IsMenuWindow(harness.Controller.Window));
        Assert.IsFalse(harness.Layers.IsMenuModeActive, "The simulation must not stay paused because the Diagnostics window is open.");
    }

    [TestMethod]
    public void Escape_ClosesTheWindow()
    {
        var harness = BuildHarness();
        PressF3(harness);

        harness.Input.Update(new KeyboardState(Keys.Escape), MouseAway);
        harness.Input.Update(NoKeys, MouseAway);

        Assert.IsNull(harness.Controller.Window);
    }

    private static float OpenAndMeasure(Harness harness)
    {
        PressF3(harness);
        harness.Controller.Window!.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1)));
        return harness.Controller.Window.ContentHeight;
    }

    [TestMethod]
    public void Update_WithLiveGauges_AddsTheFrameGraphAndARowPerGauge()
    {
        var heightWithoutGauges = OpenAndMeasure(BuildHarness());

        using var diagnostics = new DiagnosticsEngine(DiagnosticsFeatures.Gauges, randomSeed: 1, writesPeriodicReports: false);
        diagnostics.Start();
        var harness = BuildHarness(diagnostics);
        harness.EcsContext.BeginSession();
        for (var frame = 1; frame <= 3; frame++)
        {
            harness.EcsContext.Update(new EngineTime(TimeSpan.Zero, TimeSpan.Zero, IsRunningSlowly: false, FrameCount: frame));
        }

        var heightWithGauges = OpenAndMeasure(harness);

        var gaugeRowCount = diagnostics.LiveGauges!.Rows.Count - 1;
        Assert.IsGreaterThan(0f, heightWithoutGauges);
        Assert.IsGreaterThanOrEqualTo(heightWithoutGauges + DiagnosticsWindowChrome.FrameGraphHeight + gaugeRowCount * DiagnosticsWindowChrome.RowHeight, heightWithGauges);
        harness.EcsContext.Dispose();
    }

    [TestMethod]
    public void Update_WithMoreRowsThanFit_ScrollsByTheOverflow()
    {
        var harness = BuildHarness();
        var contentHeight = OpenAndMeasure(harness);
        var window = harness.Controller.Window!;

        window.SetSize(new Vector2(window.CurrentSize.X, contentHeight / 2));

        Assert.IsGreaterThan(0f, window.MaxScrollOffset.Y);
    }
}
