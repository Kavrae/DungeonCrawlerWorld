using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;
using Presentation.UI;

namespace Tests.Presentation;

/// <summary>
/// TextBox's own behavior in isolation, via its internal HandleTextInput/HandleKeyPress/
/// HandleHotkeys hooks directly (see Window's InternalsVisibleTo) -- independent of
/// GameInputController's routing, which has its own coverage in GameInputControllerTests.
/// </summary>
[TestClass]
public sealed class TextBoxTests
{
    private static WindowService CreateWindowService() => new(new FontService("Fonts"), new GlyphRenderer());

    private static TextBox CreateTextBox(WindowService windowService, bool multiline = false)
    {
        var textBox = windowService.CreateWindow<TextBox>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 100), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Multiline = multiline },
        });
        textBox.Initialize();
        return textBox;
    }

    /// <summary>A multiline box with an explicit MaximumSize.Y cap, for AutoSizeToContent tests. No border/title (both default off), so WindowCurrentSize.Y == content height exactly -- ContentFont.LineHeight * lines + TextWindow.LinePadding(3) * 2, the same formula TextWindowScrollingTests already hardcodes 3 for.</summary>
    private static TextBox CreateGrowableMultilineTextBox(WindowService windowService, float maximumHeight)
    {
        var textBox = windowService.CreateWindow<TextBox>(null, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = new Vector2(300, 500), MaximumSize = new Vector2(300, maximumHeight), DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Multiline = true },
        });
        textBox.Initialize();
        return textBox;
    }

    [TestMethod]
    public void TypingCharacters_AppendsToText()
    {
        var textBox = CreateTextBox(CreateWindowService());

        textBox.HandleTextInput('h');
        textBox.HandleTextInput('i');

        Assert.AreEqual("hi", textBox.OriginalText);
    }

    [TestMethod]
    public void TypingControlCharacter_IsIgnored()
    {
        var textBox = CreateTextBox(CreateWindowService());

        textBox.HandleTextInput('\r');
        textBox.HandleTextInput('a');

        Assert.AreEqual("a", textBox.OriginalText);
    }

    [TestMethod]
    public void PressingBackspace_RemovesLastCharacter()
    {
        var textBox = CreateTextBox(CreateWindowService());
        textBox.HandleTextInput('h');
        textBox.HandleTextInput('i');

        textBox.HandleKeyPress(Keys.Back);

        Assert.AreEqual("h", textBox.OriginalText);
    }

    [TestMethod]
    public void PressingBackspace_OnEmptyText_DoesNotThrow()
    {
        var textBox = CreateTextBox(CreateWindowService());

        textBox.HandleKeyPress(Keys.Back);

        Assert.AreEqual(string.Empty, textBox.OriginalText);
    }

    [TestMethod]
    public void PressingEnter_RaisesTextSubmittedWithCurrentText()
    {
        var textBox = CreateTextBox(CreateWindowService());
        textBox.HandleTextInput('h');
        textBox.HandleTextInput('i');
        string? submitted = null;
        textBox.TextSubmitted += text => submitted = text;

        textBox.HandleHotkeys(new KeyboardState(Keys.Enter), new KeyboardState());

        Assert.AreEqual("hi", submitted);
    }

    [TestMethod]
    public void PressingShiftEnter_OnMultilineBox_InsertsNewlineInsteadOfSubmitting()
    {
        var textBox = CreateTextBox(CreateWindowService(), multiline: true);
        textBox.HandleTextInput('h');
        var submitted = false;
        textBox.TextSubmitted += _ => submitted = true;

        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());

        Assert.AreEqual("h\n", textBox.OriginalText);
        Assert.IsFalse(submitted);
    }

    [TestMethod]
    public void PressingShiftEnter_OnSingleLineBox_SubmitsInsteadOfInsertingNewline()
    {
        var textBox = CreateTextBox(CreateWindowService());
        textBox.HandleTextInput('h');
        string? submitted = null;
        textBox.TextSubmitted += text => submitted = text;

        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());

        Assert.AreEqual("h", submitted);
        Assert.AreEqual("h", textBox.OriginalText);
    }

    /// <summary>
    /// Regression guard for the confirmed StringUtility gap (see
    /// StringUtilityTests.SimpleWordWrap_EmbeddedNewlineNotAtChunkBoundary...): TextBox's own
    /// ReformatDisplayText override must keep a manually-typed line break exactly where it was
    /// typed, not let it drift mid-chunk.
    /// </summary>
    [TestMethod]
    public void TypingAcrossAManualNewline_KeepsTheLineBreakExactlyWhereItWasTyped()
    {
        var textBox = CreateTextBox(CreateWindowService(), multiline: true);

        textBox.HandleTextInput('h');
        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());
        textBox.HandleTextInput('i');

        Assert.AreEqual("h\ni", textBox.DisplayText.FormattedText);
    }

    [TestMethod]
    public void Initialize_MultilineBox_StartsAtTwoLineMinimumHeight()
    {
        var textBox = CreateGrowableMultilineTextBox(CreateWindowService(), maximumHeight: 1000);

        var expectedHeight = textBox.ContentFont.LineHeight * 2 + 3 * 2;
        Assert.AreEqual(expectedHeight, textBox.WindowCurrentSize.Y);
    }

    /// <summary>Two lines is still the minimum (no growth yet); the third line is what actually needs more room.</summary>
    [TestMethod]
    public void TypingTwoNewlines_DoesNotGrowUntilTheThirdLine()
    {
        var textBox = CreateGrowableMultilineTextBox(CreateWindowService(), maximumHeight: 1000);
        var twoLineHeight = textBox.WindowCurrentSize.Y;

        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());

        Assert.AreEqual(twoLineHeight, textBox.WindowCurrentSize.Y);
    }

    [TestMethod]
    public void TypingAThirdLine_GrowsHeightByExactlyOneLine()
    {
        var textBox = CreateGrowableMultilineTextBox(CreateWindowService(), maximumHeight: 1000);
        var twoLineHeight = textBox.WindowCurrentSize.Y;

        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());
        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());

        Assert.AreEqual(twoLineHeight + textBox.ContentFont.LineHeight, textBox.WindowCurrentSize.Y);
    }

    [TestMethod]
    public void GrowingPastMaximumSize_CapsHeightAndDoesNotExceedIt()
    {
        var windowService = CreateWindowService();
        var lineHeight = new FontService("Fonts").GetFont(8).LineHeight;
        var threeLineCap = lineHeight * 3 + 3 * 2;
        var textBox = CreateGrowableMultilineTextBox(windowService, threeLineCap);

        for (var index = 0; index < 4; index++)
        {
            textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());
        }

        Assert.AreEqual(threeLineCap, textBox.WindowCurrentSize.Y);
    }

    [TestMethod]
    public void SingleLineBox_HeightIsUnaffectedByTyping()
    {
        var textBox = CreateTextBox(CreateWindowService());
        var initialHeight = textBox.WindowCurrentSize.Y;

        textBox.HandleTextInput('a');
        textBox.HandleTextInput('b');
        textBox.HandleTextInput('c');

        Assert.AreEqual(initialHeight, textBox.WindowCurrentSize.Y);
    }

    /// <summary>
    /// Regression test for the reported bug, using the same pattern GameShellBootstrapper's
    /// quest-composer popup does: a Fixed (not WrapContent) parent explicitly resizing itself
    /// off the TextBox's own Resized event, using a chrome-overhead constant computed once up
    /// front. A WrapContent parent was tried first and rejected -- see AutoSizeToContent's own
    /// remarks and this method's sibling notes: Window.Measure overwrites a child's own
    /// MaximumSize with _parentWindow.ContentSize on every pass, and a WrapContent parent's
    /// ContentSize starts at ~(0,0) before it has ever measured a child -- so a WrapContent
    /// parent and a child whose growth cap depends on that same parent's ContentSize collapse
    /// each other to ~0 instead of settling on a real size. A Fixed parent has no such
    /// circularity: its own ContentSize is already stable before the child is ever measured.
    /// </summary>
    [TestMethod]
    public void FixedParent_ExplicitlyResizedOffTextBoxResizedEvent_ShrinksThenGrowsWithIt()
    {
        var windowService = CreateWindowService();
        var parentSize = new Vector2(300, 600);
        var textBoxMaximumSize = new Vector2(300, 500);
        var chromeOverhead = parentSize.Y - textBoxMaximumSize.Y;

        var parent = windowService.CreateWindow<Window>(null, new WindowOptions
        {
            Hierarchy = new WindowHierarchyOptions { CanContainChildWindows = true },
            Layout = new WindowLayoutOptions { Size = parentSize, DisplayMode = WindowDisplayMode.Fixed },
        });
        parent.Initialize();

        var textBox = windowService.CreateWindow<TextBox>(parent, new WindowOptions
        {
            Layout = new WindowLayoutOptions { Size = textBoxMaximumSize, MaximumSize = textBoxMaximumSize, DisplayMode = WindowDisplayMode.Fixed },
            Chrome = new WindowChromeOptions { CanUserScrollVertical = true },
            Text = new TextOptions { Multiline = true },
        });
        // Subscribed before AddChildWindow -- Initialize (called from within AddChildWindow)
        // is what fires the first Resized, shrinking the TextBox to its 2-line minimum, and
        // this must catch that first shrink too, not just later ones.
        textBox.Resized += _ => parent.SetSize(new Vector2(parent.WindowCurrentSize.X, textBox.WindowCurrentSize.Y + chromeOverhead));

        parent.AddChildWindow(textBox);
        var shrunkParentHeight = parent.WindowCurrentSize.Y;

        Assert.IsLessThan(600, shrunkParentHeight);

        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());
        textBox.HandleHotkeys(new KeyboardState(Keys.Enter, Keys.LeftShift), new KeyboardState());

        Assert.IsGreaterThan(shrunkParentHeight, parent.WindowCurrentSize.Y);
    }
}
