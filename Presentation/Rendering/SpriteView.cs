using Microsoft.Xna.Framework;
using SpriteFormat;

namespace Presentation.Rendering;

/// <summary>A sprite to draw: a cell of a spritesheet, how it sits on its footprint, how it is turned and flipped, and its tint.</summary>
/// <param name="Placement">Pixels per tile and pivot, from the manifest entry; null stretches it to fill whatever it is drawn into. Only the map reads it -- icons always fill their cell.</param>
/// <param name="Transform">The cell's flips and quarter turns, applied by every reader.</param>
/// <param name="Tint">The cell's own tint, multiplied into whatever tint the reader draws with (a corpse's grey, a translucent alpha); null for none. Applied by every reader, icons included.</param>
public readonly record struct SpriteView(string SheetPath, Rectangle SourceRectangle, SpritePlacement? Placement = null, SpriteCellTransform Transform = default, Color? Tint = null);
