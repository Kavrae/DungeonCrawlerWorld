using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Grid of item-stack icons for one entity's inventory -- one InventoryItemStackCell per stack,
/// no empty filler cells, wraps to hostWindow's width and scrolls vertically without limit (the
/// host tab body window already has CanUserScrollVertical -- see TabbedContent). Rebuilds
/// (destroy-all, recreate-all -- cheap, since this only fires on an actual inventory mutation,
/// not every frame) whenever the pool's per-entity version changes.
/// </summary>
public sealed class InventoryGridContent(
    ComponentManager componentManager,
    ItemCatalog itemCatalog,
    ElementPoolService elementPoolService,
    FontService fontService,
    GlyphRenderer glyphRenderer,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    int entityId) : IElementContent
{
    public static readonly Vector2 CellSize = new(24, 24);
    private const float CellGap = 1f;

    private static readonly Color DisabledCellColor = Color.Gray;

    private readonly MultiComponentPool<InventoryItemStackComponent> _stacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly List<InventoryItemStackComponent> _reusableStacks = [];

    private readonly VersionWatcher _versionWatcher = new();

    private Window _hostWindow = null!;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        hostWindow.Resized += OnHostWindowResized;
    }

    public void Update(GameTime gameTime)
    {
        if (!_versionWatcher.HasChanged(_stacks.GetEntityVersion(entityId)))
        {
            return;
        }

        RebuildCells();
    }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        // Nothing to draw directly -- every stack is its own child InventoryItemStackCell,
        // which Window already draws as part of its own child-element loop.
    }

    /// <summary>Removes every cell -- called by TabbedContent when this tab is switched away from.</summary>
    public void Deactivate() => elementPoolService.CloseAllChildren(_hostWindow);

    private void OnHostWindowResized(Element _) => RebuildCells();

    private void RebuildCells()
    {
        elementPoolService.CloseAllChildren(_hostWindow);

        InventoryQueries.CopyStacksForEntity(_stacks, entityId, _reusableStacks);

        var columns = ComputeColumnCount();

        for (var i = 0; i < _reusableStacks.Count; i++)
        {
            var stack = _reusableStacks[i];
            if (!itemCatalog.TryGet(stack.ItemDefinitionId, out var definition))
            {
                continue;
            }

            var column = i % columns;
            var row = i / columns;
            var position = new Vector2(column * (CellSize.X + CellGap), row * (CellSize.Y + CellGap));

            var cell = elementPoolService.CreateElement<InventoryItemStackCell>(_hostWindow, new ElementOptions
            {
                Hierarchy = new ElementHierarchyOptions { CanContainChildren = false },
                Layout = new ElementLayoutOptions { RelativePosition = position, Size = CellSize, DisplayMode = ElementDisplayMode.Fixed },
                Chrome = new ElementChromeOptions { ShowBorder = true, CanUserFocus = false },
                Content = new ElementContentOptions { ContentColor = stack.IsDisabled ? DisabledCellColor : Color.White },
            });
            cell.Configure(stack.ItemDefinitionId, definition.SpriteName, definition.Glyph, definition.GlyphColor, stack.Quantity, stack.IsDisabled, CellSize);
            _hostWindow.AddChild(cell);
        }
    }

    private int ComputeColumnCount() =>
        System.Math.Max(1, (int)((_hostWindow.ContentSize.X + CellGap) / (CellSize.X + CellGap)));
}
