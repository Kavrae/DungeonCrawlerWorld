using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Replaces the old static CurrencyRow -- two independently hoverable/draggable/right-clickable
/// CurrencyElement children (Gold, Credits) sharing one fixed-height row, instead of a single
/// read-only TextWindow. Hosted via Window.SetFooterContent (see its own doc comment) rather than
/// built by hand -- Initialize mirrors InventoryGridContent.Initialize's own shape exactly,
/// including setting hostWindow.Tag = this so IInventoryDropTarget resolution
/// (UiInputController.FindDropTargetEntityId) finds this row regardless of whether the drop
/// landed on a grid cell or a currency element.
/// </summary>
public sealed class CurrencyRowContent(
    int entityId,
    ComponentManager componentManager,
    World world,
    ContextMenuController contextMenuController,
    ElementPoolService elementPoolService,
    FontService fontService,
    LabelRenderer labelRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    Func<int?> getSecondaryTargetEntityId) : IElementContent, IInventoryDropTarget
{
    public const float Height = 24f;

    private readonly PackedComponentPool<CurrencyComponent>? _currencyPool = componentManager.IsRegistered<CurrencyComponent>()
        ? componentManager.GetPackedPool<CurrencyComponent>()
        : null;

    private Window _hostWindow = null!;
    private CurrencyElement? _goldElement;
    private CurrencyElement? _creditsElement;

    public int EntityId => entityId;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        hostWindow.Tag = this;

        Reposition(hostWindow.ContentSize.X);
        hostWindow.Resized += (_) => Reposition(hostWindow.ContentSize.X);
    }

    /// <summary>Creates the two elements on first call (when _hostWindow's own ContentSize is already final -- see Window.OnChildrenInitialized), just repositions/resizes them on every later call (the host window's own Resized, e.g. the outer window being drag-resized).</summary>
    private void Reposition(float width)
    {
        var halfWidth = width / 2f;

        if (_goldElement is null || _creditsElement is null)
        {
            _goldElement = CreateElement(CurrencyType.Gold, new Vector2(0, 0), new Vector2(halfWidth, Height));
            _creditsElement = CreateElement(CurrencyType.Credits, new Vector2(halfWidth, 0), new Vector2(width - halfWidth, Height));
        }
        else
        {
            _goldElement.SetRelativePosition(new Vector2(0, 0));
            _goldElement.SetSize(new Vector2(halfWidth, Height));
            _creditsElement!.SetRelativePosition(new Vector2(halfWidth, 0));
            _creditsElement.SetSize(new Vector2(width - halfWidth, Height));
        }
    }

    private CurrencyElement CreateElement(CurrencyType type, Vector2 relativePosition, Vector2 size)
    {
        var element = elementPoolService.CreateElement<CurrencyElement>(_hostWindow, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        element.Configure(entityId, type, size);
        _hostWindow.AddChild(element);
        element.OnRightClicked = clickPosition =>
        {
            var options = BuildCurrencyContextMenu(element);
            if (options.Count > 0)
            {
                contextMenuController.Open(new Vector2(clickPosition.X, clickPosition.Y), options);
            }
        };
        return element;
    }

    public void Update(GameTime gameTime)
    {
        RefreshAmounts();
        UpdateHover(Mouse.GetState());
    }

    public void DrawContent(GameTime gameTime) { }

    /// <summary>Rectangle hit-test against the live mouse position, the same shape InventoryGridContent.UpdateHover/FindHoverCandidate uses. Elements are always non-null by the time Update runs Initialize builds them first.</summary>
    private void UpdateHover(MouseState mouseState)
    {
        var mousePosition = new Point(mouseState.X, mouseState.Y);
        _goldElement!.IsHovered = _goldElement.Rectangle.Contains(mousePosition);
        _creditsElement!.IsHovered = _creditsElement.Rectangle.Contains(mousePosition);
    }

    /// <summary>Re-reads the current CurrencyComponent and pushes it into both elements -- no version watcher (Currency has none), the same unconditional-refresh cost the old CurrencyRow.Format paid every Update.</summary>
    private void RefreshAmounts()
    {
        var gold = 0;
        var credits = 0;
        if (_currencyPool?.TryGetReadonly(EntityId, out var currency) == true)
        {
            gold = currency.Gold;
            credits = currency.Credits;
        }

        _goldElement!.SetAmount(gold);
        _creditsElement!.SetAmount(credits);
    }

    /// <summary>Mirrors InventoryGridContent.BuildItemContextMenu's exact Give/Take decision logic. "Give"/"Take" move only the clicked element's own currency; "Give All"/"Take All" move both regardless of which element was right-clicked.</summary>
    private List<ContextMenuOption> BuildCurrencyContextMenu(CurrencyElement element)
    {
        List<ContextMenuOption> options = [];

        if (getSecondaryTargetEntityId() is not { } secondaryTargetEntityId)
        {
            return options;
        }

        if (element.EntityId == world.PlayerEntityId && secondaryTargetEntityId != world.PlayerEntityId)
        {
            options.Add(new ContextMenuOption("Give", null, Enabled: true, () => TransferOne(element, world.PlayerEntityId, secondaryTargetEntityId)));
            options.Add(new ContextMenuOption("Give All", null, Enabled: true, () => CurrencyActions.TryTransferAll(componentManager, world.PlayerEntityId, secondaryTargetEntityId)));
        }
        else if (element.EntityId == secondaryTargetEntityId && secondaryTargetEntityId != world.PlayerEntityId)
        {
            options.Add(new ContextMenuOption("Take", null, Enabled: true, () => TransferOne(element, secondaryTargetEntityId, world.PlayerEntityId)));
            options.Add(new ContextMenuOption("Take All", null, Enabled: true, () => CurrencyActions.TryTransferAll(componentManager, secondaryTargetEntityId, world.PlayerEntityId)));
        }

        return options;
    }

    private void TransferOne(CurrencyElement element, int sourceEntityId, int destinationEntityId) =>
        CurrencyActions.TryTransfer(componentManager, sourceEntityId, destinationEntityId, element.Type);
}
