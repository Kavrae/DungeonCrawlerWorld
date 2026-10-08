using Engine.ECS.Components;
using Engine.Events;
using Game.Modules.Actions;
using Game.Modules.Actions.Components;
using Game.Modules.Inventory;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Input;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Content;

namespace Tests.Presentation;

/// <summary>A Left Alt tap, through UiInputController, switches the player's targeting mode and shows it at the cursor -- but not mid-windup, where it says why instead.</summary>
[TestClass]
[DoNotParallelize]
public sealed class TargetingModeSwitchTests
{
    private const int PlayerEntityId = 1;

    private static readonly KeyboardState NoKeys = new();
    private static readonly KeyboardState LeftAltDown = new(Keys.LeftAlt);
    private static readonly Vector2 ScreenSize = new(2000, 2000);
    private static readonly MouseState MouseAway = new(1999, 1999, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private sealed record Harness(MapViewState MapViewState, CursorTextContent CursorText, UiInputController Input, ComponentManager Components);

    private static Harness BuildHarness()
    {
        var components = BuiltInTestComponents.RegisterAll(new ComponentManager(initialEntityCapacity: 16, initialComponentCapacity: 16));
        var mapViewState = new MapViewState();
        var pointerState = new PointerState();
        var cursorText = new CursorTextContent(pointerState, TestFonts.Shared, new LabelRenderer());
        var playerQuery = new TestPlayerQuery(PlayerEntityId);
        var modeSwitch = new TargetingModeSwitch(mapViewState, TestActionStateViews.Over(components), playerQuery, cursorText);
        var input = TestUiInputController.Create(new UiLayerStack(), ScreenSize, components, playerQuery, new EventBus(), new ItemCatalog(), mapViewState: mapViewState, pointerState: pointerState, targetingModeSwitch: modeSwitch);
        return new Harness(mapViewState, cursorText, input, components);
    }

    private static void TapLeftAlt(Harness harness)
    {
        harness.Input.Update(LeftAltDown, MouseAway);
        harness.Input.Update(NoKeys, MouseAway);
    }

    [TestMethod]
    public void StartsInTargetMode()
    {
        Assert.AreEqual(TargetingMode.Target, BuildHarness().MapViewState.TargetingMode);
    }

    [TestMethod]
    public void LeftAltTap_SwitchesTheMode_AndShowsTheNewOneAtTheCursor()
    {
        var harness = BuildHarness();

        TapLeftAlt(harness);

        Assert.AreEqual(TargetingMode.Ground, harness.MapViewState.TargetingMode);
        Assert.AreEqual(TargetingModeSwitch.TextFor(TargetingMode.Ground), harness.CursorText.ShownText);

        TapLeftAlt(harness);

        Assert.AreEqual(TargetingMode.Target, harness.MapViewState.TargetingMode);
        Assert.AreEqual(TargetingModeSwitch.TextFor(TargetingMode.Target), harness.CursorText.ShownText);
    }

    [TestMethod]
    public void HeldLeftAlt_SwitchesOnce()
    {
        var harness = BuildHarness();

        harness.Input.Update(LeftAltDown, MouseAway);
        harness.Input.Update(LeftAltDown, MouseAway);
        harness.Input.Update(LeftAltDown, MouseAway);

        Assert.AreEqual(TargetingMode.Ground, harness.MapViewState.TargetingMode);
    }

    [TestMethod]
    public void DuringAWindup_RefusesAndSaysWhy()
    {
        var harness = BuildHarness();
        harness.Components.Merge(PlayerEntityId, PendingWindupComponent.ForAction(Guid.NewGuid(), TestSelections.At(default), readyAtFrame: 60));

        TapLeftAlt(harness);

        Assert.AreEqual(TargetingMode.Target, harness.MapViewState.TargetingMode);
        Assert.AreEqual(TargetingModeSwitch.RefusedDuringWindupText, harness.CursorText.ShownText);
    }
}
