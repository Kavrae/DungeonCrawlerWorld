using Presentation.Input;
using Presentation.UI.Content;

namespace Presentation.UI;

/// <summary>The shell's shared UI singletons -- one of each, built first, before any window.</summary>
/// <remarks>
/// Handed only to composition points that need most of it (the element factory registrations, ItemWindowCoordinator);
/// an individual element still takes just the services it uses, so this never becomes a bag every class reaches into.
/// </remarks>
public sealed record ShellServices(
    UiLayerStack Layers,
    MapViewState MapViewState,
    MapCamera Camera,
    ContextMenuController ContextMenuController,
    TooltipController TooltipController,
    PointerState PointerState,
    CursorTextContent CursorTextContent);
