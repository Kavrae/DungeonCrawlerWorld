namespace UIEngine.UI;

/// <summary>How child windows are arranged within a parent window.</summary>
public enum ChildElementTileMode : byte
{
    /// <summary>Freely positioned; the creator sets relative position and draw order.</summary>
    Floating,

    /// <summary>Arranged horizontally, left to right, each after the previous child's width, SpacingAfter and the parent's ChildGap.</summary>
    Horizontal,

    /// <summary>Arranged vertically, top to bottom, each after the previous child's height, SpacingAfter and the parent's ChildGap.</summary>
    Vertical,
}