using FontStashSharp;
using Game.Blueprints;
using Game.Modules.Core.Components;
using Game.Modules.Currency;
using Microsoft.Xna.Framework;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI.Content;

/// <summary>
/// One half of a CurrencyRowContent -- "Gold : {n} [sprite]" or "Credits : {n} [sprite]" for one
/// entity. A plain Element (not Window) -- no title/chrome needed, same reasoning
/// InventoryItemStackCell/Folder/Button use -- hoverable/draggable/right-clickable exactly like
/// InventoryItemStackCell (same base-fill+hover-overlay draw via GridSquareRenderer, IsHovered set
/// every frame by the owning content class, OnRightClicked inherited free from Element). No
/// IsSelected/click-to-inspect concept -- hover + drag + right-click only, matching what was asked.
/// </summary>
public sealed class CurrencyElement(FontService fontService, ElementPoolService elementPoolService, LabelRenderer labelRenderer, SpriteSheetService spriteSheetService, SpriteRenderer spriteRenderer)
    : Element(fontService, elementPoolService, labelRenderer)
{
    private const float IconGap = 4f;

    private SpriteFontBase _textFont = null!;
    private SpriteFontBase _glyphFont = null!;
    private int _amount;

    /// <summary>The entity whose Currency this element represents -- what UiInputController's content-drag path reads as a transfer's origin entity, and what the owning CurrencyRowContent's context menu reads to decide Give vs. Take (mirrors InventoryItemStackCell.EntityId exactly).</summary>
    public int EntityId { get; private set; }

    /// <summary>Which currency this element represents -- which sprite/label it draws, and (via CurrencyActions.TryTransfer) which balance a drag/Give/Take moves.</summary>
    public CurrencyType Type { get; private set; }

    /// <summary>Drives a translucent highlight overlay -- see CurrencyRowContent's own hover polling. Mirrors InventoryItemStackCell.IsHovered exactly.</summary>
    public bool IsHovered { get; set; }

    /// <summary>The square icon's own on-screen size -- what UiInputController.TryStartContentDrag reads as the drag ghost's size, not CurrentSize (this element's full "Gold : 10 [sprite]" bounds, much wider than tall): drawing the ghost at the whole element's size stretched the sprite horizontally.</summary>
    public Vector2 IconSize => new(ContentSize.Y, ContentSize.Y);

    /// <summary>Label/sprite key/glyph-fallback char/glyph-fallback color for each CurrencyType -- the one place this element switches on it, so adding a new currency here (and to CurrencyType/CurrencyComponent) is enough for this element to draw it.</summary>
    private (string Label, string SpriteName, string Glyph, Color GlyphColor) TypeDisplay => Type switch
    {
        CurrencyType.Gold => ("Gold", "Currency-Gold", "G", Color.Gold),
        CurrencyType.Credits => ("Credits", "Currency-Credit", "C", Color.LightBlue),
        _ => throw new ArgumentOutOfRangeException(),
    };

    /// <summary>elementSize sizes the sprite/glyph-fallback font -- same "known fixed size, not ContentSize" reasoning InventoryItemStackCell.Configure documents (this runs immediately after CreateElement, before layout has necessarily settled).</summary>
    public void Configure(int entityId, CurrencyType type, Vector2 elementSize)
    {
        EntityId = entityId;
        Type = type;
        _textFont = fontService.GetFont(FontChrome.DefaultFontSize);
        _glyphFont = fontService.GetFont((int)(elementSize.Y * FontChrome.IconGlyphFontFraction));
    }

    /// <summary>Called every frame by the owning CurrencyRowContent -- Currency has no version watcher, so an unconditional re-read is the simplest correct option (same cost the old CurrencyRow.Format paid every Update).</summary>
    public void SetAmount(int amount) => _amount = amount;

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = ElementPoolService.SpriteBatch;
        var unitRectangle = ElementPoolService.UnitRectangle;
        var bounds = new Rectangle((int)ContentAbsolutePosition.X, (int)ContentAbsolutePosition.Y, (int)ContentSize.X, (int)ContentSize.Y);

        GridSquareRenderer.DrawBase(spriteBatch, unitRectangle, bounds);
        GridSquareRenderer.DrawStateOverlay(spriteBatch, unitRectangle, bounds, IsHovered ? GridSquareState.Hovered : GridSquareState.Normal);

        var (label, spriteName, glyph, glyphColor) = TypeDisplay;

        var text = $"{label} : {_amount}";
        var textSize = _textFont.MeasureString(text);
        var textPosition = ContentAbsolutePosition + new Vector2(0, (ContentSize.Y - textSize.Y) / 2f);
        spriteBatch.DrawString(_textFont, text, textPosition, WindowPalette.BodyTextColor);

        // Sits IconGap past the text's own measured width, not pinned to the element's right edge --
        // "Gold : 10" then the sprite immediately after, not stranded at the far side of the row.
        var iconPosition = new Vector2(textPosition.X + textSize.X + IconGap, ContentAbsolutePosition.Y);
        SpriteComponent? sprite = SpriteManifest.TryGet(spriteName, out var spriteComponent) ? spriteComponent : null;
        SpriteOrGlyphRenderer.Draw(spriteBatch, spriteSheetService, spriteRenderer, LabelRenderer, sprite, _glyphFont, glyph, glyphColor, iconPosition, IconSize, Color.White);
    }
}
