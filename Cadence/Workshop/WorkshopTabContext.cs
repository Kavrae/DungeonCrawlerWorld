using UIEngine.Bootstrap;
using UIEngine.Input;
using UIEngine.UI;

namespace Workshop;

/// <summary>The shell's services a tab builds its windows with, one per tab.</summary>
/// <remarks>A tab registers its input handlers through AddInputHandler, so the shell can remove every one of them when the tab closes (RemoveAllInputHandlers) and a closed tab never handles input.</remarks>
public sealed class WorkshopTabContext(UiEngineContext uiEngine, UiLayerStack layers, ContextMenuController contextMenuController, IWorkshopStatus status, UiInputController inputController, TabNavigator navigator, WorkshopBottomPanel bottomPanel)
{
    private readonly List<IUiInputHandler> _inputHandlers = [];

    public UiEngineContext UiEngine { get; } = uiEngine;

    public UiLayerStack Layers { get; } = layers;

    public ContextMenuController ContextMenuController { get; } = contextMenuController;

    /// <summary>The shell's status line.</summary>
    public IWorkshopStatus Status { get; } = status;

    /// <summary>Opens another tab with one of its records selected -- what a link to another table's record asks for.</summary>
    public TabNavigator Navigator { get; } = navigator;

    /// <summary>Shows what names the record the tab has selected in the bottom panel's Used By, and what it names in Uses.</summary>
    public void ShowRelatedRecords(IReadOnlyList<WorkshopRelatedRecord> usedBy, IReadOnlyList<WorkshopRelatedRecord> uses) => bottomPanel.ShowRelatedRecords(usedBy, uses);

    /// <summary>Shows how the tab's selected record differs from what it is compared with in the bottom panel's Changes; null takes Changes away (nothing to compare).</summary>
    public void ShowChanges(WorkshopChanges? changes) => bottomPanel.ShowChanges(changes);

    /// <summary>Gives element keyboard focus -- taking the player to a field.</summary>
    public void FocusElement(UIEngine.UI.Element element) => inputController.FocusElement(element);

    /// <summary>Registers handler with the shell's input controller until the tab closes.</summary>
    public void AddInputHandler(IUiInputHandler handler)
    {
        inputController.AddInputHandler(handler);
        _inputHandlers.Add(handler);
    }

    /// <summary>Removes every handler this tab registered -- the shell calls this when the tab closes.</summary>
    public void RemoveAllInputHandlers()
    {
        foreach (var handler in _inputHandlers)
        {
            inputController.RemoveInputHandler(handler);
        }

        _inputHandlers.Clear();
    }
}
