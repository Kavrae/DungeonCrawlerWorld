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
/// read-only TextWindow. Implements IInventoryDropTarget the same way InventoryGridContent does
/// (Tag set on the *parent* window, since both elements are its direct children -- mirrors the old
/// CurrencyRow.Build's own parent.AddChild(row) convention), so a drag dropped anywhere in this
/// row resolves to EntityId regardless of whether it's an item stack (UiInputController's own
/// "currency row as a drag destination too" widening) or a currency element.
/// </summary>
public sealed class CurrencyRowContent(
    ComponentManager componentManager,
    World world,
    ContextMenuController contextMenuController,
    ElementPoolService elementPoolService,
    FontService fontService,
    LabelRenderer labelRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    Func<int?> getSecondaryTargetEntityId) : IInventoryDropTarget
{
    public const float Height = 24f;

    private readonly PackedComponentPool<CurrencyComponent>? _currencyPool = componentManager.IsRegistered<CurrencyComponent>()
        ? componentManager.GetPackedPool<CurrencyComponent>()
        : null;

    private CurrencyElement _goldElement = null!;
    private CurrencyElement _creditsElement = null!;

    public int EntityId { get; private set; }

    public void Build(Window parent, int entityId, float y, float width)
    {
        EntityId = entityId;
        parent.Tag = this;

        var halfWidth = width / 2f;
        _goldElement = CreateElement(parent, entityId, CurrencyType.Gold, new Vector2(0, y), new Vector2(halfWidth, Height));
        _creditsElement = CreateElement(parent, entityId, CurrencyType.Credits, new Vector2(halfWidth, y), new Vector2(width - halfWidth, Height));
    }

    private CurrencyElement CreateElement(Window parent, int entityId, CurrencyType type, Vector2 relativePosition, Vector2 size)
    {
        var element = elementPoolService.CreateElement<CurrencyElement>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = relativePosition, Size = size, DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = Color.Transparent },
        });
        element.Configure(entityId, type, size);
        parent.AddChild(element);
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

    /// <summary>Called every frame the owning window is alive -- rectangle hit-test against the live mouse position, the same shape InventoryGridContent.UpdateHover/FindHoverCandidate uses.</summary>
    public void UpdateHover(MouseState mouseState)
    {
        var mousePosition = new Point(mouseState.X, mouseState.Y);
        _goldElement.IsHovered = _goldElement.Rectangle.Contains(mousePosition);
        _creditsElement.IsHovered = _creditsElement.Rectangle.Contains(mousePosition);
    }

    /// <summary>Re-reads the current CurrencyComponent and pushes it into both elements -- no version watcher (Currency has none), the same unconditional-refresh cost the old CurrencyRow.Format paid every Update.</summary>
    public void RefreshAmounts()
    {
        var gold = 0;
        var credits = 0;
        if (_currencyPool?.TryGetReadonly(EntityId, out var currency) == true)
        {
            gold = currency.Gold;
            credits = currency.Credits;
        }

        _goldElement.SetAmount(gold);
        _creditsElement.SetAmount(credits);
    }

    /// <summary>InventoryManagementWindow's own resize handler only -- SecondaryInventoryWindow is sized once and never revisited, same as its grid/summary.</summary>
    public void Reposition(float y, float width)
    {
        var halfWidth = width / 2f;
        _goldElement.SetRelativePosition(new Vector2(0, y));
        _goldElement.SetSize(new Vector2(halfWidth, Height));
        _creditsElement.SetRelativePosition(new Vector2(halfWidth, y));
        _creditsElement.SetSize(new Vector2(width - halfWidth, Height));
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
