using FontStashSharp;
using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>
/// PlayerHealthBarContent's hover popup: the player's natural regeneration rate, then one row per
/// body part, name plus its own live pixel-drawn bar via ResourceBarRenderer, the same renderer the
/// big HUD bar itself uses. No Total row -- redundant with the big bar it's popped up from.
/// </summary>
/// <remarks>
/// Sized/shown/hidden externally by PlayerHealthBarContent -- this only draws whatever the host
/// popup window's own resolved ContentSize currently is, recomputed fresh every DrawContent call
/// rather than cached, since health can change (regen/damage/heals) while the popup is being hovered.
/// </remarks>
public sealed class PlayerHealthHoverContent(
    World world,
    HealthView healthView,
    EntityBodyParts bodyParts,
    FontService fontService,
    StatModifierView statModifierView) : IElementContent
{
    private const float RowHeight = 16f;
    private const float RowContentWidth = 170f;
    private const float BarWidth = 60f;
    private const float BarHeight = 8f;
    private const float NameBarGap = 6f;

    /// <summary>The popup's content-area size for the regeneration row plus bodyPartCount body-part rows.</summary>
    public static Vector2 ContentSizeFor(int bodyPartCount) => new(RowContentWidth, RowHeight * (bodyPartCount + 1));

    private readonly List<RowData> _scratchRows = [];

    /// <summary>The regeneration row's text, rebuilt only when the rate shown changes.</summary>
    private readonly KeyedCache<int, string> _regenerationTextCache = new();

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont(GameFontChrome.PlayerHealthHoverFontSize);
    }

    public void Update(GameTime gameTime) { }

    /// <summary>The regeneration row's text, or null when there is no player.</summary>
    internal string? RegenerationText()
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0)
        {
            return null;
        }

        var tenths = ResourceRegenerationText.DisplayedTenths(healthView.RegenerationPerSecond(playerEntityId));
        return _regenerationTextCache.Get(tenths, static key => ResourceRegenerationText.Format(key));
    }

    public void DrawContent(GameTime gameTime)
    {
        var regenerationText = RegenerationText();
        if (regenerationText is null)
        {
            return;
        }

        var spriteBatch = _hostWindow.Render.Batch;
        var contentWidth = _hostWindow.ContentSize.X;

        LabelRenderer.DrawLeftAligned(spriteBatch, _font, regenerationText, Vector2.Zero, new Vector2(contentWidth, RowHeight), HealthBarPalette.RegenerationTextColor);

        BuildRows(_scratchRows);
        for (var rowIndex = 0; rowIndex < _scratchRows.Count; rowIndex++)
        {
            var row = _scratchRows[rowIndex];
            DrawBodyPartRow(spriteBatch, contentWidth, (rowIndex + 1) * RowHeight, row.Name, row.Fraction, row.HasResource);
        }
    }

    /// <summary>One row per BodyPartComponent the player owns, in whatever order the MultiComponentPool's own chain enumerates them (no re-sorting) -- empty if the player has none (e.g. a hypothetical Simple-health player). Pure data, no rendering -- split out from DrawContent so a test can assert row assembly headlessly, without a GraphicsDevice-backed SpriteBatch to actually draw with.</summary>
    internal void BuildRows(List<RowData> destination)
    {
        destination.Clear();

        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0)
        {
            return;
        }

        foreach (var part in bodyParts.Parts(playerEntityId))
        {
            var effectiveMaximumHealth = statModifierView.GetEffectiveValue(playerEntityId, StatModifierTarget.MaximumHealth, part.BaseMaximumHealth);
            var partFraction = effectiveMaximumHealth > 0 ? MathHelper.Clamp(part.CurrentHealth / effectiveMaximumHealth, 0f, 1f) : 0f;
            destination.Add(new RowData(part.Name, partFraction, effectiveMaximumHealth > 0));
        }
    }

    internal readonly record struct RowData(string Name, float Fraction, bool HasResource);

    private void DrawBodyPartRow(SpriteEffectBatch spriteBatch, float contentWidth, float rowTop, string name, float fraction, bool hasResource)
    {
        var barX = contentWidth - BarWidth;
        var nameWidth = System.Math.Max(0f, barX - NameBarGap);

        LabelRenderer.DrawLeftAligned(spriteBatch, _font, name, new Vector2(0f, rowTop), new Vector2(nameWidth, RowHeight), Color.White);

        var barY = rowTop + (RowHeight - BarHeight) / 2f;
        var barRectangle = new Rectangle((int)barX, (int)barY, (int)BarWidth, (int)BarHeight);
        ResourceBarRenderer.Draw(spriteBatch, barRectangle, fraction, hasResource, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
    }
}
