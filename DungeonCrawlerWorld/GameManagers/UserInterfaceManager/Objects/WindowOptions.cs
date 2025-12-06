using Microsoft.Xna.Framework;

namespace DungeonCrawlerWorld.GameManagers.UserInterfaceManager
{
    /// <summary>
    /// Options for configuring the behavior and appearance of a window during instantiation.
    /// </summary>
    /// <todo>
    /// Separate each /*=header=*/ section into its own child Options class to reduce the size of this class.
    /// </todo>
    public class WindowOptions
    {
        /*========Window hierarchy========*/
        /// <summary>
        /// Allows the window to contain, update, and draw child windows.
        /// </summary>
        public bool? CanContainChildWindows { get; set; }
        
        /// <summary>
        /// Determines how child windows are tiled within this window.
        /// When tiled, this window will determine the relative position and size of each child window based on 
        /// the specified tile mode and position in the child window list.
        /// </summary>
        public WindowTileMode? ChildWindowTileMode { get; set; }

        /*========Window========*/
        /// <summary>
        /// Determines how the window sizes itself relative to its content and parent container.
        /// </summary>
        public WindowDisplayMode? DisplayMode { get; set; }

        /// <summary>
        /// The position of the window relative to its parent container.
        /// Absolute value is determined by adding the parent's position to this relative position.
        /// If no parent is specified, such as for root windows, the position is relative to the game screen.
        /// </summary>
        public Vector2? RelativePosition { get; set; }

        /// <summary>
        /// The minimum size of the window during both manual and automating resizing.
        /// </summary>
        public Vector2? MinimumSize { get; set; }
        
        /// <summary>
        /// The maximum size of the window during both manual and automating resizing.
        /// </summary>
        public Vector2? MaximumSize { get; set; }

        /// <summary>
        /// The current size of the window when set to Static display mode.
        /// If not specified, the maximum size will be used.
        /// </summary>
        public Vector2? Size { get; set; }

        /// <summary>
        /// Determines whether the window is visible.
        /// This can be used to temporarily hide windows without requiring expensive destruction and re-creation.
        /// </summary>
        public bool? IsVisible { get; set; }

        /// <summary>
        /// Determines if the window background is transparent.
        /// </summary>
        /// <todo>
        /// rename for clarity
        /// </todo>
        public bool? IsTransparent { get; set; }

        /*========Title========*/
        /// <summary>
        /// Determines whether the window title bar is displayed during non-minimized display modes.
        /// When displayed, the window's content size will be recalculated to account for the title bar height.
        /// </summary>
        public bool? ShowTitle { get; set; }

        /// <summary>
        /// Determines whether the window title bar is displayed whenever the window is minimized.
        /// This allows for otherwise title-less windows to minimize properly and not disappear.
        /// </summary>
        public bool? ShowTitleWhenMinimized { get; set; }

        /// <summary>
        /// The text to be displayed in the window's title bar.
        /// </summary>
        public string TitleText { get; set; }
        
        /// <summary>
        /// The color of the text in the window's title bar background.
        /// </summary>
        /// <todo>
        /// Separate title font and background font colors
        /// </todo>
        public Color? TitleColor { get; set; }

        /*========Border========*/
        /// <summary>
        /// Determines whether the window border is displayed.
        /// When displayed, the window's title bar and content size will be recalculated to account for the border size.
        /// </summary>
        public bool? ShowBorder { get; set; }
        
        /// <summary>
        /// The size of the window border per size in pixels.
        /// Default is 1 pixel per side.
        /// </summary>
        public Vector2? BorderSize { get; set; }

        /*========Content========*/
        /// <summary>
        /// The color of the window's content area background.
        /// </summary>
        /// <todo>
        /// Separate content font and background colors
        /// </todo>
        public Color? ContentColor { get; set; }

        /*========User Controls========*/
        /// <summary>
        /// Determines whether the user can close the window via the title bar buttons
        /// NOT YET USED
        /// </summary>
        public bool? CanUserClose { get; set; }
        
        /// <summary>
        /// Determines whether the user can minimize the window via the title bar buttons
        /// NOT YET USED
        /// </summary>
        public bool? CanUserMinimize { get; set; }
        
        /// <summary>
        /// Determines whether the user can move the window via dragging the title bar
        /// NOT YET USED
        /// </summary>
        public bool? CanUserMove { get; set; }
        
        /// <summary>
        /// Determines whether the user can resize the window via dragging the borders
        /// NOT YET USED
        /// </summary>
        public bool? CanUserResize { get; set; }
        
        /// <summary>
        /// Determines whether the user can scroll the window horizontally
        /// NOT YET USED
        /// KEYS NOT YET DETERMINED
        /// </summary>
        public bool? CanUserScrollHorizontal { get; set; }
        
        /// <summary>
        /// Determines whether the user can scroll the window VERTICALLY
        /// NOT YET USED
        /// KEYS NOT YET DETERMINED
        /// </summary>
        public bool? CanUserScrollVertical { get; set; }
    }
}