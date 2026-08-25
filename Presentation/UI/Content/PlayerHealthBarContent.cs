using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
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
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// Permanent top-right HUD readout for the player's health -- unlike MapWindow's per-entity
/// tile bars (which hide at full health), this always renders since it's a persistent HUD
/// element, not a transient overlay. Hosted via IWindowContent/SetContent (see
/// ShellBootstrapper), the same pattern DebugWindowContent uses, rather than living inside
/// MapWindow -- it belongs to the HUD tier (screen-absolute coordinates), not the map's own
/// local content-viewport space. Light grey full-width fill when the player has no
/// SimpleHealthComponent at all -- reserved for a future temporarily/permanently-immortal player
/// state, rather than hiding the bar outright.
/// </summary>
/// <remarks>
/// Also self-polls Mouse.GetState() every Update (see UpdateHover), the same idiom
/// AbilityScoreWindow uses for its own hover popup, to drive a per-body-part breakdown popup
/// (PlayerHealthHoverContent) -- owns and drives that popup Window directly (created in
/// Initialize, added to UiLayer.Tooltip), mirroring HotbarController's own _summaryWindow
/// pattern.
/// </remarks>
public sealed class PlayerHealthBarContent(World world, ComponentManager componentManager, FontService fontService, UiLayerStack layers) : IElementContent
{
    public static readonly Vector2 Size = new(HudMetrics.EntrySize.X * 4.5f, HudMetrics.EntrySize.Y * 0.75f);

    /// <summary>Matches ShowBorder's default (1,1) BorderSize, doubled -- see Element.BorderInsetDoubled. The popup's row content itself reads its host window's actual resolved ContentSize at draw time, so this only needs to be a reasonable approximation, not pixel-exact.</summary>
    private static readonly Vector2 PopupBorderInsetDoubled = new(2, 2);

    /// <summary>Popup sits directly below the bar (PopupAnchor.South) -- the bar sits in the top-right HUD corner, so North would risk clipping off the top of the screen.</summary>
    private static readonly Vector2 PopupGap = new(0, 2);

    private readonly PackedComponentPool<SimpleHealthComponent> _healthPool = componentManager.GetPackedPool<SimpleHealthComponent>();
    private readonly MultiComponentPool<BodyPartComponent> _bodyParts = componentManager.GetMultiPool<BodyPartComponent>();

    // Optional -- see StatModifierMath.GetEffectiveValue's own doc comment for why a null pool
    // (StatModifiersModule not registered) is treated the same as "no active modifiers."
    private readonly MultiComponentPool<StatModifierComponent>? _statModifiers = componentManager.IsRegistered<StatModifierComponent>()
        ? componentManager.GetMultiPool<StatModifierComponent>()
        : null;

    private Window _hostWindow = null!;
    private Window _hoverPopup = null!;

    private bool _hasHealth;
    private float _healthFraction = 1f;

    private int _hoveredFrames;

    /// <summary>Test-only seam onto the popup this content owns/drives -- see the internal Update overload below for why the real screen-bounds source (ElementPoolService.GraphicsDevice, unavailable headlessly) is also parameterized out for tests.</summary>
    internal Window HoverPopup => _hoverPopup;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;

        _hoverPopup = hostWindow.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = Vector2.Zero, // Repositioned every frame by UpdateHover once hovered.
                Size = PlayerHealthHoverContent.ContentSize + PopupBorderInsetDoubled,
                DisplayMode = ElementDisplayMode.Fixed,
                IsVisible = false,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, ShowTitle = false, CanUserFocus = false, CanUserClose = false },
        });
        _hoverPopup.SetContent(new PlayerHealthHoverContent(world, _bodyParts, fontService, _statModifiers));
        _hoverPopup.Initialize();
        layers.Add(UiLayer.Tooltip, _hoverPopup);
    }

    /// <summary>Whether the player currently has a SimpleHealthComponent and what fraction of effective max health remains is decided here -- Draw only reads the cached fraction and maps it to a fill color/width.</summary>
    public void Update(GameTime gameTime) => Update(gameTime, Mouse.GetState(), _hostWindow.ElementPoolService.GraphicsDevice.Viewport.Bounds);

    /// <summary>
    /// Takes explicit MouseState/screenBounds rather than reading Mouse.GetState()/
    /// ElementPoolService.GraphicsDevice itself -- the same seam UiInputController's own
    /// dual-overload Update uses, extended to both of this method's real-environment reads (not
    /// just MouseState) so a test can drive the real hover/positioning pipeline with synthetic
    /// values instead of calling a private hover method directly or requiring a real
    /// GraphicsDevice (unavailable headlessly -- see WindowGlowTests/FolderTests/
    /// HotbarContentTests' own doc comments for the same constraint elsewhere in this codebase).
    /// </summary>
    internal void Update(GameTime gameTime, MouseState mouseState, Rectangle screenBounds)
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0 || !HealthQueries.TryGetTotals(_healthPool, _bodyParts, playerEntityId, out var currentHealth, out var maximumHealth) || maximumHealth <= 0)
        {
            _hasHealth = false;
            _healthFraction = 1f;
        }
        else
        {
            _hasHealth = true;
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(_statModifiers, playerEntityId, StatModifierTarget.MaximumHealth, maximumHealth);
            _healthFraction = effectiveMaximumHealth > 0
                ? MathHelper.Clamp(currentHealth / effectiveMaximumHealth, 0f, 1f)
                : 1f;
        }

        UpdateHover(mouseState, screenBounds);
    }

    /// <summary>Delay-gated via HudMetrics.HoverTooltipDelayFrames before showing (mirrors AbilityScoreWindow.UpdateHover), but hides immediately with no delay on hover loss.</summary>
    private void UpdateHover(MouseState mouseState, Rectangle screenBounds)
    {
        var barRectangle = BarRectangle();
        var isHovering = barRectangle.Contains(new Point(mouseState.X, mouseState.Y));
        _hoveredFrames = isHovering ? _hoveredFrames + 1 : 0;

        if (!isHovering || _hoveredFrames < HudMetrics.HoverTooltipDelayFrames)
        {
            _hoverPopup.IsVisible = false;
            return;
        }

        _hoverPopup.SetRelativePosition(PopupPositioning.GetPositionWithinBounds(barRectangle, _hoverPopup.CurrentSize, PopupAnchor.South, PopupGap, screenBounds));
        _hoverPopup.IsVisible = true;
    }

    private Rectangle BarRectangle()
    {
        var origin = _hostWindow.ContentAbsolutePosition;
        var contentSize = _hostWindow.ContentSize;
        return new Rectangle((int)origin.X, (int)origin.Y, (int)contentSize.X, (int)contentSize.Y);
    }

    public void DrawContent(GameTime gameTime)
    {
        var spriteBatch = _hostWindow.ElementPoolService.SpriteBatch;
        var unitRectangle = _hostWindow.ElementPoolService.UnitRectangle;

        // BarRectangle(), not the static Size -- Size is the window's outer bounds (used by
        // ShellBootstrapper to position/size the host window itself); the actual drawable
        // area is whatever's left after its border insets that, so the bar has to size itself
        // off ContentSize to fit inside the border rather than drawing over it.
        ResourceBarRenderer.Draw(spriteBatch, unitRectangle, BarRectangle(), _healthFraction, _hasHealth, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
    }
}
