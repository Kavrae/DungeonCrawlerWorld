using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Presentation.Rendering;

/// <summary>Shared "item stack quantity, bottom-right corner, shadowed for legibility over any background" draw primitive -- InventoryItemStackCell (the inventory grid) and HotbarContent (a bound item slot) both stack the same items and need to show the same count the same way.</summary>
public static class ItemIconRenderer
{
    private static readonly Color QuantityShadowColor = Color.Black;
    private static readonly Color QuantityTextColor = Color.White;
    private static readonly Vector2 QuantityShadowOffset = new(-1, -1);
    private static readonly Vector2 QuantityTextPadding = new(2, 0);

    /// <summary>No-ops for quantity &lt;= 1 -- a lone item doesn't need a count badge.</summary>
    public static void DrawQuantityBadge(SpriteBatch spriteBatch, SpriteFontBase quantityFont, int quantity, Vector2 contentPosition, Vector2 contentSize)
    {
        if (quantity <= 1)
        {
            return;
        }

        var text = quantity.ToString();
        var textSize = quantityFont.MeasureString(text);
        var textPosition = contentPosition + contentSize - textSize - QuantityTextPadding;
        spriteBatch.DrawString(quantityFont, text, textPosition, QuantityShadowColor);
        spriteBatch.DrawString(quantityFont, text, textPosition + QuantityShadowOffset, QuantityTextColor);
    }
}
