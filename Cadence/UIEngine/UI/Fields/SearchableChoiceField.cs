using UIEngine.UI.Content;

namespace UIEngine.UI.Fields;

/// <summary>
/// A text box that chooses one of a long list: a press opens the whole list under it, and typing narrows the list to the
/// choices holding what is typed. Choosing one shows its label. Text that matches no choice is a value of its own when
/// AllowsFreeText, and is put back to the chosen label otherwise.
/// </summary>
/// <remarks>
/// While typing, the list highlights only the choice the text names exactly, so Enter chooses it; with nothing
/// highlighted Enter closes the list and resolves the typed text. Typed text is also resolved once the box has lost focus
/// and its list has closed -- never while a press on the list is still choosing. Configure before Initialize, the usual
/// contract.
/// </remarks>
public sealed class SearchableChoiceField(ElementServices services, DropdownController dropdowns, CursorTextContent? cursorTextContent = null, ContextMenuController? contextMenuController = null)
    : TextBox(services, cursorTextContent, contextMenuController)
{
    private IReadOnlyList<string> _choices = [];
    private bool _allowsFreeText;
    private string _committedText = string.Empty;
    private bool _isSettingText;
    private bool _hasTypedText;

    /// <summary>The chosen choice's index, or -1 for none or free text.</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>The text the field stands for now: the chosen label, or the free text last committed.</summary>
    public string CommittedText => _committedText;

    /// <summary>Raised when the player chooses or commits a different value: the index chosen (-1 for free text) and the text.</summary>
    public event Action<int, string>? Chosen;

    public bool IsListOpen => dropdowns.IsOpen && ReferenceEquals(dropdowns.Owner, this);

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        TextChanged += OnTextChanged;
        TextSubmitted += _ => CommitTypedText();
    }

    /// <param name="selectedIndex">The chosen choice, or -1 for none.</param>
    /// <param name="freeText">With selectedIndex -1 and allowsFreeText: the text the field holds.</param>
    public void Configure(IReadOnlyList<string> choices, int selectedIndex, bool allowsFreeText, string? freeText = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(selectedIndex, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(selectedIndex, choices.Count);

        _choices = choices;
        _allowsFreeText = allowsFreeText;
        SelectedIndex = selectedIndex;
        _committedText = selectedIndex >= 0 ? choices[selectedIndex] : allowsFreeText ? freeText ?? string.Empty : string.Empty;
        _hasTypedText = false;
    }

    public override void Initialize()
    {
        base.Initialize();

        ShowText(_committedText);
    }

    /// <summary>Puts the committed text back over anything typed and closes the list; false when nothing was typed.</summary>
    public bool RevertTypedText()
    {
        if (!_hasTypedText)
        {
            return false;
        }

        _hasTypedText = false;
        CloseList();
        ShowText(_committedText);
        return true;
    }

    /// <summary>Closes the list and resolves anything typed, as Enter with nothing highlighted does; false when nothing was typed.</summary>
    public bool CommitTypedText()
    {
        if (!_hasTypedText)
        {
            return false;
        }

        CloseList();
        ResolveTypedText();
        return true;
    }

    protected override bool OnContentClickAction(Microsoft.Xna.Framework.Point mousePosition)
    {
        base.OnContentClickAction(mousePosition);
        if (AcceptsEdits && !IsListOpen)
        {
            OpenList(string.Empty);
        }

        return true;
    }

    public override void Update(Microsoft.Xna.Framework.GameTime gameTime)
    {
        base.Update(gameTime);

        if (_hasTypedText && !IsFocused && !IsListOpen)
        {
            ResolveTypedText();
        }
    }

    protected internal override void OnClosed()
    {
        CloseList();
        base.OnClosed();
    }

    private void OnTextChanged(string text)
    {
        if (_isSettingText || !IsFocused)
        {
            return;
        }

        _hasTypedText = true;
        OpenList(text);
    }

    /// <summary>Opens the choices holding filter: with no filter the chosen one marked, while typing only the one filter names exactly.</summary>
    private void OpenList(string filter)
    {
        var options = new List<DropdownOption>();
        var markedIndex = -1;
        for (var index = 0; index < _choices.Count; index++)
        {
            if (filter.Length > 0 && !_choices[index].Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var isMarked = filter.Length == 0
                ? index == SelectedIndex
                : string.Equals(_choices[index], filter, StringComparison.OrdinalIgnoreCase);
            if (isMarked)
            {
                markedIndex = options.Count;
            }

            var capturedIndex = index;
            options.Add(new DropdownOption(_choices[index], null, Enabled: true, () => Choose(capturedIndex)));
        }

        if (options.Count == 0)
        {
            CloseList();
            return;
        }

        dropdowns.OpenBelow(this, options, markedIndex, DropdownField.ListMaximumHeight);
    }

    private void CloseList()
    {
        if (IsListOpen)
        {
            dropdowns.Close();
        }
    }

    private void Choose(int index)
    {
        _hasTypedText = false;
        ShowText(_choices[index]);
        Commit(index, _choices[index]);
    }

    /// <summary>Typed text becomes the choice it names exactly, else free text when allowed, else the committed text comes back.</summary>
    private void ResolveTypedText()
    {
        if (!_hasTypedText || IsListOpen)
        {
            return;
        }

        _hasTypedText = false;
        var typed = OriginalText;
        for (var index = 0; index < _choices.Count; index++)
        {
            if (string.Equals(_choices[index], typed, StringComparison.OrdinalIgnoreCase))
            {
                Choose(index);
                return;
            }
        }

        if (_allowsFreeText)
        {
            Commit(-1, typed);
            return;
        }

        ShowText(_committedText);
    }

    private void Commit(int index, string text)
    {
        if (index == SelectedIndex && text == _committedText)
        {
            return;
        }

        SelectedIndex = index;
        _committedText = text;
        Chosen?.Invoke(index, text);
    }

    private void ShowText(string text)
    {
        _isSettingText = true;
        try
        {
            SetText(text);
        }
        finally
        {
            _isSettingText = false;
        }
    }
}
