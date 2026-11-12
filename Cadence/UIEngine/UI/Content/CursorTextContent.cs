using Engine.Utilities;
using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Geometry;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;
using UIEngine.UI.ColorPalettes;

namespace UIEngine.UI.Content;

/// <summary>Everything written at the mouse cursor: a message beside it, brief or held, and a label above it.</summary>
/// <remarks>
/// <para>
/// The message is the cursor's toast: Show(text) fades after a second (TextBox's "Copied"), ShowPersistent holds until its
/// owner hides it (Item Details Comparison's "Select next item...", a drop's "Drop"). The two share one line, so showing
/// either replaces the other.
/// </para>
/// <para>
/// The label is a boxed line above the cursor while its owner keeps it shown -- the name of what's hovered. It is
/// independent of the message, and showing the same text every update measures nothing.
/// </para>
/// <para>
/// The held message and the label are each owner-checked (PopupOwnership), as the shared tooltip is. Hosted in a
/// zero-size, fully transparent User-tier window, like a drag ghost: it draws at the live mouse position rather than
/// relative to any window, and User is the topmost tier.
/// </para>
/// </remarks>
public sealed class CursorTextContent(PointerState pointerState, FontService fontService) : IElementContent
{
    /// <summary>Total time a brief message stays visible, including the fade -- roughly a standard toast duration.</summary>
    private static readonly int DisplayFrames = GameTiming.FramesForSeconds(1.0f);

    /// <summary>How much of DisplayFrames, at the end, is spent fading to transparent rather than fully opaque.</summary>
    private static readonly int FadeFrames = GameTiming.FramesForSeconds(0.3f);

    /// <summary>The message's offset from the live cursor position -- drawn near, not directly under, the pointer hotspot.</summary>
    private static readonly Vector2 MessageOffset = new(12, 12);

    /// <summary>Gap between the label's bottom edge and the cursor's hotspot.</summary>
    private const float LabelGapAboveCursor = 4f;

    /// <summary>The label's background margin around its text on every side.</summary>
    private static readonly Vector2 LabelTextPadding = new(4, 2);

    private static readonly Color TextColor = Color.White;

    private readonly SpriteFontBase _messageFont = fontService.GetFont(FontChrome.CursorTextFontSize);
    private readonly SpriteFontBase _labelFont = fontService.GetFont(FontChrome.DefaultFontSize);

    private Window _hostWindow = null!;

    private string _messageText = string.Empty;
    private int _messageRemainingFrames;
    private PopupOwnership _heldMessageOwnership;

    private string _labelText = string.Empty;
    private Vector2 _labelTextSize;
    private PopupOwnership _labelOwnership;

    /// <summary>Starts (or restarts) the display/fade countdown with the given text -- e.g. TextBox calling Show("Copied") after a successful Ctrl+C/Ctrl+X.</summary>
    /// <remarks>Ends a held message early: the two share the one line.</remarks>
    public void Show(string text)
    {
        _messageText = text;
        _messageRemainingFrames = DisplayFrames;
        _heldMessageOwnership.Clear();
    }

    /// <summary>Shows text beside the cursor on owner's behalf, with no countdown or fade, until that owner's HidePersistent or the next Show.</summary>
    public void ShowPersistent(object owner, string text)
    {
        _messageText = text;
        _heldMessageOwnership.Take(owner);
    }

    /// <summary>Ends the held message if owner is the one showing it; nothing happens otherwise, a brief message included.</summary>
    public void HidePersistent(object owner)
    {
        if (_heldMessageOwnership.TryRelease(owner))
        {
            _messageRemainingFrames = 0;
        }
    }

    /// <summary>The message showing now, or empty -- for tests.</summary>
    internal string ShownText => _heldMessageOwnership.IsHeld || _messageRemainingFrames > 0 ? _messageText : string.Empty;

    /// <summary>Shows text above the cursor on owner's behalf, taking the label over from whoever showed it before.</summary>
    public void ShowLabel(object owner, string text)
    {
        _labelOwnership.Take(owner);
        if (text == _labelText)
        {
            return;
        }

        _labelText = text;
        _labelTextSize = _labelFont.MeasureString(text);
    }

    /// <summary>Hides the label if owner is the one showing it; nothing happens otherwise.</summary>
    public void HideLabel(object owner) => _labelOwnership.TryRelease(owner);

    /// <summary>The label showing now, or empty -- for tests.</summary>
    public string ShownLabelText => _labelOwnership.IsHeld ? _labelText : string.Empty;

    public void Initialize(Window hostWindow) => _hostWindow = hostWindow;

    public void Update(GameTime gameTime)
    {
        if (_heldMessageOwnership.IsHeld)
        {
            return;
        }

        if (_messageRemainingFrames > 0)
        {
            _messageRemainingFrames--;
        }
    }

    public void DrawContent(GameTime gameTime)
    {
        DrawMessage();
        DrawLabel();
    }

    private void DrawMessage()
    {
        if (!_heldMessageOwnership.IsHeld && _messageRemainingFrames <= 0)
        {
            return;
        }

        var alpha = !_heldMessageOwnership.IsHeld && _messageRemainingFrames < FadeFrames ? (float)_messageRemainingFrames / FadeFrames : 1f;
        var cursor = pointerState.CursorPosition;
        var position = _hostWindow.ToContentLocal(new Vector2(cursor.X, cursor.Y)) + MessageOffset;
        LabelRenderer.Draw(_hostWindow.Render.Batch, _messageFont, _messageText, position, TextColor * alpha);
    }

    private void DrawLabel()
    {
        if (!_labelOwnership.IsHeld || _labelText.Length == 0)
        {
            return;
        }

        var backgroundSize = _labelTextSize + LabelTextPadding * 2;
        var cursor = pointerState.CursorPosition;
        var screenPosition = new Vector2(cursor.X - backgroundSize.X / 2f, cursor.Y - LabelGapAboveCursor - backgroundSize.Y);
        screenPosition = RectangleClamp.ClampInside(screenPosition, backgroundSize, _hostWindow.Render.ScreenSize);

        var position = _hostWindow.ToContentLocal(screenPosition);
        var background = new Rectangle((int)position.X, (int)position.Y, (int)backgroundSize.X, (int)backgroundSize.Y);
        _hostWindow.Render.Batch.FillRectangle(background, WindowPalette.PanelBackgroundColor);
        LabelRenderer.Draw(_hostWindow.Render.Batch, _labelFont, _labelText, position + LabelTextPadding, TextColor);
    }
}
