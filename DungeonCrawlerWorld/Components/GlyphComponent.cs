using System;

using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.Components
{
    public struct GlyphComponent : IEntityComponent
    {
        public Guid EntityId { get; set; }
        public string Glyph { get; set; }
        public Color GlyphColor { get; set; }
        public Point GlyphOffset { get; set; }

        public GlyphComponent(Guid entityId) : this(entityId, "", Color.Black, new(0, 0)) { }
        public GlyphComponent(Guid entityId, string glyph,  Color glyphColor, Point glyphOffset)
        {
            EntityId = entityId;
            Glyph = glyph;
            GlyphColor = glyphColor;
            GlyphOffset = glyphOffset;

            ComponentRepo.GlyphComponents.Remove(entityId);
            ComponentRepo.GlyphComponents.Add(entityId, this);
        }

        public override string ToString()
        {
            return $"Glyph : {Glyph}";
        }
    }
}
