using Microsoft.Xna.Framework;
using UIEngine.UI;
using UIEngine.UI.ColorPalettes;

namespace Workshop.Debug;

/// <summary>How the Debug tab's sections create and place their windows and children.</summary>
internal static class DebugTabElements
{
    private static readonly Vector2 LargestSize = new(4000, 4000);

    /// <summary>A titled top-level window in the Base layer.</summary>
    public static Window CreateWindow(DebugTabServices services, Rectangle bounds, string title)
    {
        var window = services.Pool.CreateElement<Window>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(bounds.X, bounds.Y), Size = new Vector2(bounds.Width, bounds.Height), MaximumSize = LargestSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = title, ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        window.Initialize();
        services.Layers.Add(UiLayer.Base, window);
        return window;
    }

    public static T CreateChild<T>(DebugTabServices services, Window parent, Vector2 position, Vector2 size, bool showBorder)
        where T : Element
    {
        var child = services.Pool.CreateElement<T>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MaximumSize = LargestSize, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = showBorder },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        parent.AddChild(child);
        return child;
    }

    public static TextWindow CreateText(DebugTabServices services, Window parent, Vector2 position, Vector2 size, string text)
    {
        var textWindow = services.Pool.CreateElement<TextWindow>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MaximumSize = LargestSize, DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = text, TextColor = WindowPalette.BodyTextColor },
        });
        parent.AddChild(textWindow);
        return textWindow;
    }

    /// <summary>A single-line text box whose text can be selected and copied but not changed.</summary>
    public static TextBox CreateReadOnlyBox(DebugTabServices services, Window parent, Vector2 position, Vector2 size)
    {
        var box = services.Pool.CreateElement<TextBox>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MaximumSize = new Vector2(LargestSize.X, size.Y), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true },
            Content = new ElementContentOptions { ContentColor = Color.White },
            Text = new TextOptions { TextColor = WindowPalette.BodyTextColor },
        });
        box.IsReadOnly = true;
        parent.AddChild(box);
        return box;
    }

    /// <summary>A switch tile: label on a raised or sunken face, styled by StyleTile; a click runs onClicked.</summary>
    public static TextWindow CreateTile(DebugTabServices services, Window parent, Vector2 position, float width, string label, Action onClicked)
    {
        var size = new Vector2(width, DebugTabChrome.RowHeight);
        var tile = services.Pool.CreateElement<TextWindow>(parent, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Inset, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
            Text = new TextOptions { Text = label, TextColor = WindowPalette.BodyTextColor },
        });
        tile.Clicked += _ => onClicked();
        parent.AddChild(tile);
        return tile;
    }

    /// <summary>Raises the selected tile on the control colour; sinks the others.</summary>
    public static void StyleTile(TextWindow tile, bool isSelected)
    {
        tile.BorderStyle = isSelected ? BorderStyle.Outset : BorderStyle.Inset;
        tile.SetContentColor(isSelected ? WindowPalette.ControlBackground : WindowPalette.PanelContentColor);
        tile.TextColor = isSelected ? Color.White : WindowPalette.BodyTextColor;
    }

    public static void Place(Element element, Rectangle bounds)
    {
        element.SetRelativePosition(new Vector2(bounds.X, bounds.Y));
        element.SetSize(new Vector2(bounds.Width, bounds.Height));
    }

    /// <summary>Removes window from the Base layer and closes it.</summary>
    public static void Close(DebugTabServices services, Window window)
    {
        services.Layers.Remove(UiLayer.Base, window);
        window.Close();
    }
}
