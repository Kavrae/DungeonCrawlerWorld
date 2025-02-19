using System;

using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Components
{
    public struct ClassGlyphComponent
    {
        public Guid EntityId { get; set; }
        public string Glyph { get; set; }
        public Color GlyphColor { get; set; }
        public Point GlyphOffset { get; set; }

        public ClassGlyphComponent(Guid entityId) : this(entityId, "", Color.Black, new(0, 0)) { }
        public ClassGlyphComponent(Guid entityId, string glyph,  Color glyphColor, Point glyphOffset)
        {
            EntityId = entityId;
            Glyph = glyph;
            GlyphColor = glyphColor;
            GlyphOffset = glyphOffset;

            ComponentRepo.ClassGlyphComponents.Remove(entityId);
            ComponentRepo.ClassGlyphComponents.Add(entityId, this);
        }

        public override string ToString()
        {
            return $"Class Glyph : {Glyph}";
        }
    }
}
