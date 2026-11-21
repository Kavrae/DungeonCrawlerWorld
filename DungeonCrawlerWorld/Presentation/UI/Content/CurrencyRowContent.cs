using Game.Modules.Currency;
using Game.Modules.Currency.Components;
using Game.Modules.ItemPiles;
using Game.Modules.Shops;
using Game.Views;
using Game.World;
using Microsoft.Xna.Framework;
using Presentation.Input.DragDrop;
using Presentation.UI.Inventory;
using UIEngine.Input;
using UIEngine.UI;

namespace Presentation.UI.Content;

/// <summary>
/// Replaces the old static CurrencyRow -- two independently hoverable/draggable/right-clickable
/// CurrencyElement children (Gold, Credits) sharing one fixed-height row, instead of a single
/// read-only TextWindow. Hosted via Window.SetFooterContent (see its own doc comment) rather than
/// built by hand -- Initialize mirrors InventoryGridContent.Initialize's own shape exactly,
/// including naming itself as hostWindow.DropTarget, so a drop on the row or either element lands
/// in entityId's inventory.
/// </summary>
public sealed class CurrencyRowContent(
    int entityId,
    InventoryServices inventoryServices,
    World world,
    ContextMenuController contextMenuController,
    ElementPoolService elementPoolService,
    Func<int?> getSecondaryTargetEntityId,
    // False for the trade window's own two currency footers -- "10 [sprite]"
    // instead of "Gold : 10 [sprite]", the column being too narrow to spare the label.
    bool showLabels = true,
    CurrencyRowButton? rowButton = null,
    bool alwaysStacksAmounts = false) : IElementContent, IContentDropTarget
{
    private readonly CurrencyView _currencyView = inventoryServices.CurrencyView;
    private readonly ShopView _shopView = inventoryServices.ShopView;
    private readonly CurrencyCommands _currencyCommands = inventoryServices.CurrencyCommands;
    private readonly ShopCommands _shopCommands = inventoryServices.ShopCommands;

    public const float Height = 24f;

    /// <summary>The height with each currency on its own row.</summary>
    public const float StackedHeight = Height * 2;

    private const int LargestAmountSharingARow = 9999;

    private Window _hostWindow = null!;
    private CurrencyElement? _goldElement;
    private CurrencyElement? _creditsElement;
    private Button? _rowButtonElement;

    public int EntityId => entityId;

    /// <summary>The height the row needs for the amounts held now: one row, or one per currency when it always stacks or a row with a button holds an amount past four digits.</summary>
    public float RequiredHeight => StacksAmounts ? StackedHeight : Height;

    private bool StacksAmounts =>
        alwaysStacksAmounts ||
        (rowButton is not null &&
         _currencyView.TryGetCurrency(EntityId, out var currency) &&
         (currency.Gold > LargestAmountSharingARow || currency.Credits > LargestAmountSharingARow));

    private readonly InventoryDropTarget _dropTarget = new(entityId);

    /// <summary>A drop anywhere on this row lands in its entity's inventory -- an item as well as currency.</summary>
    public object? DropTargetFor(object payload, Point position) => _dropTarget;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        hostWindow.DropTarget = this;

        Reposition(hostWindow.ContentSize.X);
        hostWindow.Resized += (_) => Reposition(hostWindow.ContentSize.X);
    }

    /// <summary>Creates the two elements on first call (when _hostWindow's own ContentSize is already final -- see Window.OnChildrenInitialized), just repositions/resizes them on every later call (the host window's own Resized, e.g. the outer window being drag-resized).</summary>
    private void Reposition(float width)
    {
        var amountsWidth = width - (rowButton?.Width ?? 0f);
        var halfWidth = amountsWidth / 2f;
        var stacksAmounts = StacksAmounts;
        var goldSize = new Vector2(stacksAmounts ? amountsWidth : halfWidth, Height);
        var creditsPosition = stacksAmounts ? new Vector2(0, Height) : new Vector2(halfWidth, 0);
        var creditsSize = new Vector2(stacksAmounts ? amountsWidth : amountsWidth - halfWidth, Height);

        if (_goldElement is null || _creditsElement is null)
        {
            _goldElement = CreateElement(CurrencyType.Gold, new Vector2(0, 0), goldSize);
            _creditsElement = CreateElement(CurrencyType.Credits, creditsPosition, creditsSize);
            if (rowButton is not null)
            {
                _rowButtonElement = CreateRowButton(rowButton, new Vector2(amountsWidth, 0));
            }
        }
        else
        {
            _goldElement.SetRelativePosition(new Vector2(0, 0));
            _goldElement.SetSize(goldSize);
            _creditsElement!.SetRelativePosition(creditsPosition);
            _creditsElement.SetSize(creditsSize);
            _rowButtonElement?.SetRelativePosition(new Vector2(amountsWidth, 0));
        }
    }

    private Button CreateRowButton(CurrencyRowButton definition, Vector2 relativePosition)
    {
        var button = elementPoolService.CreateElement<Button>(_hostWindow, new ElementOptions
        {
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = new Vector2(definition.Width, Height), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = true, BorderStyle = BorderStyle.Outset, CanUserFocus = false },
        });
        button.SetText(definition.Label);
        button.Enabled = definition.CanPress();
        button.Clicked += _ => definition.Pressed();
        _hostWindow.AddChild(button);
        return button;
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
        element.Configure(entityId, type, size, showLabels);
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
        if (_rowButtonElement is not null)
        {
            _rowButtonElement.Enabled = rowButton!.CanPress();
        }
    }

    public void DrawContent(GameTime gameTime) { }

    /// <summary>Re-reads the current CurrencyComponent and pushes it into both elements -- no version watcher (Currency has none), the same unconditional-refresh cost the old CurrencyRow.Format paid every Update.</summary>
    private void RefreshAmounts()
    {
        var gold = 0;
        var credits = 0;
        if (_currencyView.TryGetCurrency(EntityId, out var currency))
        {
            gold = currency.Gold;
            credits = currency.Credits;
        }

        _goldElement!.SetAmount(gold);
        _creditsElement!.SetAmount(credits);
    }

    /// <summary>On the player's own row while no shop is open, "Drop" (all of the clicked currency) and "Drop All" (both) first. Then mirrors InventoryGridContent.BuildItemContextMenu's exact Give/Take decision logic. "Give"/"Take" move only the clicked element's own currency; "Give All"/"Take All" move both regardless of which element was right-clicked. A shop never offers "Take"/"Take All" -- a player can Give Gold to a shop but never take it back out (see this class's own doc comment). A shop also never offers plain "Give": giving one currency type at a time isn't a meaningful shop gesture, so only "Give All" is shown once the secondary target carries a ShopComponent -- a corpse/container still gets both.</summary>
    private List<DropdownOption> BuildCurrencyContextMenu(CurrencyElement element)
    {
        List<DropdownOption> options = [];
        var openSecondaryTargetEntityId = getSecondaryTargetEntityId();

        if (element.EntityId == world.PlayerEntityId && (openSecondaryTargetEntityId is not { } openTarget || !_shopView.IsTradingShop(openTarget)))
        {
            AddDropOption(options, "Drop", ItemDrop.AllOf(element.Type));
            AddDropOption(options, "Drop All", ItemDrop.AllCurrency);
        }

        if (openSecondaryTargetEntityId is not { } secondaryTargetEntityId)
        {
            return options;
        }

        var secondaryIsShop = _shopView.IsTradingShop(secondaryTargetEntityId);

        if (element.EntityId == world.PlayerEntityId && secondaryTargetEntityId != world.PlayerEntityId)
        {
            if (!secondaryIsShop)
            {
                options.Add(new DropdownOption("Give", null, Enabled: true, () => TransferOne(element, world.PlayerEntityId, secondaryTargetEntityId)));
            }

            options.Add(new DropdownOption("Give All", null, Enabled: true, () => TransferAll(world.PlayerEntityId, secondaryTargetEntityId)));
        }
        else if (element.EntityId == secondaryTargetEntityId && secondaryTargetEntityId != world.PlayerEntityId && !secondaryIsShop)
        {
            // Nothing to take reads disabled, not absent: an empty chest or pile still shows its row.
            _currencyView.TryGetCurrency(secondaryTargetEntityId, out var held);
            options.Add(new DropdownOption("Take", null, Enabled: AmountOf(held, element.Type) > 0, () => TransferOne(element, secondaryTargetEntityId, world.PlayerEntityId)));
            options.Add(new DropdownOption("Take All", null, Enabled: held.Gold > 0 || held.Credits > 0, () => _currencyCommands.TryTransferAll(secondaryTargetEntityId, world.PlayerEntityId)));
        }

        return options;
    }

    private static int AmountOf(CurrencyComponent currency, CurrencyType type) => type == CurrencyType.Gold ? currency.Gold : currency.Credits;

    /// <summary>Adds an option arming drop (IPlayerItemDrops), disabled while it can't be armed.</summary>
    private void AddDropOption(List<DropdownOption> options, string label, ItemDrop drop) =>
        options.Add(new DropdownOption(label, null, Enabled: inventoryServices.PlayerItemDrops.CanArmDrop(drop), () => inventoryServices.PlayerItemDrops.ArmDrop(drop)));

    /// <summary>Moves one currency type's whole balance through ShopCommands.TryGiveCurrencyToShop -- the shared chokepoint every "give currency to a shop" gesture (this context menu or a direct currency drag) routes through, so the gift is announced (ShopDealCompletedEvent, read by the "Angel Investor" and "Scammed" achievements) the same way regardless of which gesture the player used. Take never reaches the shop branch: destinationEntityId is always world.PlayerEntityId on that path, which never carries ShopComponent.</summary>
    private void TransferOne(CurrencyElement element, int sourceEntityId, int destinationEntityId) =>
        _shopCommands.TryGiveCurrencyToShop(sourceEntityId, destinationEntityId, element.Type);

    /// <summary>"Give All" -- same chokepoint as TransferOne, once per CurrencyType (mirrors CurrencyActions.TryTransferAll's own "iterate every type" shape) so Credits still move even when there's no Gold to trigger the achievement.</summary>
    private void TransferAll(int sourceEntityId, int destinationEntityId)
    {
        foreach (var type in Enum.GetValues<CurrencyType>())
        {
            _shopCommands.TryGiveCurrencyToShop(sourceEntityId, destinationEntityId, type);
        }
    }
}
