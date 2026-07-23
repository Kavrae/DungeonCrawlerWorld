namespace Presentation.Input;

/// <summary>
/// Which edge(s) of a window a resize-drag is anchored to -- a corner is two flags combined
/// (e.g. Top | Left), since dragging a corner resizes both axes at once. See
/// Window.GetResizeEdgesAt for how a screen position maps to these.
/// </summary>
[Flags]
internal enum ResizeEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}