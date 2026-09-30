using Microsoft.Xna.Framework;
using Presentation.UI.Content;

namespace Presentation.Input;

/// <summary>Where the pointer is and what it's dragging, as UiInputController last saw it, for content that draws at the cursor.</summary>
/// <remarks>UiInputController rewrites it at the end of every Update; built before it, so the contents that read it can take it in their constructors.</remarks>
public sealed class PointerState
{
    /// <summary>The mouse's screen position.</summary>
    public Point CursorPosition { get; internal set; }

    /// <summary>The content drag in progress, if any -- what DragGhostContent draws.</summary>
    public DragGhostState ContentDrag { get; internal set; }
}
