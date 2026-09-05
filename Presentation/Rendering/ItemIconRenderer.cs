using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>Shared "item stack quantity, bottom-left corner, shadowed for legibility over any background" draw primitive -- bottom-left for consistency with ShopItemStackCell/TradeItemStackCell's own quantity placement, both of which reserve the bottom-right corner for price. InventoryItemStackCell is the one remaining consumer today (HotbarContent moved to its own bottom-center "x{n}" badge, see HotbarContent's own doc comment).</summary>
public static class ItemIconRenderer
{
    private static readonly Color QuantityShadowColor = Color.Black;
    private static readonly Color QuantityTextColor = Color.White;
    private static readonly Vector2 QuantityShadowOffset = new(-1, -1);
    private static readonly Vector2 QuantityTextPadding = new(0, 0);

    /// <summary>No-ops at quantity &lt;= 1 -- a lone stack doesn't need a number. A wand's own remaining/max charges used to replace this outright ("5/6" instead of the plain count) but that's shown in the hover tooltip now instead -- every item's badge is just its plain Quantity, charges or not.</summary>
    public static void DrawQuantityBadge(SpriteBatch spriteBatch, SpriteFontBase quantityFont, int quantity, Vector2 contentPosition, Vector2 contentSize)
    {
        if (quantity <= 1)
        {
            return;
        }

        DrawBottomAligned(spriteBatch, quantityFont, quantity.ToString(), contentPosition, contentSize, alignRight: false, QuantityTextColor);
    }

    /// <summary>
    /// Same shadowed-for-legibility-over-any-background styling DrawQuantityBadge already uses,
    /// generalized to either bottom corner and a caller-chosen text color -- TradeItemStackCell's
    /// own bottom-left quantity / bottom-right total-price pair (the price colored favorable/
    /// unfavorable, the same way ShopItemStackCell's own price line already is).
    /// </summary>
    public static void DrawBottomAligned(SpriteBatch spriteBatch, SpriteFontBase font, string text, Vector2 contentPosition, Vector2 contentSize, bool alignRight, Color textColor)
    {
        var textSize = font.MeasureString(text);
        var x = alignRight ? contentPosition.X + contentSize.X - textSize.X - QuantityTextPadding.X : contentPosition.X + QuantityTextPadding.X;
        var textPosition = new Vector2(x, contentPosition.Y + contentSize.Y - textSize.Y - QuantityTextPadding.Y);
        spriteBatch.DrawString(font, text, textPosition, QuantityShadowColor);
        spriteBatch.DrawString(font, text, textPosition + QuantityShadowOffset, textColor);
    }
}
