using FontStashSharp;
using Game.Modules.StatusEffects;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.FloatingText;

/// <summary>Draws FloatingTextController's active texts over the map, oldest first.</summary>
/// <remarks>
/// A text is one row, centred horizontally on its position. Rising text has its bottom edge there, so it starts just
/// above the entity's footprint; falling text has its top edge there, so it starts just below. The row is an optional status effect icon (stacks added, Immune), an optional yellow "!" (a critical hit),
/// then the label. Heals read "+N", stacks "+N", damage a plain "N". A critical hit's row starts CritPopScale times its
/// size and shrinks back over CritPopFrames. A status effect's icon is its "StatusEffect-&lt;Type&gt;" sprite when the
/// manifest has one, otherwise its registered glyph in the effect's color. Amount strings are cached up to
/// MaxCachedAmount so drawing a text allocates nothing. Texts are published for the whole Local tier, far wider than the
/// viewport, so a text more than ViewportMarginTiles outside the visible tiles is skipped before any measuring. Borough zoom draws nothing: its tiles are too small for the text
/// to belong to anything.
/// </remarks>
public sealed class FloatingTextRenderer(
    FloatingTextController controller,
    MapCamera camera,
    FontService fontService,
    StatusEffectDisplayRegistry statusEffectDisplays,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    LabelRenderer labelRenderer)
{
    private const int MaxCachedAmount = 9999;
    private const string CriticalMarker = "!";
    private const string DodgedLabel = "Dodge";
    private const string ImmuneLabel = "Immune";
    private const float IconGapFraction = 0.15f;
    private const int ViewportMarginTiles = 2;

    private readonly FloatingTextController _controller = controller;
    private readonly MapCamera _camera = camera;
    private readonly FontService _fontService = fontService;
    private readonly StatusEffectDisplayRegistry _statusEffectDisplays = statusEffectDisplays;
    private readonly SpriteSheetService _spriteSheetService = spriteSheetService;
    private readonly SpriteRenderer _spriteRenderer = spriteRenderer;
    private readonly LabelRenderer _labelRenderer = labelRenderer;

    private readonly string?[] _amountTexts = new string?[MaxCachedAmount + 1];
    private readonly string?[] _plusAmountTexts = new string?[MaxCachedAmount + 1];
    private readonly Dictionary<StatusEffectType, SpriteView?> _statusEffectSprites = [];

    public void Draw(SpriteBatch spriteBatch, int currentMapLayer)
    {
        var baseFontSize = _camera.CurrentZoomLevel switch
        {
            ZoomLevel.Team => FontChrome.FloatingTextFontSize,
            ZoomLevel.Neighborhood => FontChrome.FloatingTextNeighborhoodFontSize,
            _ => 0,
        };

        if (baseFontSize == 0)
        {
            return;
        }

        for (var index = 0; index < _controller.ActiveTextCount; index++)
        {
            ref readonly var text = ref _controller.GetActiveText(index);
            if (text.MapLayer == currentMapLayer)
            {
                DrawText(spriteBatch, text, baseFontSize);
            }
        }
    }

    private void DrawText(SpriteBatch spriteBatch, in FloatingTextInstance text, int baseFontSize)
    {
        var tilePosition = text.SpawnTilePosition + FloatingTextMotion.GetOffsetTiles(text.AgeFrames, text.HorizontalDirection, text.VerticalDirection);
        if (!IsNearViewport(tilePosition))
        {
            return;
        }

        var isCritical = (text.Flags & FloatingTextFlags.Critical) != 0;
        var font = _fontService.GetFont(isCritical ? GetCritPopFontSize(baseFontSize, text.AgeFrames) : baseFontSize);
        var alpha = FloatingTextMotion.GetAlpha(text.AgeFrames);

        var hasIcon = text.Kind is FloatingTextKind.StatusEffectStacksAdded or FloatingTextKind.Immune;
        var iconSize = hasIcon ? font.LineHeight : 0f;
        var iconGap = hasIcon ? font.LineHeight * IconGapFraction : 0f;
        var markerWidth = isCritical ? font.MeasureString(CriticalMarker).X : 0f;
        var label = GetLabel(text.Kind, text.Amount);
        var labelWidth = font.MeasureString(label).X;

        var screenPosition = _camera.TileToScreen(tilePosition);
        var rowWidth = iconSize + iconGap + markerWidth + labelWidth;
        var rowTop = MathF.Round(text.VerticalDirection > 0 ? screenPosition.Y : screenPosition.Y - font.LineHeight);
        var cursorX = MathF.Round(screenPosition.X - rowWidth / 2f);

        if (hasIcon)
        {
            DrawStatusEffectIcon(spriteBatch, font, text.EffectType, new Vector2(cursorX, rowTop), iconSize, alpha);
            cursorX += iconSize + iconGap;
        }

        if (isCritical)
        {
            ContrastTextRenderer.Draw(spriteBatch, font, CriticalMarker, new Vector2(cursorX, rowTop), alpha, FloatingTextPalette.CriticalMarkerColor);
            cursorX += markerWidth;
        }

        ContrastTextRenderer.Draw(spriteBatch, font, label, new Vector2(MathF.Round(cursorX), rowTop), alpha, FloatingTextPalette.GetColor(text.Kind, text.EffectType));
    }

    private bool IsNearViewport(Vector2 tilePosition)
    {
        var scrollPosition = _camera.CurrentScrollPosition;
        return tilePosition.X >= scrollPosition.X - ViewportMarginTiles
            && tilePosition.X <= scrollPosition.X + _camera.TileColumns + ViewportMarginTiles
            && tilePosition.Y >= scrollPosition.Y - ViewportMarginTiles
            && tilePosition.Y <= scrollPosition.Y + _camera.TileRows + ViewportMarginTiles;
    }

    private void DrawStatusEffectIcon(SpriteBatch spriteBatch, SpriteFontBase font, StatusEffectType effectType, Vector2 topLeft, float size, float alpha)
    {
        var iconSize = new Vector2(size, size);

        if (GetStatusEffectSprite(effectType) is { } sprite)
        {
            _spriteRenderer.Draw(spriteBatch, _spriteSheetService.GetTexture(sprite.SheetPath), sprite.SourceRectangle, topLeft, iconSize, Color.White * alpha);
            return;
        }

        var glyph = _statusEffectDisplays.TryGet(effectType, out var display) ? display.Glyph : "?";
        var glyphPosition = _labelRenderer.GetCenteredPosition(font, glyph, topLeft, iconSize);
        ContrastTextRenderer.Draw(spriteBatch, font, glyph, new Vector2(MathF.Round(glyphPosition.X), MathF.Round(glyphPosition.Y)), alpha, FloatingTextPalette.GetStatusEffectColor(effectType));
    }

    private SpriteView? GetStatusEffectSprite(StatusEffectType effectType)
    {
        if (!_statusEffectSprites.TryGetValue(effectType, out var sprite))
        {
            sprite = SpriteViews.TryGetFirst($"StatusEffect-{effectType}", out var found) ? found : null;
            _statusEffectSprites[effectType] = sprite;
        }

        return sprite;
    }

    private static int GetCritPopFontSize(int baseFontSize, int ageFrames)
    {
        var popProgress = MathHelper.Clamp((float)ageFrames / FloatingTextChrome.CritPopFrames, 0f, 1f);
        return (int)MathF.Round(baseFontSize * MathHelper.Lerp(FloatingTextChrome.CritPopScale, 1f, popProgress));
    }

    private string GetLabel(FloatingTextKind kind, ushort amount) => kind switch
    {
        FloatingTextKind.Dodged => DodgedLabel,
        FloatingTextKind.Immune => ImmuneLabel,
        FloatingTextKind.Healed or FloatingTextKind.Regenerated or FloatingTextKind.StatusEffectStacksAdded => GetAmountText(_plusAmountTexts, amount, "+"),
        _ => GetAmountText(_amountTexts, amount, string.Empty),
    };

    private static string GetAmountText(string?[] cache, ushort amount, string prefix)
    {
        if (amount > MaxCachedAmount)
        {
            return prefix + amount;
        }

        return cache[amount] ??= prefix + amount;
    }
}
