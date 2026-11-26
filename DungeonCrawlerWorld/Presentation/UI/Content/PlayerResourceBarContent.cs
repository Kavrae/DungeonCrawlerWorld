using Game.Modules.Health;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>A permanent top-right HUD bar for one of the player's resources -- health or mana -- with its "current / maximum" text and a hover popup.</summary>
/// <remarks>
/// Unlike the map's per-entity bars, which hide at full health, it always draws. It is HUD-tier content in
/// screen-absolute coordinates, hosted through IElementContent (see ShellBootstrapper), not part of MapWindow. A player
/// without the resource -- no health component, or no mana pool yet (ManaGrant) -- draws a light grey full bar and no
/// text rather than hiding the bar. What differs per resource is its IPlayerResourceBarSource: how its totals are read,
/// how the current value rounds for display, its palette and its hover popup's content.
/// </remarks>
public sealed class PlayerResourceBarContent : IElementContent
{
    public static readonly Vector2 Size = new(HudChrome.EntrySize.X * 4.5f, HudChrome.EntrySize.Y * 0.75f);

    private readonly World _world;
    private readonly IPlayerResourceBarSource _source;
    private readonly UiLayerStack _layers;
    private readonly ResourceBarValueLabel _valueLabel = new();

    private Window _hostWindow = null!;
    private ResourceBarHoverPopup _hoverPopup = null!;

    private bool _hasResource;
    private float _fraction = 1f;

    private PlayerResourceBarContent(World world, IPlayerResourceBarSource source, UiLayerStack layers)
    {
        _world = world;
        _source = source;
        _layers = layers;
    }

    /// <summary>The player's health bar; its popup lists the regeneration rate and every body part.</summary>
    public static PlayerResourceBarContent ForHealth(World world, HealthView healthView, EntityBodyParts bodyParts, StatModifierView statModifierView, FontService fontService, UiLayerStack layers) =>
        new(world, new HealthSource(healthView, bodyParts, new PlayerHealthHoverContent(world, healthView, bodyParts, fontService, statModifierView)), layers);

    /// <summary>The player's mana bar; its popup shows the regeneration rate.</summary>
    public static PlayerResourceBarContent ForMana(World world, ActionStateView actionStateView, FontService fontService, UiLayerStack layers) =>
        new(world, new ManaSource(actionStateView, new PlayerManaHoverContent(world, actionStateView, fontService)), layers);

    /// <summary>Test-only seam onto the popup this content drives.</summary>
    internal Window HoverPopup => _hoverPopup.Window;

    /// <summary>The "current / maximum" text the bar draws, or null when the player has none of the resource.</summary>
    internal string? ValueText => _valueLabel.Text;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _hoverPopup = new ResourceBarHoverPopup(hostWindow, _layers, _source.HoverContent, _source.HoverContentSize(-1));
    }

    public void Update(GameTime gameTime) => Update(_hostWindow.Render.GraphicsDevice.Viewport.Bounds);

    /// <summary>Reads the player's totals into the fraction and text Draw uses, then drives the hover popup.</summary>
    /// <remarks>Takes screenBounds rather than reading Render.GraphicsDevice, so a test can drive the real hover pipeline headlessly.</remarks>
    internal void Update(Rectangle screenBounds)
    {
        var playerEntityId = _world.PlayerEntityId;
        if (playerEntityId >= 0 && _source.TryGetTotals(playerEntityId, out var current, out var maximum) && maximum > 0)
        {
            _hasResource = true;
            _fraction = MathHelper.Clamp(current / maximum, 0f, 1f);
            _valueLabel.Show((_source.DisplayedCurrent(current), ResourceBarRounding.DisplayedMaximum(maximum)));
        }
        else
        {
            _hasResource = false;
            _fraction = 1f;
            _valueLabel.Hide();
        }

        _hoverPopup.Update(_hostWindow.IsPointerOver, _hostWindow.ContentRectangle, _source.HoverContentSize(playerEntityId), screenBounds);
    }

    /// <summary>Draws inside the host's border (its content rectangle), not across the static Size the host window is given.</summary>
    public void DrawContent(GameTime gameTime)
    {
        var batch = _hostWindow.Render.Batch;
        var bar = _hostWindow.LocalContentRectangle;

        ResourceBarRenderer.Draw(batch, bar, _fraction, _hasResource, _source.OutlineColor, _source.FractionColor);
        _valueLabel.Draw(batch, _hostWindow.FontService, bar);
    }

    /// <summary>What a PlayerResourceBarContent reads and shows for one resource.</summary>
    private interface IPlayerResourceBarSource
    {
        Color OutlineColor { get; }

        IElementContent HoverContent { get; }

        bool TryGetTotals(int playerEntityId, out float current, out float maximum);

        /// <summary>The current value as the bar shows it (ResourceBarRounding).</summary>
        int DisplayedCurrent(float current);

        Color FractionColor(float fraction);

        /// <param name="playerEntityId">The player, or negative when there is none.</param>
        Vector2 HoverContentSize(int playerEntityId);
    }

    private sealed class HealthSource(HealthView healthView, EntityBodyParts bodyParts, PlayerHealthHoverContent hoverContent) : IPlayerResourceBarSource
    {
        public Color OutlineColor => HealthBarPalette.OutlineColor;

        public IElementContent HoverContent => hoverContent;

        public bool TryGetTotals(int playerEntityId, out float current, out float maximum) => healthView.TryGetTotals(playerEntityId, out current, out maximum);

        public int DisplayedCurrent(float current) => ResourceBarRounding.DisplayedHealth(current);

        public Color FractionColor(float fraction) => HealthBarPalette.FractionColor(fraction);

        public Vector2 HoverContentSize(int playerEntityId) => PlayerHealthHoverContent.ContentSizeFor(playerEntityId < 0 ? 0 : bodyParts.Count(playerEntityId));
    }

    private sealed class ManaSource(ActionStateView actionStateView, PlayerManaHoverContent hoverContent) : IPlayerResourceBarSource
    {
        public Color OutlineColor => ManaBarPalette.OutlineColor;

        public IElementContent HoverContent => hoverContent;

        public bool TryGetTotals(int playerEntityId, out float current, out float maximum) => actionStateView.TryGetManaTotals(playerEntityId, out current, out maximum);

        public int DisplayedCurrent(float current) => ResourceBarRounding.DisplayedMana(current);

        public Color FractionColor(float fraction) => ManaBarPalette.FractionColor(fraction);

        public Vector2 HoverContentSize(int playerEntityId) => PlayerManaHoverContent.ContentSize;
    }
}
