using Microsoft.Xna.Framework;

using DungeonCrawlerWorld.Utilities;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public struct Tile
    {
        public Vector3Int Size { get; set; }
        public Rectangle DrawRectangle { get; set; }
        public Rectangle InnerDrawRectangle { get; set; }

        public Point MapNodePosition { get; set; }
        public bool IsSelected { get; set; }

        public bool HasChanged { get; set; }

        public Color? BackgroundColor { get; set; }
        public Color? ForegroundColor { get; set; }
        public string Glyph { get; set; }
        public Vector2 GlyphDrawPosition { get; set; }


    }
}
