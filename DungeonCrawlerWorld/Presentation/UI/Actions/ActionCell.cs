using Engine.Utilities;
using FontStashSharp;
using Game.Modules.Actions;
using Microsoft.Xna.Framework;
using Presentation.Input.DragDrop;
using Presentation.Rendering;
using Presentation.UI.Chrome;
using Presentation.UI.ColorPalettes;
using Presentation.UI.Content;
using UIEngine;
using UIEngine.Fonts;
using UIEngine.Input;
using UIEngine.Rendering;
using UIEngine.UI;

namespace Presentation.UI.Actions;

/// <summary>What an Actions window cell stands for: one of the player's actions, or a menu command.</summary>
public readonly record struct ActionCellSubject(Guid? ActionId, Guid? MenuCommandId)
{
    public static ActionCellSubject Action(Guid actionId) => new(actionId, null);

    public static ActionCellSubject MenuCommand(Guid menuCommandId) => new(null, menuCommandId);
}

/// <summary>One square of the Actions window's grid: an action's or menu command's icon, drawn as a hotbar slot holding it draws it.</summary>
/// <remarks>
/// The cooldown wedge (ActionSlotFill), what using it takes (bottom left, in each resource's colour), the lit marker of a
/// toggle that is on, and grey while it can't be used now. What changes from frame to frame is set by its grid every frame
/// (ActionGridContent), never at a rebuild, since none of it changes the set of actions. A press picks up what it shows, to
/// bind to a hotbar slot (DragPayloadOrigin.ActionCell).
/// </remarks>
public sealed class ActionCell(ElementServices services, SpriteSheetService spriteSheetService) : Element(services), IContentDragSource, IShowsActions
{
    private const float BadgePadding = 3f;

    private readonly RadialFillRenderer _icon = new(spriteSheetService);
    private readonly KeyedCache<float, string> _manaCostText = new();
    private readonly TextSizeCache _manaCostTextSize = new();
    private readonly KeyedCache<float, string> _healthCostText = new();
    private readonly TextSizeCache _healthCostTextSize = new();

    private Color _glyphColor;
    private FontStashTextMeasurer _glyphFontMeasurer = null!;
    private SpriteFontBase _costFont = null!;
    private SpriteFontBase _nameFont = null!;

    /// <summary>The name as drawn in Card view, shortened to the cell.</summary>
    private readonly KeyedCache<(string Name, float Width, SpriteFontBase Font), string> _truncatedName = new();

    public ActionCellSubject Subject { get; private set; }

    /// <summary>The action's or menu command's name -- what hovering names above the cursor.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>How much of the icon the cooldown wedge covers, 0 to 1.</summary>
    public float FillPercentage { get; set; }

    /// <summary>What using it takes now; free for a menu command.</summary>
    public ActivationCostTotals Costs { get; set; }

    /// <summary>Whether the player can't use it now (its blocker, or a menu command that can't run): the icon is greyed.</summary>
    public bool IsBlocked { get; set; }

    /// <summary>Whether it is a toggle that is on.</summary>
    public bool IsToggledOn { get; set; }

    /// <summary>Whether the open context menu is about what this cell shows -- opened for it, or for a hotbar slot bound to it.</summary>
    public bool IsContextMenuTarget { get; set; }

    /// <summary>Action compare mode's state for this cell, set every frame by its grid: Eligible glows green (it shares the anchor's activator), Ineligible greys the icon, None (not comparing) draws neither -- as an inventory cell's.</summary>
    public CellCompareState CompareState { get; set; }

    /// <summary>The key of the hotbar slot bound to what this cell shows, drawn over the icon as the slot draws it; null when no slot with a key holds it.</summary>
    public string? KeyLabel { get; set; }

    /// <summary>Whether Action Details shows what this cell shows as selected (MapViewState.SelectedActionId, SelectedMenuCommandId).</summary>
    public bool IsSelected { get; set; }

    /// <summary>Whether it is drawn as a card (GridView.Card: wider than tall) -- the icon at its left, then the name, and below it the costs -- rather than an icon square.</summary>
    public bool IsCard { get; private set; }

    public void Configure(ActionCellSubject subject, string name, string? spriteName, string glyph, Color glyphColor, Vector2 cellSize)
    {
        Subject = subject;
        Name = name;
        _icon.Sprite = spriteSheetService.Sprites.FirstOrNull(spriteName);
        _icon.Glyph = glyph;
        _glyphColor = glyphColor;
        _icon.BackgroundColor = Color.Transparent;
        FillPercentage = 0f;
        Costs = default;
        IsBlocked = false;
        IsToggledOn = false;
        IsContextMenuTarget = false;
        IsSelected = false;
        KeyLabel = null;
        CompareState = CellCompareState.None;
        _glyphFontMeasurer = FontService.GetTextMeasurer(FontService.GetFont((int)(cellSize.Y * GameFontChrome.InventoryStackIconGlyphFontFraction)));
        _costFont = FontService.GetFont((int)(cellSize.Y * GameFontChrome.HotbarOverlayFontFraction));
        _nameFont = FontService.GetFont((int)(cellSize.Y * GameFontChrome.InventoryStackBadgeFontFraction));
        IsCard = cellSize.X > cellSize.Y;
    }

    public override void DrawContent(GameTime gameTime)
    {
        var spriteBatch = Render.Batch;
        var bounds = LocalContentRectangle;

        GridSquareRenderer.DrawBase(spriteBatch, bounds);
        GridSquareRenderer.DrawStateOverlay(spriteBatch, bounds,
            IsContextMenuTarget ? GridSquareState.ContextMenuTarget
            : IsSelected ? GridSquareState.Selected
            : IsPointerOver ? GridSquareState.Hovered
            : GridSquareState.Normal);

        if (CompareState == CellCompareState.Eligible)
        {
            GlowRenderer.Draw(spriteBatch, bounds, InventoryItemStackCell.CompareEligibleGlowColor, GlowMode.ExteriorFade);
            GlowRenderer.Draw(spriteBatch, bounds, InventoryItemStackCell.CompareEligibleGlowColor, GlowMode.InteriorFade);
        }

        var isGreyedOut = IsBlocked || CompareState == CellCompareState.Ineligible;
        _icon.SpriteTint = isGreyedOut ? Color.Gray : Color.White;
        _icon.GlyphColor = isGreyedOut ? Color.Gray : _glyphColor;
        _icon.FillPercentage = FillPercentage;
        var iconBounds = IsCard ? new Rectangle(bounds.X, bounds.Y, bounds.Height, bounds.Height) : bounds;
        _icon.Draw(spriteBatch, _glyphFontMeasurer, iconBounds);

        if (IsToggledOn)
        {
            ToggleActiveMarker.Draw(spriteBatch, iconBounds);
        }

        if (KeyLabel is { } keyLabel)
        {
            HotkeyKeyLabel.Draw(spriteBatch, _costFont, keyLabel, iconBounds);
        }

        var costsLeft = bounds.X + BadgePadding;
        if (IsCard)
        {
            costsLeft = iconBounds.Right + BadgePadding;
            DrawCardName(spriteBatch, bounds, costsLeft, isGreyedOut);
        }

        if (!Costs.IsFree)
        {
            var x = DrawCost(spriteBatch, bounds, costsLeft, Costs.Mana, CostBadgePalette.Mana, _manaCostText, _manaCostTextSize);
            DrawCost(spriteBatch, bounds, x, Costs.Health, CostBadgePalette.Health, _healthCostText, _healthCostTextSize);
        }
    }

    /// <summary>Takes a click -- which selects it (its grid's Clicked handler), as an inventory cell's does -- so a quick second one pairs with it as a double-click (UiInputController pairs only with a click something handled).</summary>
    protected override bool OnContentClickAction(Point mousePosition) => true;

    public bool ShowsAction(Guid actionId) => Subject.ActionId == actionId;

    public bool ShowsMenuCommand(Guid menuCommandId) => Subject.MenuCommandId == menuCommandId;

    /// <summary>Picks up the action or menu command, at the cell's size, to bind to a hotbar slot.</summary>
    public bool TryBeginContentDrag(Point pressPosition, out ContentDragStart start)
    {
        start = new ContentDragStart(new DragPayload(DragPayloadOrigin.ActionCell, ActionId: Subject.ActionId, MenuCommandId: Subject.MenuCommandId), CurrentSize);
        return true;
    }

    /// <summary>Card view's name, in the top half from left (beside the icon), cut with "..." to fit.</summary>
    private void DrawCardName(SpriteEffectBatch spriteBatch, Rectangle bounds, float left, bool isGreyedOut)
    {
        var width = System.Math.Max(0f, bounds.Right - left - BadgePadding);
        var shownName = _truncatedName.Get((Name, width, _nameFont), FontService, static (key, fontService) => StringUtility.TruncateWithEllipsis(fontService.GetTextMeasurer(key.Font), key.Name, key.Width));
        LabelRenderer.DrawLeftAligned(spriteBatch, _nameFont, shownName, new Vector2(left, bounds.Y + BadgePadding), new Vector2(width, bounds.Height / 2f - BadgePadding), isGreyedOut ? Color.Gray : Color.White);
    }

    /// <summary>One resource's amount at x along the bottom edge, to one decimal place, or nothing when it is 0. Returns where the next one starts.</summary>
    private float DrawCost(SpriteEffectBatch spriteBatch, Rectangle bounds, float x, float amount, Color color, KeyedCache<float, string> text, TextSizeCache textSize)
    {
        if (amount <= 0)
        {
            return x;
        }

        var costText = text.Get(amount, static cost => cost.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        var size = textSize.SizeOf(_costFont, costText);
        ContrastTextRenderer.Draw(spriteBatch, _costFont, costText, new Vector2(x, bounds.Bottom - size.Y - BadgePadding), fillColor: color);
        return x + size.X + BadgePadding;
    }
}
