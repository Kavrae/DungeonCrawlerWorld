using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class TextWindowOptions : WindowOptions
    {
        public string Text { get; set; }
        public Color? TextColor { get; set; }
    }
}