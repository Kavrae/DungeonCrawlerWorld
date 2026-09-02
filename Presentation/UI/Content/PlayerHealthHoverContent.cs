using Engine.ECS.Components.Stores;
using FontStashSharp;
using Game.Modules.Health.Components;
using Game.Modules.StatModifiers;
using Game.Modules.StatModifiers.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

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
    MultiComponentPool<BodyPartComponent> bodyParts,
    FontService fontService,
    MultiComponentPool<StatModifierComponent>? statModifiers = null) : IElementContent
{
    /// <summary>Up to 6 body parts -- the player is always the Human race today (see PlayerHealthBarContent's own doc comment), so this doesn't need to grow/shrink with the entity's actual part count.</summary>
    public const int MaxRowCount = 6;

    private const float RowHeight = 16f;
    private const float RowContentWidth = 170f;
    private const float BarWidth = 60f;
    private const float BarHeight = 8f;
    private const float NameBarGap = 6f;

    /// <summary>The popup's own desired content-area size -- PlayerHealthBarContent adds its host window's border inset on top of this when sizing the popup window itself.</summary>
    public static readonly Vector2 ContentSize = new(RowContentWidth, RowHeight * MaxRowCount);

    private readonly LabelRenderer _labelRenderer = new();
    private readonly List<RowData> _scratchRows = [];

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont(FontChrome.PlayerHealthHoverFontSize);
    }

    public void Update(GameTime gameTime) { }

    public void DrawContent(GameTime gameTime)
    {
        BuildRows(_scratchRows);
        if (_scratchRows.Count == 0)
        {
            return;
        }

        var spriteBatch = _hostWindow.ElementPoolService.SpriteBatch;
        var unitRectangle = _hostWindow.ElementPoolService.UnitRectangle;
        var origin = _hostWindow.ContentAbsolutePosition;
        var contentSize = _hostWindow.ContentSize;
        var rowHeight = contentSize.Y / MaxRowCount;

        for (var rowIndex = 0; rowIndex < _scratchRows.Count; rowIndex++)
        {
            var row = _scratchRows[rowIndex];
            DrawRow(spriteBatch, unitRectangle, origin, contentSize.X, rowHeight, rowIndex, row.Name, row.Fraction, row.HasResource);
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

        for (var denseIndex = bodyParts.GetFirstDenseIndex(playerEntityId); denseIndex != -1; denseIndex = bodyParts.GetNextDenseIndex(denseIndex))
        {
            ref readonly var part = ref bodyParts.GetReadonlyByDenseIndex(denseIndex);
            var effectiveMaximumHealth = StatModifierMath.GetEffectiveValue(statModifiers, playerEntityId, StatModifierTarget.MaximumHealth, part.MaximumHealth);
            var partFraction = effectiveMaximumHealth > 0 ? MathHelper.Clamp(part.CurrentHealth / effectiveMaximumHealth, 0f, 1f) : 0f;
            destination.Add(new RowData(part.Name, partFraction, effectiveMaximumHealth > 0));
        }
    }

    internal readonly record struct RowData(string Name, float Fraction, bool HasResource);

    private void DrawRow(SpriteBatch spriteBatch, Texture2D unitRectangle, Vector2 origin, float contentWidth, float rowHeight, int rowIndex, string name, float fraction, bool hasResource)
    {
        var rowTop = origin.Y + rowIndex * rowHeight;
        var barX = origin.X + contentWidth - BarWidth;
        var nameWidth = System.Math.Max(0f, barX - origin.X - NameBarGap);

        _labelRenderer.DrawLeftAligned(spriteBatch, _font, name, new Vector2(origin.X, rowTop), new Vector2(nameWidth, rowHeight), Color.Black);

        var barY = rowTop + (rowHeight - BarHeight) / 2f;
        var barRectangle = new Rectangle((int)barX, (int)barY, (int)BarWidth, (int)BarHeight);
        ResourceBarRenderer.Draw(spriteBatch, unitRectangle, barRectangle, fraction, hasResource, HealthBarPalette.OutlineColor, HealthBarPalette.FractionColor);
    }
}
