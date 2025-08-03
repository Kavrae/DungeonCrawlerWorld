using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DungeonCrawlerWorld.Utilities
{
    public class FormatTextCriteria
    {
        private SpriteFont _font;
        public SpriteFont Font
        {
            get
            {
                return _font;
            }
            set
            {
                _font = value;
                FontSize = Font.MeasureString(" ");
            }
        }
        public Vector2 FontSize { get; set; }
        public float MaximumPixelWidth { get; set; }
        public string TextToFormat { get; set; }
        public bool Truncate { get; set; }
        public bool WordWrap { get; set; }
    }
}