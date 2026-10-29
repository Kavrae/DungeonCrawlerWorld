namespace UIEngine.UI;

/// <summary>
/// Generic "one pooled window a controller can open/close/toggle" lifecycle -- the shared shape of windows that
/// differ only in their own ElementOptions (createAndConfigure) and disabled predicate. Pooled and
/// reused for a future open (see
/// ElementPoolService) -- ElementPoolService.CloseElement clears every event on a pooled Element
/// (Closed included) before it goes back into its pool, so HandleClosed's own subscription can't
/// outlive the reuse cycle without detaching itself. The open window is a member of group, if one is given.
/// </summary>
public sealed class WindowLifecycle<TWindow>(Func<TWindow> createAndConfigure, Func<bool> isDisabled, UiLayerStack layers, Action onClosed, ElementGroup? group = null)
    where TWindow : Element
{
    public TWindow? Window { get; private set; }

    public void Open()
    {
        if (Window is not null || isDisabled())
        {
            return;
        }

        var window = createAndConfigure();
        window.Closed += HandleClosed;
        window.Initialize();
        layers.Add(UiLayer.DynamicHud, window);
        layers.OpenMenuWindow(window); // See UiLayerStack.OpenMenuWindow: a host may pause while menu windows are open.
        group?.Join(window);
        Window = window;
    }

    public void Toggle()
    {
        if (Window is not null)
        {
            Window.Close();
        }
        else
        {
            Open();
        }
    }

    public void CloseIfOpen() => Window?.Close();

    private void HandleClosed(Element closedWindow)
    {
        layers.Remove(UiLayer.DynamicHud, closedWindow);
        layers.CloseMenuWindow(closedWindow);
        group?.Leave(closedWindow);
        Window = null;
        onClosed();
    }
}
