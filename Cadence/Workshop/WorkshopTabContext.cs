using UIEngine.Bootstrap;
using UIEngine.UI;

namespace Workshop;

/// <summary>The shell's services a tab builds its windows with.</summary>
public sealed record WorkshopTabContext(UiEngineContext UiEngine, UiLayerStack Layers, ContextMenuController ContextMenuController);
