using Engine.ECS.Components.Stores;
using Game.Modules.Currency.Components;
using Microsoft.Xna.Framework;
using Presentation.UI.ColorPalettes;

namespace Presentation.UI;

/// <summary>
/// Shared "Gold : X    Credits : Y" footer row -- InventoryManagementWindow (the player's own
/// inventory) and SecondaryInventoryWindow (whatever's being looted, corpse or container) each
/// build one via Build, then call Format on their own Update to refresh it, matching
/// SecondaryInventoryWindow's own AddSummaryLine sizing conventions (a single fixed-height row,
/// not the generic pinned-footer primitive TODO.md's Element footer entry describes -- this is
/// two hand-built copies, tolerable until a third consumer justifies that abstraction). currencyPool
/// is optional (mirrors every other optional-module pool pattern in Presentation, e.g. MapWindow's
/// _deadPool) so a window built without CurrencyModule registered (older tests, a stripped-down
/// mod configuration) still renders "Gold : 0    Credits : 0" instead of throwing.
/// </summary>
public static class CurrencyRow
{
    public const float Height = 20f;

    public static TextWindow Build(Window parent, ElementPoolService elementPoolService, float y, float width)
    {
        var row = elementPoolService.CreateElement<TextWindow>(parent, new ElementOptions
        {
            Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
            Layout = new ElementLayoutOptions { RelativePosition = new Vector2(0, y), Size = new Vector2(width, Height), DisplayMode = ElementDisplayMode.Fixed },
            Chrome = new ElementChromeOptions { ShowBorder = false, ShowTitle = false, CanUserFocus = false },
            Content = new ElementContentOptions { ContentColor = WindowPalette.PanelContentColor },
            Text = new TextOptions { TextColor = WindowPalette.BodyTextColor },
        });
        parent.AddChild(row);
        row.UpdateText(Format(null, -1));
        return row;
    }

    public static string Format(PackedComponentPool<CurrencyComponent>? currencyPool, int entityId)
    {
        var gold = 0;
        var credits = 0;
        if (currencyPool?.TryGetReadonly(entityId, out var currency) == true)
        {
            gold = currency.Gold;
            credits = currency.Credits;
        }

        return $"Gold : {gold}    Credits : {credits}";
    }
}
