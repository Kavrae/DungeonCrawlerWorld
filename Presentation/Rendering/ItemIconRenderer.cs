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
    private static readonly Vector2 QuantityTextPadding = new(0, 0);

    /// <summary>
    /// chargeText, when non-null, replaces the plain quantity number outright (e.g. "5/6" for a
    /// wand's remaining/max charges) rather than showing alongside it -- see this parameter's own
    /// callers for why: the moment an item's first charge is ever consumed, its Quantity stops
    /// meaning "how many I have" for that specific stack (a wand's own charge count is what
    /// actually matters once it's diverged), so showing both at once would read as contradictory.
    /// No-ops when there's nothing to show at all: no chargeText and quantity &lt;= 1.
    /// </summary>
    public static void DrawQuantityBadge(SpriteBatch spriteBatch, SpriteFontBase quantityFont, int quantity, string? chargeText, Vector2 contentPosition, Vector2 contentSize)
    {
        var text = chargeText ?? (quantity > 1 ? quantity.ToString() : null);
        if (text is null)
        {
            return;
        }

        var textSize = quantityFont.MeasureString(text);
        var textPosition = contentPosition + contentSize - textSize - QuantityTextPadding;
        spriteBatch.DrawString(quantityFont, text, textPosition, QuantityShadowColor);
        spriteBatch.DrawString(quantityFont, text, textPosition + QuantityShadowOffset, QuantityTextColor);
    }
}
