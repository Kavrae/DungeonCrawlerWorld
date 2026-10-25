using FontStashSharp;
using Microsoft.Xna.Framework;
using UIEngine.Fonts;
using UIEngine.Rendering;
using UIEngine.UI.Chrome;

namespace UIEngine.UI;

/// <summary>One checkbox in a ToggleList: its label and whether it starts ticked.</summary>
public sealed record ToggleListItem(string Label, bool IsOn);

/// <summary>A column of checkboxes, optionally headed by an "All" checkbox that the others sit indented under.</summary>
/// <remarks>
/// All is ticked exactly when every item is: ticking or unticking it does the same to every item, and ticking or unticking
/// an item ticks or unticks All to match. Changed is raised once per click, after every checkbox shows the result; a caller
/// reads the items' states with IsItemOn, and shows states of its own with SetItemStates, which raises nothing.
/// </remarks>
public sealed class ToggleList(FontService fontService, ElementPoolService elementPoolService, UiRenderService renderService)
    : Window(fontService, elementPoolService, renderService)
{
    public const float DefaultRowHeight = 24f;

    /// <summary>How far the items sit right of All, so it reads as covering them.</summary>
    public const float ItemIndent = 20f;

    private readonly List<Toggle> _itemToggles = [];
    private IReadOnlyList<ToggleListItem> _items = [];
    private string? _allLabel;
    private Color _labelColor;
    private Color _onColor;
    private Color _offColor;
    private float _rowHeight = DefaultRowHeight;

    /// <summary>The All checkbox; null when the list has none.</summary>
    public Toggle? AllToggle { get; private set; }

    public int ItemCount => _items.Count;

    /// <summary>Raised after a click changed which checkboxes are ticked.</summary>
    public event Action? Changed;

    /// <summary>The height a list of itemCount items takes, with an All checkbox above them when hasAll.</summary>
    public static float HeightFor(int itemCount, bool hasAll, float rowHeight = DefaultRowHeight) => (itemCount + (hasAll ? 1 : 0)) * rowHeight;

    public override void Build(Element? parent, ElementOptions options)
    {
        base.Build(parent, options);

        ContentPadding = Vector2.Zero;
        _itemToggles.Clear();
        _items = [];
        _allLabel = null;
        _rowHeight = DefaultRowHeight;
        AllToggle = null;
    }

    /// <summary>Must be called after CreateElement but before Initialize, the usual Configure contract.</summary>
    /// <param name="allLabel">The All checkbox's label, or null for no All checkbox.</param>
    public void Configure(IReadOnlyList<ToggleListItem> items, string? allLabel, Color labelColor, Color onColor, Color offColor, float rowHeight = DefaultRowHeight)
    {
        _items = items;
        _allLabel = allLabel;
        _labelColor = labelColor;
        _onColor = onColor;
        _offColor = offColor;
        _rowHeight = rowHeight;
    }

    /// <summary>Item index's checkbox.</summary>
    public Toggle ItemToggle(int index) => _itemToggles[index];

    public bool IsItemOn(int index) => _itemToggles[index].IsOn;

    /// <summary>Shows states, one per item, and All to match, raising nothing.</summary>
    public void SetItemStates(IReadOnlyList<bool> states)
    {
        for (var index = 0; index < _itemToggles.Count; index++)
        {
            _itemToggles[index].SetIsOn(states[index]);
        }

        AllToggle?.SetIsOn(AreAllItemsOn());
    }

    /// <summary>Builds the checkboxes once ContentSize is real -- the same hook GridControl builds its row in.</summary>
    protected override void OnChildrenInitialized()
    {
        base.OnChildrenInitialized();

        var font = FontService.GetFont(FontChrome.DefaultFontSize);
        var y = 0f;
        if (_allLabel is not null)
        {
            AllToggle = CreateToggle(font, _allLabel, AreAllItemsStartingOn(), new Vector2(0, y), OnAllToggled);
            y += _rowHeight;
        }

        var indent = _allLabel is null ? 0 : ItemIndent;
        foreach (var item in _items)
        {
            _itemToggles.Add(CreateToggle(font, item.Label, item.IsOn, new Vector2(indent, y), _ => OnItemToggled()));
            y += _rowHeight;
        }
    }

    private Toggle CreateToggle(SpriteFontBase font, string label, bool isOn, Vector2 position, Action<bool> onToggled)
    {
        var toggle = ElementPoolService.CreateElement<Toggle>(this, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = position, Size = new Vector2(System.Math.Max(1, ContentSize.X - position.X), _rowHeight), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        toggle.Configure(label, LabelPosition.East, isOn, _onColor, _offColor, _labelColor, font, onToggled);
        AddChild(toggle);
        return toggle;
    }

    private void OnAllToggled(bool isOn)
    {
        foreach (var toggle in _itemToggles)
        {
            toggle.SetIsOn(isOn);
        }

        Changed?.Invoke();
    }

    private void OnItemToggled()
    {
        AllToggle?.SetIsOn(AreAllItemsOn());
        Changed?.Invoke();
    }

    private bool AreAllItemsOn() => _itemToggles.Count > 0 && _itemToggles.TrueForAll(toggle => toggle.IsOn);

    private bool AreAllItemsStartingOn() => _items.Count > 0 && _items.All(item => item.IsOn);
}
