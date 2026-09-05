using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.Shops;
using Game.Modules.Shops.Components;
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
    Func<int?> getSecondaryTargetEntityId,
    EventBus? eventBus = null,
    // False for the trade window's own two currency footers (PLAN-trade-window.md) -- "10 [sprite]"
    // instead of "Gold : 10 [sprite]", the column being too narrow to spare the label.
    bool showLabels = true,
    // Null (every non-trade caller) -- CurrencyElement.Configure's own default (WindowPalette.
    // BodyTextColor). White for the trade window's own two currency footers, whose trade grid sits
    // on a transparent background too dark for the shared BodyTextColor to read against.
    Color? textColor = null) : IElementContent, IInventoryDropTarget
{
    public const float Height = 24f;

    private readonly PackedComponentPool<CurrencyComponent>? _currencyPool = componentManager.IsRegistered<CurrencyComponent>()
        ? componentManager.GetPackedPool<CurrencyComponent>()
        : null;

    private readonly PackedComponentPool<ShopComponent>? _shopPool = componentManager.IsRegistered<ShopComponent>()
        ? componentManager.GetPackedPool<ShopComponent>()
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
        element.Configure(entityId, type, size, showLabels, textColor);
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

    /// <summary>Mirrors InventoryGridContent.BuildItemContextMenu's exact Give/Take decision logic. "Give"/"Take" move only the clicked element's own currency; "Give All"/"Take All" move both regardless of which element was right-clicked. A shop never offers "Take"/"Take All" -- a player can Give Gold to a shop but never take it back out (see this class's own doc comment). A shop also never offers plain "Give": giving one currency type at a time isn't a meaningful shop gesture, so only "Give All" is shown once the secondary target carries a ShopComponent -- a corpse/container still gets both.</summary>
    private List<ContextMenuOption> BuildCurrencyContextMenu(CurrencyElement element)
    {
        List<ContextMenuOption> options = [];

        if (getSecondaryTargetEntityId() is not { } secondaryTargetEntityId)
        {
            return options;
        }

        var secondaryIsShop = _shopPool?.Has(secondaryTargetEntityId) == true;

        if (element.EntityId == world.PlayerEntityId && secondaryTargetEntityId != world.PlayerEntityId)
        {
            if (!secondaryIsShop)
            {
                options.Add(new ContextMenuOption("Give", null, Enabled: true, () => TransferOne(element, world.PlayerEntityId, secondaryTargetEntityId)));
            }

            options.Add(new ContextMenuOption("Give All", null, Enabled: true, () => TransferAll(world.PlayerEntityId, secondaryTargetEntityId)));
        }
        else if (element.EntityId == secondaryTargetEntityId && secondaryTargetEntityId != world.PlayerEntityId && !secondaryIsShop)
        {
            options.Add(new ContextMenuOption("Take", null, Enabled: true, () => TransferOne(element, secondaryTargetEntityId, world.PlayerEntityId)));
            options.Add(new ContextMenuOption("Take All", null, Enabled: true, () => CurrencyActions.TryTransferAll(componentManager, secondaryTargetEntityId, world.PlayerEntityId)));
        }

        return options;
    }

    /// <summary>Moves one currency type's whole balance through ShopActions.TryGiveCurrencyToShop -- the shared chokepoint every "give currency to a shop" gesture (this context menu, a direct currency drag, or completing a trade) routes through, so GoldGivenToShopEvent (the "Angel Investor" achievement's own trigger) fires the same way regardless of which gesture the player used. Take never reaches the shop branch: destinationEntityId is always world.PlayerEntityId on that path, which never carries ShopComponent.</summary>
    private void TransferOne(CurrencyElement element, int sourceEntityId, int destinationEntityId) =>
        ShopActions.TryGiveCurrencyToShop(componentManager, _shopPool, eventBus, sourceEntityId, destinationEntityId, element.Type);

    /// <summary>"Give All" -- same chokepoint as TransferOne, once per CurrencyType (mirrors CurrencyActions.TryTransferAll's own "iterate every type" shape) so Credits still move even when there's no Gold to trigger the achievement.</summary>
    private void TransferAll(int sourceEntityId, int destinationEntityId)
    {
        foreach (var type in Enum.GetValues<CurrencyType>())
        {
            ShopActions.TryGiveCurrencyToShop(componentManager, _shopPool, eventBus, sourceEntityId, destinationEntityId, type);
        }
    }
}
