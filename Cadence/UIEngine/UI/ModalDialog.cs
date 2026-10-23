using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI;

/// <summary>A ModalDialog button: its label and what choosing it does.</summary>
public sealed record DialogButton(string Label, Action OnChosen);

/// <summary>What a ModalDialog asks: its title, its buttons (the cancel one is what Escape or the close button means), and its body -- a message, or content the caller fills.</summary>
/// <param name="CancelButtonIndex">The button closing the dialog any other way counts as.</param>
public sealed record ModalDialogRequest(string Title, IReadOnlyList<DialogButton> Buttons, int CancelButtonIndex)
{
    /// <summary>Text shown as the body when FillBody is null.</summary>
    public string? Message { get; init; }

    /// <summary>The size of the body above the buttons.</summary>
    public Vector2 BodySize { get; init; } = new(360, 80);

    /// <summary>Fills the body window with content of the caller's own -- a list to pick a replacement from.</summary>
    public Action<Window>? FillBody { get; init; }
}

/// <summary>
/// A question the rest of the window set waits on -- a body and a row of buttons, opened as a menu window so everything
/// behind it is dimmed and out of reach until one is chosen. Exactly one button's action runs: the one chosen, or the
/// cancel button's when the dialog is closed with Escape or its close button.
/// </summary>
/// <remarks>Opened only through Show. A chosen button closes the dialog on the update after the click, then runs.</remarks>
public sealed class ModalDialog(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Window(fontService, elementPoolService, renderService)
{
    public const float ButtonHeight = 26f;
    private const float ButtonMinimumWidth = 80f;
    private const float ButtonGap = 6f;
    private const float ButtonTextPadding = 16f;
    private const float BodyGap = 8f;

    private readonly SpriteFontBase _font = fontService.GetFont(FontChrome.DefaultFontSize);

    private ModalDialogRequest _request = null!;
    private bool _answered;
    private int? _pendingChoice;

    public Window Body { get; private set; } = null!;

    /// <summary>The button row, in the request's order.</summary>
    public IReadOnlyList<Button> Buttons => _buttons;

    private readonly List<Button> _buttons = [];

    /// <summary>Opens request as a dialog centred in screenBounds, in front of every other window.</summary>
    public static ModalDialog Show(ElementPoolService elementPoolService, UiLayerStack layers, Rectangle screenBounds, ModalDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.CancelButtonIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(request.CancelButtonIndex, request.Buttons.Count);

        var dialog = elementPoolService.CreateElement<ModalDialog>(null, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { Size = new Vector2(1, 1), MaximumSize = new Vector2(screenBounds.Width, screenBounds.Height), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowTitle = true, TitleText = request.Title, ShowBorder = true, CanUserClose = true, CanUserMove = true },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
        });
        dialog._request = request;
        dialog._answered = false;
        dialog._pendingChoice = null;
        dialog.Closed += _ =>
        {
            layers.Remove(UiLayer.DynamicHud, dialog);
            layers.CloseMenuWindow(dialog);
            dialog.AnswerOnce(request.CancelButtonIndex);
        };
        dialog.Initialize();
        dialog.Centre(screenBounds);
        layers.Add(UiLayer.DynamicHud, dialog);
        layers.OpenMenuWindow(dialog);
        return dialog;
    }

    public override void Initialize()
    {
        base.Initialize();

        _buttons.Clear();
        var buttonWidths = _request.Buttons.Select(button => System.Math.Max(ButtonMinimumWidth, _font.MeasureString(button.Label).X + ButtonTextPadding)).ToArray();
        var buttonRowWidth = buttonWidths.Sum() + ButtonGap * (buttonWidths.Length - 1);
        var contentSize = new Vector2(System.Math.Max(_request.BodySize.X, buttonRowWidth), _request.BodySize.Y + BodyGap + ButtonHeight);
        SetSize(contentSize + (CurrentSize - ContentSize));

        Body = ElementPoolService.CreateElement<Window>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = true },
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(contentSize.X, _request.BodySize.Y), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
        });
        Body.ContentPadding = Vector2.Zero;
        AddChild(Body);

        if (_request.FillBody is { } fillBody)
        {
            fillBody(Body);
        }
        else if (_request.Message is { } message)
        {
            AddMessage(message, contentSize.X);
        }

        var x = contentSize.X - buttonRowWidth;
        for (var buttonIndex = 0; buttonIndex < _request.Buttons.Count; buttonIndex++)
        {
            var capturedIndex = buttonIndex;
            var button = ElementPoolService.CreateElement<Button>(this, new ElementOptions
            {
                Layout = new ElementLayoutOptions { RelativePosition = new Vector2(x, _request.BodySize.Y + BodyGap), Size = new Vector2(buttonWidths[buttonIndex], ButtonHeight), DisplayMode = ElementDisplayMode.Fixed },
                Text = new TextOptions { Text = _request.Buttons[buttonIndex].Label },
            });
            button.Clicked += _ => _pendingChoice = capturedIndex;
            AddChild(button);
            _buttons.Add(button);
            x += buttonWidths[buttonIndex] + ButtonGap;
        }
    }

    private void AddMessage(string message, float width)
    {
        var text = ElementPoolService.CreateElement<TextWindow>(Body, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = Vector2.Zero, Size = new Vector2(width, _request.BodySize.Y), DisplayMode = ElementDisplayMode.Fixed, IsTransparent = true },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Text = new TextOptions { Text = message, TextColor = WindowPalette.BodyTextColor },
        });
        text.ContentFont = _font;
        Body.AddChild(text);
    }

    private void Centre(Rectangle screenBounds) =>
        SetRelativePosition(new Vector2(screenBounds.Center.X - CurrentSize.X / 2f, screenBounds.Center.Y - CurrentSize.Y / 2f));

    /// <summary>Chooses buttonIndex as a click on it would.</summary>
    public void Choose(int buttonIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(buttonIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(buttonIndex, _request.Buttons.Count);

        _pendingChoice = buttonIndex;
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (_pendingChoice is not { } choice)
        {
            return;
        }

        _pendingChoice = null;
        AnswerOnce(choice);
        Close();
    }

    private void AnswerOnce(int buttonIndex)
    {
        if (_answered)
        {
            return;
        }

        _answered = true;
        _request.Buttons[buttonIndex].OnChosen();
    }
}
