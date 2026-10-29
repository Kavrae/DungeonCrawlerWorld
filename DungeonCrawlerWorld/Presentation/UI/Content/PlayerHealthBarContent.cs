using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;
using UIEngine.UI.Chrome;

namespace Presentation.UI.Content;

/// <summary>
/// Permanent top-right HUD readout for the player's health -- unlike MapWindow's per-entity
/// tile bars (which hide at full health), this always renders since it's a persistent HUD
/// element, not a transient overlay. Hosted via IWindowContent/SetContent (see
/// ShellBootstrapper), the same pattern NotificationCenter's summary window uses, rather than living inside
/// MapWindow -- it belongs to the HUD tier (screen-absolute coordinates), not the map's own
/// local content-viewport space. Light grey full-width fill when the player has no
/// SimpleHealthComponent at all -- reserved for a future temporarily/permanently-immortal player
/// state, rather than hiding the bar outright.
/// </summary>
/// <remarks>
/// While the pointer is over its window (Element.IsPointerOver), it drives a per-body-part breakdown popup
/// (PlayerHealthHoverContent) -- owns and drives that popup Window directly (created in
/// Initialize, added to UiLayer.Tooltip), mirroring HotbarController's own _summaryWindow
/// pattern.
/// </remarks>
public sealed class PlayerHealthBarContent(World world, HealthView healthView, EntityBodyParts bodyParts, StatModifierView statModifierView, FontService fontService, UiLayerStack layers) : IElementContent
{
    public static readonly Vector2 Size = new(HudChrome.EntrySize.X * 4.5f, HudChrome.EntrySize.Y * 0.75f);

    /// <summary>Matches ShowBorder's default (1,1) BorderSize, doubled -- see Element.BorderInsetDoubled. The popup's row content itself reads its host window's actual resolved ContentSize at draw time, so this only needs to be a reasonable approximation, not pixel-exact.</summary>
    private static readonly Vector2 PopupBorderInsetDoubled = new(2, 2);

    /// <summary>Popup sits directly below the bar (PopupAnchor.South) -- the bar sits in the top-right HUD corner, so North would risk clipping off the top of the screen.</summary>
    private static readonly Vector2 PopupGap = new(0, 2);

    private readonly EntityBodyParts _bodyParts = bodyParts;

    private Window _hostWindow = null!;
    private Window _hoverPopup = null!;

    /// <summary>The "current / maximum" text drawn, rebuilt only when either number changes.</summary>
    private readonly KeyedCache<(int Current, int Maximum), string> _valueTextCache = new();
    private string _valueText = string.Empty;
    private readonly TextSizeCache _valueTextSize = new();

    private bool _hasHealth;
    private float _healthFraction = 1f;

    private int _hoveredFrames;

    /// <summary>The body-part count the popup is sized for, so it is resized only when the count changes.</summary>
    private int _popupRowCount;

    /// <summary>Test-only seam onto the popup this content owns/drives -- see the internal Update overload below for why the real screen-bounds source (Render.GraphicsDevice, unavailable headlessly) is also parameterized out for tests.</summary>
    internal Window HoverPopup => _hoverPopup;

    /// <summary>The "current / maximum" text the bar draws, or null when the player has no health to show.</summary>
    internal string? ValueText => _hasHealth ? _valueText : null;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;

        _hoverPopup = hostWindow.ElementPoolService.CreateElement<Window>(null, new ElementOptions
        {
            Layout = new ElementLayoutOptions
            {
                RelativePosition = Vector2.Zero, // Repositioned every frame by UpdateHover once hovered.
                Size = PlayerHealthHoverContent.ContentSizeFor(_popupRowCount) + PopupBorderInsetDoubled,
                DisplayMode = ElementDisplayMode.Fixed,
                IsVisible = false,
            },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, ShowTitle = false, CanUserFocus = false, CanUserClose = false },
        });
        _hoverPopup.SetContent(new PlayerHealthHoverContent(world, _bodyParts, fontService, statModifierView));
        _hoverPopup.Initialize();
        layers.Add(UiLayer.Tooltip, _hoverPopup);
    }

    /// <summary>Whether the player currently has a SimpleHealthComponent and what fraction of effective max health remains is decided here -- Draw only reads the cached fraction and maps it to a fill color/width.</summary>
    public void Update(GameTime gameTime) => Update(_hostWindow.Render.GraphicsDevice.Viewport.Bounds);

    /// <summary>
    /// Takes screenBounds rather than reading Render.GraphicsDevice itself, so a test can drive the
    /// real hover/positioning pipeline without a real GraphicsDevice (unavailable headlessly -- see
    /// WindowGlowTests/FolderTests/HotbarContentTests' own doc comments for the same constraint
    /// elsewhere in this codebase).
    /// </summary>
    internal void Update(Rectangle screenBounds)
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0 || !healthView.TryGetTotals(playerEntityId, out var currentHealth, out var maximumHealth) || maximumHealth <= 0)
        {
            _hasHealth = false;
            _healthFraction = 1f;
        }
        else
        {
            _hasHealth = true;
            var effectiveMaximumHealth = statModifierView.GetEffectiveValue(playerEntityId, StatModifierTarget.MaximumHealth, maximumHealth);
            _healthFraction = effectiveMaximumHealth > 0
                ? MathHelper.Clamp(currentHealth / effectiveMaximumHealth, 0f, 1f)
                : 1f;
            _valueText = _valueTextCache.Get((ResourceBarRounding.DisplayedHealth(currentHealth), ResourceBarRounding.DisplayedMaximum(effectiveMaximumHealth)), static key => ResourceBarValueText.Format(key.Current, key.Maximum));
        }

        UpdateHover(screenBounds);
    }

    /// <summary>Delay-gated via HudChrome.HoverTooltipDelayFrames before showing (mirrors AbilityScoreWindow.UpdateHover), but hides immediately with no delay on hover loss.</summary>
    private void UpdateHover(Rectangle screenBounds)
    {
        var barRectangle = BarRectangle();
        var isHovering = _hostWindow.IsPointerOver;
        _hoveredFrames = isHovering ? _hoveredFrames + 1 : 0;

        if (!isHovering || _hoveredFrames < HudChrome.HoverTooltipDelayFrames)
        {
            _hoverPopup.IsVisible = false;
            return;
        }

        var rowCount = _bodyParts.Count(world.PlayerEntityId);
        if (rowCount != _popupRowCount)
        {
            _popupRowCount = rowCount;
            var popupSize = PlayerHealthHoverContent.ContentSizeFor(rowCount) + PopupBorderInsetDoubled;
            _hoverPopup.SetMaximumSize(popupSize);
            _hoverPopup.SetSize(popupSize);
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
        var spriteBatch = _hostWindow.Render.Batch;
        var unitRectangle = _hostWindow.Render.UnitRectangle;

        // BarRectangle(), not the static Size -- Size is the window's outer bounds (used by
        // ShellBootstrapper to position/size the host window itself); the actual drawable
        // area is whatever's left after its border insets that, so the bar has to size itself
        // off ContentSize to fit inside the border rather than drawing over it.
        var barRectangle = _hostWindow.ToContentLocal(BarRectangle());
        ResourceBarRenderer.Draw(spriteBatch, unitRectangle, barRectangle, _healthFraction, _hasHealth, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);

        if (_hasHealth)
        {
            var valueFont = fontService.GetFont((int)(barRectangle.Height * FontChrome.ResourceBarValueFontFraction));
            ResourceBarRenderer.DrawCenteredValueText(spriteBatch, valueFont, barRectangle, _valueText, _valueTextSize.SizeOf(valueFont, _valueText).X);
        }
    }
}
