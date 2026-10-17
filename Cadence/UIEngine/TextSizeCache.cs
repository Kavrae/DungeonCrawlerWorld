using FontStashSharp;
using Microsoft.Xna.Framework;

namespace UIEngine;

/// <summary>The measured size of one line of text, measured again only when the font or the text changes.</summary>
/// <remarks>
/// One per thing drawn, like KeyedCache, for text a draw places by its size every frame. Pass the same string instance the caller's text
/// cache hands out and a repeat ask compares by reference.
/// </remarks>
public sealed class TextSizeCache
{
    private readonly KeyedCache<(SpriteFontBase Font, string Text), Vector2> _size = new();

    public Vector2 SizeOf(SpriteFontBase font, string text) => _size.Get((font, text), static key => key.Font.MeasureString(key.Text));
}
