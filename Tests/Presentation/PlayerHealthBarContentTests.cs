using Engine.ECS.Systems;
using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Math;
using Game.Modules.Core.Components;
using Game.Modules.Health;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;
using Presentation.UI.Chrome;
using Presentation.UI.Content;
using System.Linq;

namespace Tests.Presentation;

/// <summary>
/// Drives the real Update/hover pipeline (PlayerHealthBarContent's internal MouseState/
/// screenBounds-seamed Update overload -- see its own doc comment) rather than calling a private
/// hover method directly, per this repo's own "live testing catches what code review misses"
/// lesson for hit-test/popup work. screenBounds is passed in as a synthetic Rectangle instead of
/// read from a real GraphicsDevice, which -- like HotbarContent's own Draw/Update and Folder's
/// icon rendering -- isn't constructible headlessly in this test environment (see those tests'
/// own doc comments); the position math itself (PopupPositioning) is pure geometry and unaffected.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PlayerHealthBarContentTests
{
    private const int PlayerEntityId = 1;
    private static readonly Rectangle ScreenBounds = new(0, 0, 1920, 1080);

    private static (PlayerHealthBarContent Content, Window HostWindow) Build(bool complexHealth, bool hasHealth = true)
    {
        var world = new Game.World.World(new Game.World.Map(new Vector3Int(20, 20, 1))) { PlayerEntityId = PlayerEntityId };
        var fontService = TestFonts.Shared;
        var layers = new UiLayerStack();
        var windowService = TestElementPoolServiceFactory.Create(fontService, new LabelRenderer());

        var componentManager = BuiltInTestComponents.RegisterAll(new ComponentManager(20, 10));

        componentManager.Merge(PlayerEntityId, new DisplayTextComponent("Player1", "This is you."));

        if (complexHealth)
        {
            BodyPartTestWorld.WithParts(componentManager, PlayerEntityId, ("Head", BodyPartType.Head, 10, 10, true), ("Torso", BodyPartType.Torso, 15, 20, true), ("Left Arm", BodyPartType.Arm, 8, 8, false), ("Right Arm", BodyPartType.Arm, 8, 8, false), ("Left Leg", BodyPartType.Leg, 4, 9, false), ("Right Leg", BodyPartType.Leg, 9, 9, false));
        }
        else if (hasHealth)
        {
            componentManager.Merge(PlayerEntityId, new SimpleHealthComponent(50, 100));
        }

        var content = new PlayerHealthBarContent(world, componentManager, BodyPartTestWorld.PartsOf(componentManager), fontService, layers);
        var hostWindow = windowService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = new Vector2(1700, 30),
                Size = PlayerHealthBarContent.Size,
                DisplayMode = ElementDisplayMode.Fixed,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, ShowTitle = false, CanUserFocus = false },
        });
        hostWindow.SetContent(content);
        hostWindow.Initialize();

        return (content, hostWindow);
    }

    private static MouseState MouseAt(Point position) => new(position.X, position.Y, 0, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released, ButtonState.Released);

    private static Point InsideBar(Window hostWindow)
    {
        var origin = hostWindow.ContentAbsolutePosition;
        var size = hostWindow.ContentSize;
        return new Point((int)(origin.X + size.X / 2f), (int)(origin.Y + size.Y / 2f));
    }

    private static readonly Point OutsideBar = new(-100, -100);

    [TestMethod]
    public void Update_SimpleHealth_ValueTextShowsCurrentOverMaximum()
    {
        var (content, _) = Build(complexHealth: false);

        content.Update(MouseAt(OutsideBar), ScreenBounds);

        Assert.AreEqual("50 / 100", content.ValueText);
    }

    [TestMethod]
    public void Update_ComplexHealth_ValueTextShowsBodyPartTotals()
    {
        var (content, _) = Build(complexHealth: true);

        content.Update(MouseAt(OutsideBar), ScreenBounds);

        Assert.AreEqual("54 / 64", content.ValueText);
    }

    [TestMethod]
    public void Update_NoHealth_NoValueText()
    {
        var (content, _) = Build(complexHealth: false, hasHealth: false);

        content.Update(MouseAt(OutsideBar), ScreenBounds);

        Assert.IsNull(content.ValueText);
    }

    [TestMethod]
    public void Update_HoveringBelowDelayThreshold_PopupStaysHidden()
    {
        var (content, hostWindow) = Build(complexHealth: true);
        var insidePoint = InsideBar(hostWindow);

        for (var frame = 0; frame < HudChrome.HoverTooltipDelayFrames - 1; frame++)
        {
            content.Update(MouseAt(insidePoint), ScreenBounds);
            Assert.IsFalse(content.HoverPopup.IsVisible, $"Popup must stay hidden before the delay threshold (frame {frame}).");
        }
    }

    [TestMethod]
    public void Update_HoveringAtDelayThreshold_PopupBecomesVisible()
    {
        var (content, hostWindow) = Build(complexHealth: true);
        var insidePoint = InsideBar(hostWindow);

        for (var frame = 0; frame < HudChrome.HoverTooltipDelayFrames; frame++)
        {
            content.Update(MouseAt(insidePoint), ScreenBounds);
        }

        Assert.IsTrue(content.HoverPopup.IsVisible, "Popup must be visible once the hover delay threshold is reached.");
    }

    [TestMethod]
    public void Update_HoverLostAfterShown_PopupHidesImmediately()
    {
        var (content, hostWindow) = Build(complexHealth: true);
        var insidePoint = InsideBar(hostWindow);

        for (var frame = 0; frame < HudChrome.HoverTooltipDelayFrames; frame++)
        {
            content.Update(MouseAt(insidePoint), ScreenBounds);
        }
        Assert.IsTrue(content.HoverPopup.IsVisible, "Sanity check: the popup must have shown first.");

        content.Update(MouseAt(OutsideBar), ScreenBounds);

        Assert.IsFalse(content.HoverPopup.IsVisible, "Losing hover must hide the popup on the very next frame, with no delay.");
    }

    [TestMethod]
    public void Update_HoveringPastDelay_PopupPositionedBelowBarWithinScreenBounds()
    {
        var (content, hostWindow) = Build(complexHealth: true);
        var insidePoint = InsideBar(hostWindow);

        for (var frame = 0; frame < HudChrome.HoverTooltipDelayFrames; frame++)
        {
            content.Update(MouseAt(insidePoint), ScreenBounds);
        }

        var barRectangle = hostWindow.ContentRectangle;
        var expectedPosition = PopupPositioning.GetPositionWithinBounds(barRectangle, content.HoverPopup.CurrentSize, PopupAnchor.South, new Vector2(0, 2), ScreenBounds);

        Assert.AreEqual(expectedPosition, content.HoverPopup.RelativePosition);
    }

    [TestMethod]
    public void BuildRows_ComplexHealthFixture_OneRowPerBodyPart_NoTotalRow()
    {
        var (content, _) = Build(complexHealth: true);
        var hoverContent = (PlayerHealthHoverContent)content.HoverPopup.Content!;

        var rows = new List<PlayerHealthHoverContent.RowData>();
        hoverContent.BuildRows(rows);

        Assert.HasCount(6, rows, "One row per body part -- no Total row, it's redundant with the big bar this popup is attached to.");

        // BuildRows enumerates BodyPartComponent in whatever order the MultiComponentPool's own
        // dense-index chain returns them -- not insertion order -- so only set membership is
        // checked here, not positional order.
        var actualPartNames = rows.Select(row => row.Name).ToHashSet();
        HashSet<string> expectedPartNames = ["Head", "Torso", "Left Arm", "Right Arm", "Left Leg", "Right Leg"];
        Assert.IsTrue(actualPartNames.SetEquals(expectedPartNames), $"Expected {{{string.Join(", ", expectedPartNames)}}}, got {{{string.Join(", ", actualPartNames)}}}.");
    }

    [TestMethod]
    public void BuildRows_ComplexHealthFixture_EachRowFractionMatchesItsOwnPart()
    {
        var (content, _) = Build(complexHealth: true);
        var hoverContent = (PlayerHealthHoverContent)content.HoverPopup.Content!;

        var rows = new List<PlayerHealthHoverContent.RowData>();
        hoverContent.BuildRows(rows);

        var headRow = rows.Single(row => row.Name == "Head");
        Assert.AreEqual(1f, headRow.Fraction, 0.0001f, "Head fixture: 10/10.");

        var torsoRow = rows.Single(row => row.Name == "Torso");
        Assert.AreEqual(0.75f, torsoRow.Fraction, 0.0001f, "Torso fixture: 15/20.");
    }

    [TestMethod]
    public void BuildRows_MaximumHealthBuffActive_FractionComputedAgainstEffectiveMaximumNotRaw()
    {
        // A part sitting at its raw maximum (10/10 -- would read 100% by that measure) must still
        // read below 100% once a +50% MaximumHealth buff makes its true cap 15 -- regression for
        // the same bug ComplexHealthHeal/BodyPartSelection.PickLowestPercentage had.
        var bodyParts = BodyPartTestWorld.WithParts(PlayerEntityId, ("Head", BodyPartType.Head, 10, 10, true)).BodyParts;
        var statModifiers = new MultiComponentPool<StatModifierComponent>(entityCapacity: 10, initialCapacity: 4);
        statModifiers.Add(PlayerEntityId, new StatModifierComponent(StatModifierTarget.MaximumHealth, StatModifierOperation.Multiplicative, StatModifierPolarity.Buff,
            canModify: true, magnitude: 0.5f, expiresAtFrame: FrameDeadline.Never, ActionSource.Admin));
        var world = new Game.World.World(new Game.World.Map(new Vector3Int(5, 5, 1))) { PlayerEntityId = PlayerEntityId };
        var hoverContent = new PlayerHealthHoverContent(world, bodyParts, TestFonts.Shared, statModifiers);

        var rows = new List<PlayerHealthHoverContent.RowData>();
        hoverContent.BuildRows(rows);

        Assert.AreEqual(10f / 15f, rows.Single().Fraction, 0.0001f);
    }

    [TestMethod]
    public void BuildRows_SimpleHealthFixture_NoRows()
    {
        var (content, _) = Build(complexHealth: false);
        var hoverContent = (PlayerHealthHoverContent)content.HoverPopup.Content!;

        var rows = new List<PlayerHealthHoverContent.RowData>();
        hoverContent.BuildRows(rows);

        Assert.IsEmpty(rows, "A Simple-health entity has no body parts to enumerate, and there's no Total row to fall back to.");
    }
}
