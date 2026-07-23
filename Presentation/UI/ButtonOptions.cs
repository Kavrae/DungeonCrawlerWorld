using FontStashSharp;
using Microsoft.Xna.Framework;

namespace Presentation.UI;

public class ButtonOptions
{
    public Color? Color { get; set; }

    public SpriteFontBase? Font { get; set; }

    public Vector2? RelativePosition { get; set; }

    public bool? ShowBorder { get; set; }

    /// <summary>Defaults to Outset -- see Button's own doc comment on its BorderStyle property.</summary>
    public BorderStyle? BorderStyle { get; set; }

    public Vector2? Size { get; set; }

    public string? Text { get; set; }
}