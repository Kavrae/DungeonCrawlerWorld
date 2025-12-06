using FontStashSharp;
using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class ButtonOptions
    {
        public Color? Color
        {
            get; set;
        }

        public SpriteFontBase Font
        {
            get; set;
        }

        public Window ParentWindow
        {
            get; set;
        }

        public Vector2? RelativePosition
        {
            get; set;
        }

        public bool? ShowBorder
        {
            get; set;
        }

        public Vector2? Size
        {
            get; set;
        }

        public string Text
        {
            get; set;
        }

        public Vector2? TextOffset
        {
            get; set;
        }
    }
}
