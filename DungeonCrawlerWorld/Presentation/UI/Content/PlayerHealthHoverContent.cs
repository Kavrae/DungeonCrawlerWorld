using FontStashSharp;
using Game.Modules.Health;
using Game.Modules.StatModifiers;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>
/// PlayerHealthBarContent's hover popup: one row per body part, name plus its own live
/// pixel-drawn bar via ResourceBarRenderer, the same renderer the big HUD bar itself uses. No
/// Total row -- redundant with the big bar it's popped up from.
/// </summary>
/// <remarks>
/// Sized/shown/hidden externally by PlayerHealthBarContent -- this only draws whatever the host
/// popup window's own resolved ContentSize/ContentAbsolutePosition currently are, recomputed
/// fresh every DrawContent call rather than cached, since health can change (regen/damage/heals)
/// while the popup is being hovered.
/// </remarks>
public sealed class PlayerHealthHoverContent(
    World world,
    EntityBodyParts bodyParts,
    FontService fontService,
    StatModifierView statModifierView) : IElementContent
{
    private const float RowHeight = 16f;
    private const float RowContentWidth = 170f;
    private const float BarWidth = 60f;
    private const float BarHeight = 8f;
    private const float NameBarGap = 6f;

    /// <summary>The popup's content-area size for rowCount rows -- PlayerHealthBarContent adds its host window's border inset on top of this when sizing the popup window itself.</summary>
    public static Vector2 ContentSizeFor(int rowCount) => new(RowContentWidth, RowHeight * rowCount);

    private readonly List<RowData> _scratchRows = [];

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont(GameFontChrome.PlayerHealthHoverFontSize);
    }

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime)
    {
        BuildRows(_scratchRows);
        if (_scratchRows.Count == 0)
        {
            return;
        }

        var spriteBatch = _hostWindow.Render.Batch;
        var unitRectangle = _hostWindow.Render.UnitRectangle;
        var origin = Vector2.Zero;
        var contentSize = _hostWindow.ContentSize;

        for (var rowIndex = 0; rowIndex < _scratchRows.Count; rowIndex++)
        {
            var row = _scratchRows[rowIndex];
            DrawRow(spriteBatch, unitRectangle, origin, contentSize.X, RowHeight, rowIndex, row.Name, row.Fraction, row.HasResource);
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
            var effectiveMaximumHealth = statModifierView.GetEffectiveValue(playerEntityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            var partFraction = effectiveMaximumHealth > 0 ? MathHelper.Clamp(part.CurrentHealth / effectiveMaximumHealth, 0f, 1f) : 0f;
            destination.Add(new RowData(part.Name, partFraction, effectiveMaximumHealth > 0));
        }
    }

    internal readonly record struct RowData(string Name, float Fraction, bool HasResource);

    private void DrawRow(SpriteEffectBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, float contentWidth, float rowHeight, int rowIndex, string name, float fraction, bool hasResource)
    {
        var rowTop = origin.Y + rowIndex * rowHeight;
        var barX = origin.X + contentWidth - BarWidth;
        var nameWidth = System.Math.Max(0f, barX - origin.X - NameBarGap);

        LabelRenderer.DrawLeftAligned(spriteBatch, _font, name, new Vector2(origin.X, rowTop), new Vector2(nameWidth, rowHeight), Color.White);

        var barY = rowTop + (rowHeight - BarHeight) / 2f;
        var barRectangle = new Rectangle((int)barX, (int)barY, (int)BarWidth, (int)BarHeight);
        ResourceBarRenderer.Draw(spriteBatch, unitRectangle, barRectangle, fraction, hasResource, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
    }
}
