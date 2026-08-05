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
    private readonly List<InventoryItemStackCell> _cells = [];

    private Window _hostWindow = null!;
    private bool _hasBuiltOnce;
    private uint _lastSeenVersion;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        hostWindow.Resized += OnHostWindowResized;
    }

    public void Update(GameTime gameTime)
    {
        var currentVersion = _stacks.GetEntityVersion(entityId);
        if (_hasBuiltOnce && currentVersion == _lastSeenVersion)
        {
            return;
        }

        _hasBuiltOnce = true;
        _lastSeenVersion = currentVersion;
        RebuildCells();
    }

    public void DrawContent(GameTime gameTime, SpriteBatch spriteBatch, Texture2D unitRectangle)
    {
        // Nothing to draw directly -- every stack is its own child InventoryItemStackCell,
        // which Window already draws as part of its own child-element loop.
    }

    /// <summary>Removes every cell -- called by TabbedContent when this tab is switched away from.</summary>
    public void Deactivate()
    {
        foreach (var cell in _cells)
        {
            cell.Close();
        }

        _cells.Clear();
    }

    private void OnHostWindowResized(Element _) => RebuildCells();

    private void RebuildCells()
    {
        foreach (var cell in _cells)
        {
            cell.Close();
        }

        _cells.Clear();

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

            _cells.Add(cell);
        }
    }

    private int ComputeColumnCount() =>
        System.Math.Max(1, (int)((_hostWindow.ContentSize.X + CellGap) / (CellSize.X + CellGap)));
}
