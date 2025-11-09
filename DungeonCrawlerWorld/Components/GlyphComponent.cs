using System;

using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Components
{
    public struct GlyphComponent : IEntityComponent
    {
        public int EntityId { get; set; }
        public string Glyph { get; set; }
        public Color GlyphColor { get; set; }
        public Point GlyphOffset { get; set; }

        public GlyphComponent(int entityId) : this(entityId, "", Color.Black, new(0, 0)) { }
        public GlyphComponent(int entityId, string glyph,  Color glyphColor, Point glyphOffset)
        {
            EntityId = entityId;
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
