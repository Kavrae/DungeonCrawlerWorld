using Engine.ECS.Components;
using Engine.ECS.Components.Stores;
using Engine.Events;
using FontStashSharp;
using Game.Blueprints;
using Game.Modules.Actions;
using Game.Modules.Actions.Activators;
using Game.Modules.Actions.Components;
using Game.Modules.Core.Components;
using Game.Modules.Inventory;
using Game.Modules.Inventory.Components;
using Game.Modules.Mana.Components;
using Game.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Presentation.Fonts;
using Presentation.Input;
using Presentation.Rendering;

namespace Presentation.UI.Content;

/// <summary>
/// Three fixed HotkeyCategory groups laid out left to right (see
/// HotkeySlotLayout) -- Base (3 slots, always present), DefaultAttack (1 slot, always present),
/// and Expansion (up to 20 slots as two 2x5 pages placed side by side -- page 2 to the right of
/// page 1, not stacked below -- growing as HotkeyExpansionUnlockComponent.UnlockedSlotCount
/// increases; see GetExpansionRowsVisible/GetExpansionPagesVisible). Base/DefaultAttack are
/// always vertically centered against Expansion's current height, and the whole bar's Size
/// changes as rows/pages are revealed, so this window resizes/repositions itself at runtime (see
/// RefreshLayoutIfChanged) rather than having a fixed Size the way most HUD windows do. Every
/// slot always renders (bound, unbound, or not-yet-unlocked alike) with a permanent
/// BorderStyle.FlatContrast border; the armed slot additionally gets GlowRenderer's outward glow
/// (see NotificationCenter's own unread-glow for the same primitive). A slot that's disabled for
/// any reason -- an unaffordable action, an unusable/out-of-stock item, or a not-yet-unlocked
/// Expansion slot in an already-revealed row/page -- draws its border, icon, and text overlays
/// all at DisabledSlotAlpha; the radial cooldown/lock wedge (RadialFillRenderer) stays scoped to
/// the icon itself regardless. Implements TODO.md's "Inventory and spell hotbar" and "Player
/// attack button or key" items.
/// </summary>
public sealed class HotbarContent(
    World world,
    MapViewState mapViewState,
    ComponentManager componentManager,
    EventBus eventBus,
    ActionCatalog actionCatalog,
    ItemCatalog itemCatalog,
    FontService fontService,
    SpriteSheetService spriteSheetService,
    SpriteRenderer spriteRenderer,
    Vector2 screenSize) : IElementContent
{
    public static readonly Vector2 SlotSize = new(HudMetrics.EntrySize.Y * 2.25f, HudMetrics.EntrySize.Y * 2.25f);

    private const int BaseSlotCount = 3;
    private const int ExpansionColumnsPerRow = 5;
    private const int MaxExpansionRows = 2;
    private const int MaxExpansionPages = 2;
    private const int SlotsPerExpansionPage = ExpansionColumnsPerRow * MaxExpansionRows;

    /// <summary>Fallback only -- every real entity that can open a hotbar gets one from PlayerBlueprint. Matches Expansion's old fixed slot count, so a missing component still shows a sensible bar rather than an empty/degenerate one.</summary>
    private const short DefaultUnlockedExpansionSlots = 10;

    private const float GlyphSizeFraction = 0.75f;
    private const float OverlayFontFraction = 0.3f;
    private const float CountdownFontFraction = 0.4375f;
    private Vector2 OverlayPadding = new(4f, 2f);
    private const float SlotGap = 1f;
    private const float GroupGap = 10f;
    private const float ExpansionPageGap = GroupGap;
    private const float CountdownTextGap = 2f;
    private const float DisabledSlotAlpha = 0.5f;
    private static readonly BorderThickness SlotBorderThickness = BorderThickness.Uniform(new Vector2(2, 2));

    private static readonly Color UnboundSlotColor = new(48, 48, 48);
    private static readonly Color BoundSlotBackgroundColor = Color.WhiteSmoke;
    private static readonly Color BoundSlotGlyphColor = Color.Black;
    private static readonly Color ArmedGlowColor = Color.Gold;
    private static readonly Color DragDropTargetGlowColor = Color.Gold;

    /// <summary>Distinct from ArmedGlowColor -- a bound item slot can be both armed and the Item Details window's current selection at once, and the two need to read apart. Matches InventoryItemStackCell.SelectedGlowColor.</summary>
    private static readonly Color SelectedGlowColor = Color.Cyan;

    /// <summary>Width for the Armed Hotkey Summary popup: 3 slots plus the 2 inter-slot gaps
    /// between them (see HotbarController.UpdateSummary, which centers this over whichever single
    /// slot it's currently showing).</summary>
    internal const int SummarySlotSpan = 3;
    internal static readonly float SummaryWidth = SummarySlotSpan * SlotSize.X + (SummarySlotSpan - 1) * SlotGap;

    /// <summary>
    /// Set by UiInputController while a content-drag (an inventory item cell, or an already-
    /// bound hotbar slot, picked up toward a new binding -- see its own doc comment) is in
    /// progress, so every slot glows to invite the drop -- any slot can accept any item, so
    /// there's no single "hovered" target the way a more typical drop zone would highlight.
    /// Cleared the moment the drag ends, drop accepted or not. Reuses GlowRenderer.Draw verbatim,
    /// the same primitive NotificationCenter's folder already uses for its own unread-glow --
    /// this is the pattern the later Equipment menu's own drop targets should follow too.
    /// </summary>
    public bool IsAcceptingDrag { get; set; }

    private readonly MultiComponentPool<ActionHotkeyBindingComponent> _actionHotkeyBindings = componentManager.GetMultiPool<ActionHotkeyBindingComponent>();
    private readonly MultiComponentPool<ItemHotkeyBindingComponent> _itemHotkeyBindings = componentManager.GetMultiPool<ItemHotkeyBindingComponent>();
    private readonly MultiComponentPool<ActionInstanceComponent> _actionInstances = componentManager.GetMultiPool<ActionInstanceComponent>();
    private readonly MultiComponentPool<InventoryItemStackComponent> _inventoryStacks = componentManager.GetMultiPool<InventoryItemStackComponent>();
    private readonly PackedComponentPool<ActionLockComponent> _actionLocks = componentManager.GetPackedPool<ActionLockComponent>();
    private readonly PackedComponentPool<PotionCooldownComponent> _potionCooldowns = componentManager.GetPackedPool<PotionCooldownComponent>();
    private readonly PackedComponentPool<ManaComponent> _mana = componentManager.GetPackedPool<ManaComponent>();
    private readonly PackedComponentPool<HotkeyExpansionUnlockComponent> _hotkeyExpansionUnlocks = componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>();
    private readonly RadialFillRenderer _radialFill = new(new LabelRenderer(), spriteSheetService, spriteRenderer);

    private Window _hostWindow = null!;
    private SpriteFontBase _font = null!;
    private SpriteFontBase _overlayFont = null!;
    private SpriteFontBase _countdownFont = null!;

    /// <summary>Whether each slot is currently active (usable) -- refreshed once per Update (see RefreshSlotActiveStates), not recomputed during Draw. Deciding *whether* a slot is disabled (locked, unaffordable, out of stock) is state/game logic; Draw only ever asks "is this slot active" and independently decides how that reads visually (see AlphaFor) -- the two are deliberately kept separate rather than DrawActionSlot/DrawItemSlot each computing and returning their own alpha for the rest of DrawSlot to reuse.</summary>
    private readonly Dictionary<HotkeySlot, bool> _slotActiveStates = [];

    /// <summary>Read directly from the pool at construction (not deferred to Initialize/Update) specifically so ShellBootstrapper can read a correct Size immediately after `new HotbarContent(...)`, before the host Window -- which needs that Size to construct itself -- exists at all. Field initializers can't reference another instance field (only the primary constructor's own parameters), hence re-resolving the pool from componentManager here rather than reusing _hotkeyExpansionUnlocks.</summary>
    private short _unlockedExpansionSlots = GetUnlockedExpansionSlots(componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(), world.PlayerEntityId);

    private int _lastLayoutRowsVisible = GetExpansionRowsVisible(GetUnlockedExpansionSlots(componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(), world.PlayerEntityId));
    private int _lastLayoutPagesVisible = GetExpansionPagesVisible(GetUnlockedExpansionSlots(componentManager.GetPackedPool<HotkeyExpansionUnlockComponent>(), world.PlayerEntityId));

    /// <summary>The bar's current total bounding size -- depends on how many Expansion rows/pages are currently revealed, so unlike most HUD content's Size this is an instance property, not a static constant.</summary>
    public Vector2 Size => ComputeSize(GetExpansionRowsVisible(_unlockedExpansionSlots), GetExpansionPagesVisible(_unlockedExpansionSlots));

    /// <summary>
    /// The largest the bar can ever grow to (both Expansion pages, both rows) -- ShellBootstrapper
    /// gives the host Window this as its Layout.MaximumSize (rather than leaving it unset, which
    /// falls back to the initial Size -- see Element.Build) specifically so RefreshLayoutIfChanged's
    /// later SetBounds calls, as more slots unlock, aren't clamped straight back down to whatever
    /// page/row count happened to be visible at construction time. Confirmed bug: a player starting
    /// with exactly the DefaultUnlockedExpansionSlots fallback (page 1 only) baked that width in as
    /// a hard cap, so page 2's slots -- drawn fine by DrawContent/EnumerateSlotBounds, which don't
    /// go through the clamped Rectangle -- were never actually clickable/droppable once revealed.
    /// </summary>
    public static readonly Vector2 MaximumSize = ComputeSize(MaxExpansionRows, MaxExpansionPages);

    private static Vector2 ComputeSize(int rowsVisible, int pagesVisible)
    {
        var baseWidth = BaseSlotCount * SlotSize.X + (BaseSlotCount - 1) * SlotGap;
        var defaultAttackWidth = SlotSize.X;
        var pageWidth = ExpansionColumnsPerRow * SlotSize.X + (ExpansionColumnsPerRow - 1) * SlotGap;
        var expansionWidth = pagesVisible * pageWidth + (pagesVisible - 1) * ExpansionPageGap;
        var totalWidth = baseWidth + GroupGap + defaultAttackWidth + GroupGap + expansionWidth;

        var expansionHeight = rowsVisible * SlotSize.Y + (rowsVisible - 1) * SlotGap;
        return new Vector2(totalWidth, expansionHeight);
    }

    /// <summary>Page 1 (Slot1-10) fills top-to-bottom first -- 1 row until more than a row's worth is unlocked, then 2 (the max -- see this class's own doc comment on pages sitting side by side instead of stacking further).</summary>
    private static int GetExpansionRowsVisible(short unlockedSlots) =>
        unlockedSlots > ExpansionColumnsPerRow ? MaxExpansionRows : 1;

    /// <summary>Page 2 (Slot11-20) only appears once page 1 is entirely unlocked.</summary>
    private static int GetExpansionPagesVisible(short unlockedSlots) =>
        unlockedSlots > SlotsPerExpansionPage ? MaxExpansionPages : 1;

    /// <summary>playerEntityId can still be World's unset sentinel (-1) here -- ShellBootstrapper constructs this class (and reads its Size) before GameLoop's first Update actually spawns the player (see FloorBuilder.CreatePlayer), so a negative id must fall back the same as "no component" rather than indexing the pool with it.</summary>
    private static short GetUnlockedExpansionSlots(PackedComponentPool<HotkeyExpansionUnlockComponent> hotkeyExpansionUnlocks, int playerEntityId) =>
        playerEntityId >= 0 && hotkeyExpansionUnlocks.TryGetReadonly(playerEntityId, out var unlock) ? unlock.UnlockedSlotCount : DefaultUnlockedExpansionSlots;

    public void Initialize(Window hostWindow)
    {
        _hostWindow = hostWindow;
        _font = fontService.GetFont((int)(SlotSize.Y * GlyphSizeFraction));
        _overlayFont = fontService.GetFont((int)(SlotSize.Y * OverlayFontFraction));
        _countdownFont = fontService.GetFont((int)(SlotSize.Y * CountdownFontFraction));
    }

    public void Update(GameTime gameTime)
    {
        _unlockedExpansionSlots = GetUnlockedExpansionSlots(_hotkeyExpansionUnlocks, world.PlayerEntityId);
        RefreshLayoutIfChanged();
        RefreshSlotActiveStates();
    }

    /// <summary>
    /// Whether each slot is disabled -- locked (see IsSlotLocked), an unaffordable action, or an
    /// unusable/out-of-stock item -- is entirely decided here, once per frame, not inside Draw.
    /// playerEntityId can still be -1 on the very first Update (see GetUnlockedExpansionSlots'
    /// own doc comment on why) -- skip entirely rather than indexing the component pools with it;
    /// DrawSlot's own GetValueOrDefault(slot, true) fallback keeps every slot looking active for
    /// that one frame, which is harmless since nothing meaningful is bound before the player
    /// exists anyway.
    /// </summary>
    private void RefreshSlotActiveStates()
    {
        var playerEntityId = world.PlayerEntityId;
        if (playerEntityId < 0)
        {
            return;
        }

        foreach (var entry in HotkeySlotLayout.Entries)
        {
            _slotActiveStates[entry.Slot] = ComputeIsSlotActive(playerEntityId, entry.Slot);
        }
    }

    private bool ComputeIsSlotActive(int playerEntityId, HotkeySlot slot)
    {
        if (IsSlotLocked(slot))
        {
            return false;
        }

        if (ActionHotkeyBindingQueries.TryGet(_actionHotkeyBindings, playerEntityId, slot, out var actionId) && actionCatalog.TryGet(actionId, out var action))
        {
            return HasEnoughMana(playerEntityId, action);
        }

        if (ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, playerEntityId, slot, out var stackInstanceId) && TryResolveBoundItem(playerEntityId, stackInstanceId, out var item, out _))
        {
            return item.Activator is not null;
        }

        // Unbound but unlocked -- empty, not disabled (nothing to fade).
        return true;
    }

    /// <summary>Unlocking more Expansion slots is rare and permanent (see HotkeyExpansion.Apply), so this only actually resizes/repositions the host window on the frame the row/page count changes, not every frame -- centered horizontally and anchored to the same bottom margin as before, recomputed against the new Size so the bar grows upward as rows are added and rightward as page 2 appears (see ShellBootstrapper, which sizes/positions the window identically at construction).</summary>
    private void RefreshLayoutIfChanged()
    {
        var rowsVisible = GetExpansionRowsVisible(_unlockedExpansionSlots);
        var pagesVisible = GetExpansionPagesVisible(_unlockedExpansionSlots);
        if (rowsVisible == _lastLayoutRowsVisible && pagesVisible == _lastLayoutPagesVisible)
        {
            return;
        }

        _lastLayoutRowsVisible = rowsVisible;
        _lastLayoutPagesVisible = pagesVisible;
        var newSize = ComputeSize(rowsVisible, pagesVisible);
        var newPosition = ComputeBottomCenteredPosition(newSize, screenSize);
        _hostWindow.SetBounds(newPosition, newSize);
    }

    /// <summary>Shared by ShellBootstrapper (initial placement) and RefreshLayoutIfChanged (every subsequent resize) so the two can never drift into disagreeing formulas.</summary>
    public static Vector2 ComputeBottomCenteredPosition(Vector2 size, Vector2 screenSize) =>
        new((screenSize.X - size.X) / 2f, screenSize.Y - size.Y - HudMetrics.Margin.Y * 1.5f);

    public void DrawContent(GameTime gameTime)
    {
        var spriteBatch = _hostWindow.ElementPoolService.SpriteBatch;
        var unitRectangle = _hostWindow.ElementPoolService.UnitRectangle;
        var playerEntityId = world.PlayerEntityId;

        foreach (var (slot, bounds) in EnumerateSlotBounds())
        {
            DrawSlot(spriteBatch, unitRectangle, playerEntityId, slot, bounds);

            if (IsAcceptingDrag)
            {
                GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, DragDropTargetGlowColor);
            }
        }
    }

    /// <summary>
    /// Every slot's bounds, in the same order DrawContent walks -- the shared layout math behind
    /// drawing, the drag-drop glow above, and TryGetSlotAt below, so all three can never drift
    /// out of sync with each other the way three independent copies of this walk risked. Left to
    /// right: Base (horizontal row) -> gap -> DefaultAttack (single slot) -> gap -> Expansion,
    /// itself up to two 2x5 pages side by side (page 2 to the right of page 1, with its own
    /// ExpansionPageGap) -- only entry.Row &lt; rowsVisible and entry.Page &lt; pagesVisible slots
    /// are drawn at all; a not-yet-revealed row/page doesn't just render dim, it isn't laid out or
    /// hit-testable yet. Base/DefaultAttack are vertically centered against Expansion's current
    /// height; Expansion itself sits flush at the content origin's Y, which is what makes the bar
    /// grow upward as rows are added (see RefreshLayoutIfChanged's own doc comment).
    /// </summary>
    private IEnumerable<(HotkeySlot Slot, Rectangle Bounds)> EnumerateSlotBounds()
    {
        var rowsVisible = GetExpansionRowsVisible(_unlockedExpansionSlots);
        var pagesVisible = GetExpansionPagesVisible(_unlockedExpansionSlots);
        var expansionHeight = ComputeSize(rowsVisible, pagesVisible).Y;
        var origin = _hostWindow.ContentAbsolutePosition;
        var baseWidth = BaseSlotCount * SlotSize.X + (BaseSlotCount - 1) * SlotGap;
        var centeredRowY = origin.Y + (expansionHeight - SlotSize.Y) / 2f;

        var x = origin.X;
        for (var i = 0; i < BaseSlotCount; i++)
        {
            var slot = (HotkeySlot)((int)HotkeySlot.Base1 + i);
            yield return (slot, new Rectangle((int)x, (int)centeredRowY, (int)SlotSize.X, (int)SlotSize.Y));
            x += SlotSize.X + SlotGap;
        }

        x = origin.X + baseWidth + GroupGap;
        yield return (HotkeySlot.DefaultAttack, new Rectangle((int)x, (int)centeredRowY, (int)SlotSize.X, (int)SlotSize.Y));

        var expansionOriginX = x + SlotSize.X + GroupGap;
        var pageWidth = ExpansionColumnsPerRow * SlotSize.X + (ExpansionColumnsPerRow - 1) * SlotGap;

        foreach (var entry in HotkeySlotLayout.Entries)
        {
            if (entry.Category != HotkeyCategory.Expansion || entry.Row >= rowsVisible || entry.Page >= pagesVisible)
            {
                continue;
            }

            var pageOriginX = expansionOriginX + entry.Page * (pageWidth + ExpansionPageGap);
            var slotX = pageOriginX + entry.Column * (SlotSize.X + SlotGap);
            var slotY = origin.Y + entry.Row * (SlotSize.Y + SlotGap);
            yield return (entry.Slot, new Rectangle((int)slotX, (int)slotY, (int)SlotSize.X, (int)SlotSize.Y));
        }
    }

    /// <summary>Screen-position hit test for exactly which hotbar slot (if any) screenPosition falls within -- UiInputController's content-drag path uses this both as a bind-drop target and, combined with TryGetBoundItemId, to detect a drag starting on an already-bound slot.</summary>
    internal bool TryGetSlotAt(Point screenPosition, out HotkeySlot slot)
    {
        foreach (var (candidateSlot, bounds) in EnumerateSlotBounds())
        {
            if (bounds.Contains(screenPosition))
            {
                slot = candidateSlot;
                return true;
            }
        }

        slot = default;
        return false;
    }

    /// <summary>The item stack (if any) currently bound to slot -- UiInputController's content-drag path reads this at press time to capture the payload of a drag starting on an already-bound hotbar slot.</summary>
    internal bool TryGetBoundItemStackInstanceId(HotkeySlot slot, out Guid stackInstanceId) =>
        ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, world.PlayerEntityId, slot, out stackInstanceId);

    /// <summary>The action (if any) currently bound to slot -- same drag-payload-capture role as TryGetBoundItemId, for a drag starting on an already-bound action slot.</summary>
    internal bool TryGetBoundActionId(HotkeySlot slot, out Guid actionId) =>
        ActionHotkeyBindingQueries.TryGet(_actionHotkeyBindings, world.PlayerEntityId, slot, out actionId);

    /// <summary>slot's bound action/item resolved to a title+summary pair, for the Armed Hotkey
    /// Summary window -- false if the slot has no binding. Summary, not Description: a short,
    /// concrete statement of exact effect meant to be read at a glance in this small window (see
    /// ActionDefinition/ItemDefinition's own doc comments on the Summary vs Description split) --
    /// Description is reserved for future, larger text boxes elsewhere.</summary>
    internal bool TryGetSlotSummary(HotkeySlot slot, out string title, out string summary)
    {
        var playerEntityId = world.PlayerEntityId;

        if (ActionHotkeyBindingQueries.TryGet(_actionHotkeyBindings, playerEntityId, slot, out var actionId) &&
            actionCatalog.TryGet(actionId, out var action))
        {
            title = action.Name;
            summary = action.Summary;
            return true;
        }

        if (ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, playerEntityId, slot, out var stackInstanceId) &&
            TryResolveBoundItem(playerEntityId, stackInstanceId, out var item, out _))
        {
            title = item.Name;
            summary = item.Summary;
            return true;
        }

        title = string.Empty;
        summary = string.Empty;
        return false;
    }

    /// <summary>slot's on-screen bounds -- reuses the same EnumerateSlotBounds walk DrawContent/
    /// TryGetSlotAt already share as their single source of truth.</summary>
    internal Rectangle GetSlotBounds(HotkeySlot slot)
    {
        foreach (var (candidateSlot, bounds) in EnumerateSlotBounds())
        {
            if (candidateSlot == slot)
            {
                return bounds;
            }
        }

        return Rectangle.Empty;
    }

    /// <summary>
    /// Writes (or overwrites) slot's item binding -- clears any existing action or item binding
    /// on that slot first, since a slot binds to at most one of {action, item} at a time (see
    /// IHotkeySlotBinding's own doc comment). Does not touch the inventory stack itself: binding
    /// is a reference, not a transfer (see ItemHotkeyBindingComponent's own doc comment). The
    /// real assignment path, driven by UiInputController's content-drag drop resolution. A
    /// not-yet-unlocked Expansion slot silently refuses the binding -- it isn't a valid drop
    /// target (see this class's own doc comment on the disabled-alpha treatment) -- rather than
    /// UiInputController needing its own separate lock-awareness. Publishes ItemHotkeyBoundEvent
    /// -- ArchivistAchievement's trigger -- deliberately not raised by PlayerBlueprint's own
    /// hardcoded starting binds, which are spawn-time setup, not a player action.
    /// </summary>
    internal void BindItem(HotkeySlot slot, Guid stackInstanceId)
    {
        if (IsSlotLocked(slot))
        {
            return;
        }

        var playerEntityId = world.PlayerEntityId;
        ClearSlotBinding(playerEntityId, slot);
        _itemHotkeyBindings.Add(playerEntityId, new ItemHotkeyBindingComponent(slot, stackInstanceId));

        if (InventoryQueries.TryFindByStackInstanceId(_inventoryStacks, playerEntityId, stackInstanceId, out var stack))
        {
            eventBus.Publish(new ItemHotkeyBoundEvent(playerEntityId, slot, stack.ItemDefinitionId));
        }
    }

    /// <summary>Removes slot's item binding, if any -- dragging a bound item off the hotbar entirely (see UiInputController's content-drag path).</summary>
    internal void UnbindItemSlot(HotkeySlot slot) =>
        ItemHotkeyBindingQueries.Unbind(_itemHotkeyBindings, world.PlayerEntityId, slot);

    /// <summary>Writes (or overwrites) slot's action binding -- mirrors BindItem exactly (clears any existing binding of either kind first, refuses a locked slot, publishes ActionHotkeyBoundEvent), for the same click-and-drag path now covering actions too.</summary>
    internal void BindAction(HotkeySlot slot, Guid actionId)
    {
        if (IsSlotLocked(slot))
        {
            return;
        }

        var playerEntityId = world.PlayerEntityId;
        ClearSlotBinding(playerEntityId, slot);
        _actionHotkeyBindings.Add(playerEntityId, new ActionHotkeyBindingComponent(slot, actionId));
        eventBus.Publish(new ActionHotkeyBoundEvent(playerEntityId, slot, actionId));
    }

    /// <summary>Removes slot's action binding, if any -- mirrors UnbindItemSlot for a bound action dragged off the hotbar entirely.</summary>
    internal void UnbindActionSlot(HotkeySlot slot) =>
        ActionHotkeyBindingQueries.Unbind(_actionHotkeyBindings, world.PlayerEntityId, slot);

    /// <summary>
    /// Resolves a completed content-drag drop in one call -- unbinds originSlot first (if the
    /// drag started on an already-bound hotbar slot), then binds destinationSlot (if the release
    /// landed on a valid one) to payloadId. Dropping back onto the same slot it came from is
    /// therefore a harmless unbind-then-immediately-rebind, not a special case. Either
    /// originSlot or destinationSlot may be absent on their own (never both -- the caller has
    /// nothing to do at all in that case and doesn't call this): a drag that started on an
    /// inventory cell, not a hotbar slot, has no origin to unbind; a drop that missed every
    /// hotbar slot has no destination to bind.
    ///
    /// This is the one place the unbind-then-bind sequencing rule lives, next to the rest of
    /// this class's own binding rules (locked slots, mutual exclusivity -- see BindItem/BindAction),
    /// rather than in UiInputController's drag-resolution code. UiInputController still owns
    /// gesture recognition (tap-vs-drag threshold, hit-testing the release position) -- only it
    /// has visibility across the whole press-to-release gesture, which can start on a different
    /// source Element entirely (e.g. InventoryItemStackCell); this method only knows the
    /// hotbar-slot half of that, handed to it already resolved.
    /// </summary>
    internal void ResolveDroppedBinding(HotkeySlot? originSlot, HotkeySlot? destinationSlot, bool isActionDrag, Guid payloadId)
    {
        if (originSlot is { } slot)
        {
            if (isActionDrag)
            {
                UnbindActionSlot(slot);
            }
            else
            {
                UnbindItemSlot(slot);
            }
        }

        if (destinationSlot is { } destination)
        {
            if (isActionDrag)
            {
                BindAction(destination, payloadId);
            }
            else
            {
                BindItem(destination, payloadId);
            }
        }
    }

    /// <summary>Shared by BindItem/BindAction -- a slot binds to at most one of {action, item} at a time (see IHotkeySlotBinding's own doc comment), so writing a new binding of either kind always clears both pools for that slot first.</summary>
    private void ClearSlotBinding(int playerEntityId, HotkeySlot slot)
    {
        ActionHotkeyBindingQueries.Unbind(_actionHotkeyBindings, playerEntityId, slot);
        ItemHotkeyBindingQueries.Unbind(_itemHotkeyBindings, playerEntityId, slot);
    }

    /// <summary>Delegates to HotkeySlotLayout.IsLocked -- shared with ActionTargetingController's own activation gate, so rendering and activation can't disagree about which slots are actually usable.</summary>
    private bool IsSlotLocked(HotkeySlot slot) => HotkeySlotLayout.IsLocked(slot, _unlockedExpansionSlots);

    /// <summary>
    /// The action/item-agnostic shape DrawSlot actually renders -- BuildActionVisual/
    /// BuildItemVisual are the only two places that know how to turn a binding into one of these;
    /// everything downstream (radial fill, badge, countdown) is drawn identically regardless of
    /// which kind produced it. BadgeBottomLeft picks between an action's mana-cost badge
    /// (bottom-left) and an item's stack-count badge (bottom-center) -- the two badges never
    /// coexist since a slot binds to at most one of {action, item}.
    /// </summary>
    private readonly record struct SlotVisual(
        string? SpriteName,
        string Glyph,
        Color GlyphColor,
        float FillPercentage,
        string? BadgeText,
        bool BadgeBottomLeft,
        int? CountdownSecondsAboveSlot);

    private void DrawSlot(SpriteBatch spriteBatch, Texture2D unitRectangle, int playerEntityId, HotkeySlot slot, Rectangle bounds)
    {
        var contentBounds = BorderThickness.Inset(bounds, SlotBorderThickness);
        var isActive = _slotActiveStates.GetValueOrDefault(slot, true);
        var alpha = AlphaFor(isActive);

        if (ActionHotkeyBindingQueries.TryGet(_actionHotkeyBindings, playerEntityId, slot, out var actionId) && actionCatalog.TryGet(actionId, out var action))
        {
            DrawSlotVisual(spriteBatch, unitRectangle, bounds, contentBounds, BuildActionVisual(playerEntityId, action, isActive), alpha);
        }
        else if (ItemHotkeyBindingQueries.TryGet(_itemHotkeyBindings, playerEntityId, slot, out var stackInstanceId) && TryResolveBoundItem(playerEntityId, stackInstanceId, out var item, out var stack))
        {
            DrawSlotVisual(spriteBatch, unitRectangle, bounds, contentBounds, BuildItemVisual(playerEntityId, item, stack, isActive), alpha);

            if (stackInstanceId == mapViewState.SelectedItemStackInstanceId)
            {
                GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, SelectedGlowColor);
            }
        }
        else
        {
            // Unbound -- either genuinely empty (isActive true) or a not-yet-unlocked Expansion
            // slot (isActive false, see ComputeIsSlotActive) -- either way, just the flat tint.
            spriteBatch.Draw(unitRectangle, contentBounds, UnboundSlotColor * alpha);
        }

        var (top, bottom, left, right) = BorderThickness.GetEdgeRectangles(bounds, SlotBorderThickness);
        BorderRenderer.Draw(spriteBatch, unitRectangle, BorderStyle.FlatContrast, Color.White, top, bottom, left, right, alpha);

        if (mapViewState.ArmedSlot == slot)
        {
            GlowRenderer.Draw(spriteBatch, unitRectangle, bounds, ArmedGlowColor);
        }

        DrawKeyLabel(spriteBatch, bounds, slot, alpha);
    }

    /// <summary>The one place isActive turns into an opacity -- every draw call in DrawSlot (border, icon, every text overlay) goes through this same mapping, rather than each piece deciding its own alpha.</summary>
    private static float AlphaFor(bool isActive) => isActive ? 1f : DisabledSlotAlpha;

    /// <summary>isActive (see RefreshSlotActiveStates -- an Update-time decision) drives both the icon's opacity and whether the cooldown/lock wedge shows at all: an inactive (unaffordable) action suppresses the radial fill entirely (0f) rather than showing a mask that would read as "almost ready" when it's actually just unaffordable.</summary>
    private SlotVisual BuildActionVisual(int playerEntityId, ActionDefinition action, bool isActive)
    {
        var manaCost = SpellActivator.ManaCostOf(action.Activator);
        return new SlotVisual(
            SpriteName: action.SpriteName,
            Glyph: action.Glyph,
            GlyphColor: action.GlyphColor,
            FillPercentage: isActive ? ComputeActionFillPercentage(playerEntityId, action) : 0f,
            BadgeText: manaCost > 0 ? manaCost.ToString() : null,
            BadgeBottomLeft: true,
            CountdownSecondsAboveSlot: null);
    }

    /// <summary>Mirrors ActionActivationSystem/ActionTargetingController's own gate (see either's doc comment) -- a zero-cost action (e.g. Punch) always passes.</summary>
    private bool HasEnoughMana(int playerEntityId, ActionDefinition action)
    {
        var manaCost = SpellActivator.ManaCostOf(action.Activator);
        return manaCost <= 0 || (_mana.TryGetReadonly(playerEntityId, out var mana) && mana.CurrentMana >= manaCost);
    }

    /// <summary>
    /// Resolves the stack bound to a hotkey slot (by StackInstanceId, see ItemHotkeyBindingComponent's
    /// own doc comment) to both its effective ItemDefinition (its own Override if diverged, else the
    /// plain catalog lookup) and the underlying stack itself -- callers need the stack directly
    /// (e.g. BuildItemVisual's quantity/charges) rather than re-resolving it a second time by item
    /// id, which could find the *wrong* stack once more than one diverged stack of the same
    /// ItemDefinitionId can exist side by side.
    /// </summary>
    private bool TryResolveBoundItem(int playerEntityId, Guid stackInstanceId, out ItemDefinition item, out InventoryItemStackComponent stack)
    {
        if (!InventoryQueries.TryFindByStackInstanceId(_inventoryStacks, playerEntityId, stackInstanceId, out stack))
        {
            item = null!;
            return false;
        }

        return InventoryQueries.TryResolveEffectiveItem(itemCatalog, in stack, out item);
    }

    /// <summary>
    /// isActive (see RefreshSlotActiveStates -- an Update-time decision) drives both the icon's
    /// opacity and whether the cooldown/lock wedge shows at all, the same as BuildActionVisual.
    /// The potion-cooldown countdown is independent of isActive/stock -- the cooldown is the
    /// consumer's own status, still meaningful even once this slot's item is out of stock (e.g.
    /// right after using the last one while abusing the cooldown) -- see DrawSlotVisual, which
    /// always draws it at full alpha regardless of the slot's own disabled state
    /// (PotionCooldownEffects' own doc comment: the cooldown never blocks a second potion in the
    /// first place). Takes the already-resolved stack directly (see TryResolveBoundItem) rather
    /// than re-looking it up by item id, so a slot bound to one specific divergent stack (e.g. a
    /// wand) always shows *that* stack's own Quantity/charges, not whichever stack of the same
    /// item id a fresh lookup happened to find first.
    /// </summary>
    private SlotVisual BuildItemVisual(int playerEntityId, ItemDefinition item, InventoryItemStackComponent stack, bool isActive)
    {
        var quantity = stack.Quantity;

        var countdownSeconds = item.Activator is PotionActivator &&
            _potionCooldowns.TryGetReadonly(playerEntityId, out var cooldown) && cooldown.FramesRemaining > 0
                ? PotionCooldownEffects.RemainingSeconds(cooldown.FramesRemaining)
                : (int?)null;

        return new SlotVisual(
            SpriteName: item.SpriteName,
            Glyph: item.Glyph,
            GlyphColor: BoundSlotGlyphColor,
            FillPercentage: isActive ? ComputeItemFillPercentage(playerEntityId) : 0f,
            BadgeText: item.Activator is WandActivator wandActivator
                ? $"{wandActivator.Charges}/{wandActivator.MaxCharges}"
                : quantity > 1
                    ? $"x{quantity}"
                    : null,
            BadgeBottomLeft: false,
            CountdownSecondsAboveSlot: countdownSeconds);
    }

    /// <summary>The one place a SlotVisual actually gets drawn -- radial fill/icon, then its badge (mana cost bottom-left, or stack count bottom-center) and countdown, if any. Shared by both BuildActionVisual and BuildItemVisual outputs, regardless of which kind produced them.</summary>
    private void DrawSlotVisual(SpriteBatch spriteBatch, Texture2D unitRectangle, Rectangle bounds, Rectangle contentBounds, SlotVisual visual, float alpha)
    {
        _radialFill.Sprite = visual.SpriteName is not null && SpriteManifest.TryGet(visual.SpriteName, out var sprite) ? sprite : null;
        _radialFill.SpriteTint = Color.White;
        _radialFill.Glyph = visual.Glyph;
        _radialFill.GlyphColor = visual.GlyphColor;
        _radialFill.BackgroundColor = BoundSlotBackgroundColor;
        _radialFill.FillPercentage = visual.FillPercentage;
        _radialFill.Draw(spriteBatch, unitRectangle, _font, contentBounds, alpha);

        if (visual.BadgeText is not null)
        {
            if (visual.BadgeBottomLeft)
            {
                DrawBottomLeftText(spriteBatch, bounds, visual.BadgeText, alpha);
            }
            else
            {
                DrawBottomCenterText(spriteBatch, bounds, visual.BadgeText, alpha);
            }
        }

        if (visual.CountdownSecondsAboveSlot is { } seconds)
        {
            DrawCountdownAboveSlot(spriteBatch, bounds, seconds);
        }
    }

    /// <summary>No symbol here (contrast PlayerStatusEffectsContent's status-bar icon) -- just the number, centered above the slot it belongs to.</summary>
    private void DrawCountdownAboveSlot(SpriteBatch spriteBatch, Rectangle slotBounds, int remainingSeconds)
    {
        var text = remainingSeconds.ToString();
        var textSize = _countdownFont.MeasureString(text);
        var textPosition = new Vector2(slotBounds.Center.X - textSize.X / 2f, slotBounds.Y - textSize.Y - CountdownTextGap);

        ContrastTextRenderer.Draw(spriteBatch, _countdownFont, text, textPosition);
    }

    /// <summary>Top-left of every slot -- the bind key from HotkeySlotLayout (e.g. "Q", "1", "↑1" for a Shift-page Expansion slot). Drawn even on a locked slot (dimmed like everything else there), a preview of what the key will do once unlocked.</summary>
    private void DrawKeyLabel(SpriteBatch spriteBatch, Rectangle bounds, HotkeySlot slot, float alpha)
    {
        var text = HotkeySlotLayout.GetKeyLabel(slot);
        var position = new Vector2(bounds.X + OverlayPadding.X, bounds.Y + OverlayPadding.Y);
        ContrastTextRenderer.Draw(spriteBatch, _overlayFont, text, position, alpha);
    }

    /// <summary>Bottom-left -- an action's mana cost, only ever called when ManaCost > 0.</summary>
    private void DrawBottomLeftText(SpriteBatch spriteBatch, Rectangle bounds, string text, float alpha)
    {
        var textSize = _overlayFont.MeasureString(text);
        var position = new Vector2(bounds.X + OverlayPadding.X, bounds.Bottom - textSize.Y - OverlayPadding.Y);
        ContrastTextRenderer.Draw(spriteBatch, _overlayFont, text, position, alpha);
    }

    /// <summary>Bottom-center -- an item stack's quantity as "x{n}", only ever called when quantity > 1. Replaces the old bottom-right ItemIconRenderer.DrawQuantityBadge call -- ItemIconRenderer itself is untouched, InventoryGridContent/InventoryItemStackCell still use it for the inventory grid's own cells.</summary>
    private void DrawBottomCenterText(SpriteBatch spriteBatch, Rectangle bounds, string text, float alpha)
    {
        var textSize = _overlayFont.MeasureString(text);
        var position = new Vector2(bounds.Center.X - textSize.X / 2f, bounds.Bottom - textSize.Y - OverlayPadding.Y);
        ContrastTextRenderer.Draw(spriteBatch, _overlayFont, text, position, alpha);
    }

    /// <summary>
    /// The granted instance's own cooldown fraction (any category, if it has one) whenever it's
    /// actually counting down; only otherwise falls back to the shared ActionLock's fraction
    /// (Immediate/Delayed only -- FreeCast bypasses the shared lock entirely).
    ///
    /// The shared ActionLock is genuinely shared across every Immediate/Delayed action, not
    /// scoped to whichever one actually set it -- so if this were always taken as
    /// Math.Max(lockFraction, cooldownFraction), using a *different* action (e.g. Default
    /// Attack's short windup) while this one's own, longer, real cooldown is ticking down would
    /// make this icon spike to whatever fraction that unrelated windup happens to be at, then
    /// visibly snap back down once the windup ends -- confusing/wrong, since nothing about this
    /// action's own readiness actually changed. Preferring cooldownFraction whenever it's
    /// nonzero avoids that: this action's own cooldown is always the more specific, more
    /// correct signal once it exists, so the shared lock only ever matters for an action with no
    /// cooldown of its own (e.g. Default Attack), where it's still the only fill signal available.
    /// </summary>
    private float ComputeActionFillPercentage(int playerEntityId, ActionDefinition action)
    {
        var cooldownFraction = 0f;
        if (action.Activator.Timing.CooldownFrames is { } cooldownFrames &&
            cooldownFrames > 0 &&
            ActionInstanceQueries.TryGet(_actionInstances, playerEntityId, action.Id, out var instance))
        {
            cooldownFraction = (float)instance.CooldownFramesRemaining / cooldownFrames;
        }

        if (cooldownFraction > 0f)
        {
            return cooldownFraction;
        }

        if (action.Activator.Timing.Category != ActionTimingCategory.FreeCast &&
            _actionLocks.TryGetReadonly(playerEntityId, out var actionLock) &&
            actionLock.CurrentLockTotalFrames > 0)
        {
            return (float)actionLock.CurrentLockFramesRemaining / actionLock.CurrentLockTotalFrames;
        }

        return 0f;
    }

    /// <summary>
    /// Items have no per-instance cooldown the way actions do -- ConsumableActivationSystem
    /// only ever sets the shared ActionLock (see PotionActivator.Timing.ActionLockFrames' own doc
    /// comment), so that's the only fill signal here. Deliberately not PotionCooldownComponent:
    /// that cooldown never blocks a second potion (see PotionCooldownEffects' own doc comment),
    /// so masking the slot as if it were on cooldown would be actively misleading -- it gets its
    /// own separate, informational display instead (see BuildItemVisual's countdown text).
    /// </summary>
    private float ComputeItemFillPercentage(int playerEntityId)
    {
        if (_actionLocks.TryGetReadonly(playerEntityId, out var actionLock) && actionLock.CurrentLockTotalFrames > 0)
        {
            return (float)actionLock.CurrentLockFramesRemaining / actionLock.CurrentLockTotalFrames;
        }

        return 0f;
    }
}
