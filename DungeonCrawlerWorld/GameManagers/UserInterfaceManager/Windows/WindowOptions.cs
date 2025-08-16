using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    public class WindowOptions
    {
        /*========Window hierarchy========*/
        public bool? CanContainChildWindows { get; set; }
        public WindowTileMode? ChildWindowTileMode { get; set; }

        /*========Window========*/
        public WindowDisplayMode? DisplayMode { get; set; }
        public Vector2? RelativePosition { get; set; }

        public Vector2? MinimumSize { get; set; }
        public Vector2? MaximumSize { get; set; }
        public Vector2? Size { get; set; }

        /*========Title========*/
        public bool? ShowTitle { get; set; }
        public string TitleText { get; set; }

        /*========Border========*/
        public bool? ShowBorder { get; set; }
        public Vector2? BorderSize { get; set; }

        /*========User Controls========*/
        public bool? CanUserClose { get; set; }
        public bool? CanUserMinimize { get; set; }
        public bool? CanUserMove { get; set; }
        public bool? CanUserResize { get; set; }
        public bool? CanUserScrollHorizontal { get; set; }
        public bool? CanUserScrollVertical { get; set; }
        public bool? ContentIsMinimized { get; set; }
    }
}