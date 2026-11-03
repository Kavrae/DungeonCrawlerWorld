using Engine.Math;
using Microsoft.Xna.Framework;

namespace Presentation.MapRendering;

/// <summary>A ring on the ground under a footprint, drawn by MapRenderer before anything standing on it: the selection's.</summary>
/// <param name="Position">The footprint's top-left tile.</param>
/// <param name="Size">The footprint in tiles.</param>
/// <param name="Color">The ring's colour, not premultiplied by an alpha of its own.</param>
public readonly record struct MapGroundMark(Point Position, Vector2Byte Size, Color Color);
