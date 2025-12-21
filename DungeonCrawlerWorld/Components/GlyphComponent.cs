using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Components
{
    /// <summary>
    /// A core component that defines how an entity is displayed on the map
    /// </summary>
    public struct GlyphComponent : IEntityComponent
    {
        /// <summary>
        /// The ASCII symbol drawn to the screen
        /// </summary>
        public string Glyph { get; set; }

        /// <summary>
        /// The color of the symbol to be drawn. This can be modified by the race, class, status effect, or any other systems.
        /// </summary>
        public Color GlyphColor { get; set; }

        /// <summary>
        /// The 2d pixel value determining how much the glyph is draw away from the center of a mapNode tile.
        /// This is used to correct unusually positioned glyphs, position multi-node entities, and draw effects that make a glyph move within a mapNode
        /// </summary>
        public Point GlyphOffset { get; set; }

        public GlyphComponent(int entityId) : this(entityId, "", Color.Black, new(0, 0)) { }
        public GlyphComponent(int entityId, string glyph,  Color glyphColor, Point glyphOffset)
        {
            Glyph = glyph;
            GlyphColor = glyphColor;
            GlyphOffset = glyphOffset;

            ComponentRepo.GlyphComponents[entityId] = this;
        }

        public override string ToString()
        {
            return $"Glyph : {Glyph}";
        }
    }
}
