using Microsoft.Xna.Framework;
using Presentation.UI.Details;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Builders.MapAreas;

/// <summary>The Map tab's "Cell Details" panel: what AreaCellReadout says about the tile under the pointer in the preview map.</summary>
/// <remarks>Always AreaCellReadout.LineCount lines, built once and given new text as the pointer moves, so the panel never changes height. The host is WrapContent and tiles its children vertically.</remarks>
public sealed class CellDetailsWindow(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Window(fontService, elementPoolService, renderService)
{
    private const float RowHeight = ActivatableDetailsSections.RowHeight;

    private readonly List<TextWindow> _lines = [];

    private float _contentWidth;
    private bool _childrenReady;
    private IReadOnlyList<string> _shownLines = AreaCellReadout.Nothing;

    /// <summary>Builds the lines to contentWidth -- safe before the first Initialize (the build waits for it).</summary>
    public void Configure(float contentWidth)
    {
        _contentWidth = contentWidth;
        if (_childrenReady)
        {
            Rebuild();
        }
    }

    /// <summary>Shows lines, one per line of the panel.</summary>
    public void ShowLines(IReadOnlyList<string> lines)
    {
        _shownLines = lines;
        for (var index = 0; index < _lines.Count; index++)
        {
            _lines[index].UpdateText(index < lines.Count ? lines[index] : string.Empty);
        }
    }

    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();
        _childrenReady = true;

        Rebuild();
    }

    private void Rebuild()
    {
        ElementPoolService.CloseAllChildren(this);
        _lines.Clear();

        var width = _contentWidth - ContentPadding.X * 2;
        var size = new Vector2(width, RowHeight);
        for (var index = 0; index < AreaCellReadout.LineCount; index++)
        {
            var line = ElementPoolService.CreateElement<TextWindow>(this, new ElementOptions
            {
                Layout = new ElementLayoutOptions { Size = size, MinimumSize = size, MaximumSize = size, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = Color.Transparent },
                Text = new TextOptions { Text = string.Empty, TextColor = ActivatableDetailsSections.BodyTextColor },
            });
            line.ContentFont = FontService.GetFont(UIEngine.UI.Chrome.FontChrome.DefaultFontSize);
            AddChild(line);
            _lines.Add(line);
        }

        ShowLines(_shownLines);
    }
}
